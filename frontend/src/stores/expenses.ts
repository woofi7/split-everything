import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  db,
  type LocalComment,
  type LocalExpense,
  type LocalItem,
  type LocalSettlement,
  type OutboxOperation,
} from '@/offline/db'
import { calculateItemizedSplit, calculateSplit, type SplitType } from '@/domain/splitting'
import { netBalances, simplifyDebts, pairwiseDebts, type MemberBalance, type Transfer } from '@/domain/balances'
import { roundMoney } from '@/domain/money'
import { clearReplica } from '@/offline/db'
import type { SyncEngine } from '@/offline/syncEngine'
import { ApiError, looksOffline, type ApiClient } from '@/api/client'
import { newId } from '@/domain/ids'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'

export interface ExpenseDraft {
  groupId: string
  paidByMemberId: string
  payers?: Array<{ memberId: string; amount: number }>
  description: string
  amount: number
  currency: string
  spentAt: Date
  splitType: SplitType
  participantIds: string[]
  splitValues?: Record<string, number>
  items?: LocalItem[]
  receiptId?: string | null
  notes?: string | null
  categoryKey?: string | null
}

export interface CrossGroupGroup {
  groupId: string
  groupName: string
  currency: string
  net: number
  canSettle: boolean
}

export interface PlannedOffset {
  owedGroupId: string
  owedGroupName: string
  owingGroupId: string
  owingGroupName: string
  amount: number
  currency: string
}

export interface CrossGroupRemainder {
  currency: string
  net: number
  groupId: string | null
  groupName: string | null
}

export interface CrossGroupBalance {
  withUserId: string
  withName: string
  groups: CrossGroupGroup[]
  offsets: PlannedOffset[]
  remaining: CrossGroupRemainder[]
}

export interface OffsetResult {
  applied: PlannedOffset[]
  remaining: CrossGroupRemainder[]
  settlementsRecorded: number
}

export interface SettlementDraft {
  groupId: string
  fromMemberId: string
  toMemberId: string
  amount: number
  currency: string
  settledAt?: Date
  note?: string | null
  receiptId?: string | null
}

export const useExpensesStore = defineStore('expenses', () => {
  const expenses = ref<LocalExpense[]>([])
  const settlements = ref<LocalSettlement[]>([])
  const comments = ref<LocalComment[]>([])
  const pendingCount = ref(0)
  const rejectedCount = ref(0)
  const isSyncing = ref(false)
  let engine: SyncEngine | null = null
  let api: ApiClient | null = null

  function attachSync(syncEngine: SyncEngine): void {
    engine = syncEngine
  }

  function attachApi(client: ApiClient): void {
    api = client
  }

  function requireSync(): SyncEngine {
    if (!engine) throw new Error('The expenses store has no sync engine attached.')
    return engine
  }

  function requireApi(): ApiClient {
    if (!api) throw new Error('The expenses store has no API client attached.')
    return api
  }

  async function hydrate(): Promise<void> {
    expenses.value = await db.expenses.toArray()
    settlements.value = await db.settlements.toArray()
    comments.value = await db.comments.toArray()
    await refreshPendingCount()
  }

  const forGroup = (groupId: string): LocalExpense[] =>
    expenses.value
      .filter((expense) => expense.groupId === groupId && !expense.isDeleted)
      .slice()
      .sort((left, right) => right.spentAt.localeCompare(left.spentAt))

  const settlementsForGroup = (groupId: string): LocalSettlement[] =>
    settlements.value
      .filter((settlement) => settlement.groupId === groupId && !settlement.isDeleted)
      .slice()
      .sort((left, right) => right.settledAt.localeCompare(left.settledAt))

  const commentsFor = (expenseId: string): LocalComment[] =>
    comments.value
      .filter((comment) => comment.expenseId === expenseId && !comment.isDeleted)
      .slice()
      .sort((left, right) => left.createdAt.localeCompare(right.createdAt))

  const unsyncedExpenses = computed(() => expenses.value.filter((expense) => expense.pending))

  async function add(draft: ExpenseDraft): Promise<LocalExpense> {
    const group = await requireGroup(draft.groupId)

    const description = draft.description.trim()
    if (!description) throw new Error('An expense needs a description.')
    if (!(draft.amount > 0)) throw new Error('An expense amount must be greater than zero.')
    if (draft.participantIds.length === 0) throw new Error('An expense needs at least one participant.')

    const memberIds = new Set(group.members.map((member) => member.id))
    if (!memberIds.has(draft.paidByMemberId)) {
      throw new Error('The payer must be a member of this group.')
    }
    const contributions = readPayers(draft, memberIds)
    for (const participant of draft.participantIds) {
      if (!memberIds.has(participant)) {
        throw new Error('Every participant must be a member of this group.')
      }
    }

    const shares = computeShares(draft)

    const isBaseCurrency = draft.currency === group.baseCurrency
    const expense: LocalExpense = {
      id: newId(),
      groupId: draft.groupId,
      paidByMemberId: mainPayer(contributions),
      payers: contributions.map((payer) => ({
        memberId: payer.memberId,
        amount: payer.amount,
        amountInBaseCurrency: payer.amount,
      })),
      description,
      amount: roundMoney(draft.amount, draft.currency),
      currency: draft.currency,
      amountInBaseCurrency: roundMoney(draft.amount, draft.currency),
      exchangeRate: 1,
      spentAt: draft.spentAt.toISOString(),
      splitType: draft.splitType,
      receiptId: draft.receiptId ?? null,
      notes: draft.notes ?? null,
      categoryKey: draft.categoryKey ?? null,
      splits: shares.map((share) => ({
        memberId: share.memberId,
        amount: share.amount,
        amountInBaseCurrency: isBaseCurrency ? share.amount : share.amount,
        inputValue: share.inputValue,
      })),
      items: draft.items ?? [],
      revision: 1,
      isDeleted: false,
      vectorClock: {},
      serverSeq: 0,
      pending: true,
    }

    await db.expenses.put(expense)
    expenses.value = [...expenses.value, expense]

    const operation = await requireSync().enqueue({
      entityType: 'Expense',
      entityId: expense.id,
      operation: 'Create',
      groupId: expense.groupId,
      payload: toWirePayload(expense),
    })

    await patch(expense.id, { vectorClock: operation.vectorClock })
    await refreshPendingCount()
    syncSoon()

    return (await db.expenses.get(expense.id))!
  }

  async function edit(
    expenseId: string,
    changes: Partial<Pick<ExpenseDraft, 'description' | 'amount' | 'currency' | 'spentAt' | 'splitType' | 'participantIds' | 'splitValues' | 'items' | 'notes' | 'receiptId' | 'paidByMemberId' | 'payers' | 'categoryKey'>>,
  ): Promise<LocalExpense> {
    const existing = await db.expenses.get(expenseId)
    if (!existing) throw new Error('That expense is not on this device.')

    await requireGroup(existing.groupId)

    const description = (changes.description ?? existing.description).trim()
    if (!description) throw new Error('An expense needs a description.')

    const amount = changes.amount ?? existing.amount
    if (!(amount > 0)) throw new Error('An expense amount must be greater than zero.')

    const participantIds =
      changes.participantIds ?? existing.splits.map((split) => split.memberId)

    const group = await requireGroup(existing.groupId)
    const roster = new Set(group.members.map((member) => member.id))

    const contributions = changes.payers
      ? readPayers(
          {
            payers: changes.payers,
            paidByMemberId: changes.paidByMemberId ?? existing.paidByMemberId,
            amount,
            currency: changes.currency ?? existing.currency,
          },
          roster,
        )
      : changes.paidByMemberId
        ? [{ memberId: changes.paidByMemberId, amount }]
        : keepPayers(existing, amount, changes.currency ?? existing.currency)

    const shares = computeShares({
      groupId: existing.groupId,
      paidByMemberId: changes.paidByMemberId ?? existing.paidByMemberId,
      description,
      amount,
      currency: changes.currency ?? existing.currency,
      spentAt: changes.spentAt ?? new Date(existing.spentAt),
      splitType: changes.splitType ?? existing.splitType,
      participantIds,
      splitValues:
        changes.splitValues ??
        Object.fromEntries(
          existing.splits
            .filter((split) => split.inputValue !== null)
            .map((split) => [split.memberId, split.inputValue as number]),
        ),
      items: changes.items ?? existing.items,
    })

    const updated: LocalExpense = {
      ...existing,
      paidByMemberId: mainPayer(contributions),
      payers: contributions.map((payer) => ({
        memberId: payer.memberId,
        amount: payer.amount,
        amountInBaseCurrency: payer.amount,
      })),
      description,
      amount: roundMoney(amount, changes.currency ?? existing.currency),
      currency: changes.currency ?? existing.currency,
      amountInBaseCurrency: roundMoney(amount, changes.currency ?? existing.currency),
      spentAt: (changes.spentAt ?? new Date(existing.spentAt)).toISOString(),
      splitType: changes.splitType ?? existing.splitType,
      receiptId: changes.receiptId ?? existing.receiptId,
      notes: changes.notes ?? existing.notes,
      categoryKey: changes.categoryKey === undefined ? existing.categoryKey : changes.categoryKey,
      items: changes.items ?? existing.items,
      splits: shares.map((share) => ({
        memberId: share.memberId,
        amount: share.amount,
        amountInBaseCurrency: share.amount,
        inputValue: share.inputValue,
      })),
      revision: existing.revision + 1,
      pending: true,
    }

    await db.expenses.put(updated)
    replaceExpense(updated)

    const operation = await requireSync().enqueue({
      entityType: 'Expense',
      entityId: updated.id,
      operation: 'Update',
      groupId: updated.groupId,
      payload: toWirePayload(updated),
    })

    await patch(updated.id, { vectorClock: operation.vectorClock })
    await refreshPendingCount()
    syncSoon()

    return (await db.expenses.get(updated.id))!
  }

  async function refile(expenseIds: string[], categoryKey: string | null): Promise<number> {
    const next = categoryKey ?? null
    const touched: LocalExpense[] = []
    const known = new Set<string>()

    for (const expenseId of expenseIds) {
      const existing = await db.expenses.get(expenseId)
      if (!existing || existing.isDeleted) continue
      if ((existing.categoryKey ?? null) === next) continue

      if (!known.has(existing.groupId)) {
        await requireGroup(existing.groupId)
        known.add(existing.groupId)
      }

      const updated: LocalExpense = {
        ...existing,
        categoryKey: next,
        revision: existing.revision + 1,
        pending: true,
      }

      await db.expenses.put(updated)

      const operation = await requireSync().enqueue({
        entityType: 'Expense',
        entityId: updated.id,
        operation: 'Update',
        groupId: updated.groupId,
        payload: toWirePayload(updated),
      })

      touched.push({ ...updated, vectorClock: operation.vectorClock })
    }

    if (touched.length === 0) return 0

    await db.expenses.bulkPut(touched)

    const byId = new Map(touched.map((expense) => [expense.id, expense]))
    const seen = new Set<string>()
    const merged = expenses.value.map((expense) => {
      const replacement = byId.get(expense.id)
      if (!replacement) return expense
      seen.add(expense.id)
      return replacement
    })

    for (const expense of touched) if (!seen.has(expense.id)) merged.push(expense)
    expenses.value = merged

    await refreshPendingCount()
    syncSoon()

    return touched.length
  }

  async function transfer(
    expenseId: string,
    targetGroupId: string,
    memberMapping?: Record<string, string>,
  ): Promise<void> {
    const existing = await db.expenses.get(expenseId)
    if (!existing) throw new Error('That expense is not on this device.')
    if (existing.groupId === targetGroupId) throw new Error('That expense is already in this group.')

    const queued = await db.outbox
      .where('entityId')
      .equals(expenseId)
      .filter((operation) => operation.status === 'pending' || operation.status === 'inflight')
      .count()

    if (queued > 0 || existing.pending) {
      throw new Error('This expense has changes that have not been sent yet. Try again once it has synced.')
    }

    const client = requireApi()

    try {
      await client.post(`/expenses/${expenseId}/transfer`, {
        targetGroupId,
        memberMapping:
          memberMapping && Object.keys(memberMapping).length > 0 ? memberMapping : undefined,
      })
    } catch (caught) {
      if ((caught instanceof ApiError && caught.isOffline) || looksOffline(caught)) {
        throw new Error('Moving an expense between groups needs a connection.', {
          cause: caught,
        })
      }
      throw caught
    }

    await sync()
    await useGroupsStore().loadAll()
  }

  async function crossGroupBalance(withUserId: string): Promise<CrossGroupBalance> {
    return requireApi().get<CrossGroupBalance>('/settlements/cross-group', { withUserId })
  }

  async function offsetAcrossGroups(withUserId: string, note?: string): Promise<OffsetResult> {
    const client = requireApi()

    let result: OffsetResult
    try {
      result = await client.post<OffsetResult>('/settlements/cross-group/offset', {
        withUserId,
        note: note?.trim() || null,
      })
    } catch (caught) {
      if ((caught instanceof ApiError && caught.isOffline) || looksOffline(caught)) {
        throw new Error('Cancelling debts across groups needs a connection.', { cause: caught })
      }
      throw caught
    }

    await sync()
    await useGroupsStore().loadAll()

    return result
  }

  async function remove(expenseId: string): Promise<void> {
    const existing = await db.expenses.get(expenseId)
    if (!existing) throw new Error('That expense is not on this device.')

    const tombstoned = { ...existing, isDeleted: true, pending: true }
    await db.expenses.put(tombstoned)
    replaceExpense(tombstoned)

    await requireSync().enqueue({
      entityType: 'Expense',
      entityId: expenseId,
      operation: 'Delete',
      groupId: existing.groupId,
      payload: { id: expenseId },
    })

    await refreshPendingCount()
    syncSoon()
  }

  async function comment(expenseId: string, body: string, authorMemberId: string): Promise<LocalComment> {
    const trimmed = body.trim()
    if (!trimmed) throw new Error('A comment needs some text.')

    const expense = await db.expenses.get(expenseId)
    if (!expense) throw new Error('That expense is not on this device.')

    const entity: LocalComment = {
      id: newId(),
      expenseId,
      groupId: expense.groupId,
      authorMemberId,
      parentCommentId: null,
      body: trimmed,
      createdAt: new Date().toISOString(),
      isDeleted: false,
      vectorClock: {},
      pending: true,
    }

    await db.comments.put(entity)
    comments.value = [...comments.value, entity]

    await requireSync().enqueue({
      entityType: 'ExpenseComment',
      entityId: entity.id,
      operation: 'Create',
      groupId: entity.groupId,
      payload: toCommentPayload(entity),
    })

    await refreshPendingCount()
    syncSoon()
    return entity
  }

  async function removeComment(commentId: string): Promise<void> {
    const existing = await db.comments.get(commentId)
    if (!existing || existing.isDeleted) {
      throw new Error('That comment is not on this device.')
    }

    const tombstoned = { ...existing, isDeleted: true, pending: true }
    await db.comments.put(tombstoned)
    replaceComment(tombstoned)

    await requireSync().enqueue({
      entityType: 'ExpenseComment',
      entityId: commentId,
      operation: 'Delete',
      groupId: existing.groupId,
      payload: { id: commentId },
    })

    await refreshPendingCount()
    syncSoon()
  }

  function replaceComment(comment: LocalComment): void {
    const index = comments.value.findIndex((candidate) => candidate.id === comment.id)
    if (index >= 0) comments.value[index] = comment
    else comments.value.push(comment)
    comments.value = [...comments.value]
  }

  async function settle(draft: SettlementDraft): Promise<LocalSettlement> {
    if (!(draft.amount > 0)) throw new Error('A settlement amount must be greater than zero.')
    if (draft.fromMemberId === draft.toMemberId) {
      throw new Error('A settlement needs two different members.')
    }

    const group = await requireGroup(draft.groupId)
    const memberIds = new Set(group.members.map((member) => member.id))
    if (!memberIds.has(draft.fromMemberId) || !memberIds.has(draft.toMemberId)) {
      throw new Error('Both sides of a settlement must be members of this group.')
    }

    const entity: LocalSettlement = {
      id: newId(),
      groupId: draft.groupId,
      fromMemberId: draft.fromMemberId,
      toMemberId: draft.toMemberId,
      amount: roundMoney(draft.amount, draft.currency),
      currency: draft.currency,
      amountInBaseCurrency: roundMoney(draft.amount, draft.currency),
      settledAt: (draft.settledAt ?? new Date()).toISOString(),
      note: draft.note ?? null,
      isDeleted: false,
      vectorClock: {},
      serverSeq: 0,
      pending: true,
    }

    await db.settlements.put(entity)
    settlements.value = [...settlements.value, entity]

    await requireSync().enqueue({
      entityType: 'Settlement',
      entityId: entity.id,
      operation: 'Create',
      groupId: entity.groupId,
      payload: toSettlementPayload(entity),
    })

    await refreshPendingCount()
    syncSoon()
    return entity
  }

  async function unsettle(settlementId: string): Promise<void> {
    const existing = await db.settlements.get(settlementId)
    if (!existing) throw new Error('That settlement is not on this device.')

    const tombstoned = { ...existing, isDeleted: true, pending: true }
    await db.settlements.put(tombstoned)
    replaceSettlement(tombstoned)

    await requireSync().enqueue({
      entityType: 'Settlement',
      entityId: settlementId,
      operation: 'Delete',
      groupId: existing.groupId,
      payload: { id: settlementId },
    })

    await refreshPendingCount()
    syncSoon()
  }

  function replaceSettlement(settlement: LocalSettlement): void {
    const index = settlements.value.findIndex((candidate) => candidate.id === settlement.id)
    if (index >= 0) settlements.value[index] = settlement
    else settlements.value.push(settlement)
    settlements.value = [...settlements.value]
  }

  function payersOf(expense: LocalExpense): Array<{ memberId: string; amount: number }> {
    if (expense.payers && expense.payers.length > 0) {
      return expense.payers.map((payer) => ({
        memberId: payer.memberId,
        amount: payer.amountInBaseCurrency,
      }))
    }

    return [{ memberId: expense.paidByMemberId, amount: expense.amountInBaseCurrency }]
  }

  function balanceFor(groupId: string): MemberBalance[] {
    const groupExpenses = forGroup(groupId).map((expense) => ({
      payers: payersOf(expense),
      splits: expense.splits.map((split) => ({
        memberId: split.memberId,
        amount: split.amountInBaseCurrency,
      })),
    }))

    const groupSettlements = settlementsForGroup(groupId).map((settlement) => ({
      fromMemberId: settlement.fromMemberId,
      toMemberId: settlement.toMemberId,
      amount: settlement.amountInBaseCurrency,
    }))

    const memberIds = new Set<string>()
    for (const expense of groupExpenses) {
      for (const payer of expense.payers) memberIds.add(payer.memberId)
      for (const split of expense.splits) memberIds.add(split.memberId)
    }
    for (const settlement of groupSettlements) {
      memberIds.add(settlement.fromMemberId)
      memberIds.add(settlement.toMemberId)
    }

    return netBalances([...memberIds], groupExpenses, groupSettlements)
  }

  function settleUpPlan(groupId: string): Transfer[] {
    return simplifyDebts(balanceFor(groupId))
  }

  function rawDebts(groupId: string): Transfer[] {
    const groupExpenses = forGroup(groupId).map((expense) => ({
      payers: payersOf(expense),
      splits: expense.splits.map((split) => ({
        memberId: split.memberId,
        amount: split.amountInBaseCurrency,
      })),
    }))

    const groupSettlements = settlementsForGroup(groupId).map((settlement) => ({
      fromMemberId: settlement.fromMemberId,
      toMemberId: settlement.toMemberId,
      amount: settlement.amountInBaseCurrency,
    }))

    return pairwiseDebts(groupExpenses, groupSettlements)
  }

  async function reconcile(): Promise<void> {
    const parked = await db.outbox.where('status').equals('rejected').toArray()
    for (const operation of parked) await requireSync().retry(operation.operationId)

    const queued = new Set((await db.outbox.toArray()).map((operation) => operation.entityId))

    for (const expense of await db.expenses.toArray()) {
      if (!expense.pending || queued.has(expense.id)) continue

      await requireSync().enqueue({
        entityType: 'Expense',
        entityId: expense.id,
        operation: expense.isDeleted ? 'Delete' : 'Update',
        groupId: expense.groupId,
        payload: expense.isDeleted ? { id: expense.id } : toWirePayload(expense),
      })
    }

    for (const settlement of await db.settlements.toArray()) {
      if (!settlement.pending || queued.has(settlement.id)) continue

      await requireSync().enqueue({
        entityType: 'Settlement',
        entityId: settlement.id,
        operation: settlement.isDeleted ? 'Delete' : 'Update',
        groupId: settlement.groupId,
        payload: settlement.isDeleted ? { id: settlement.id } : toSettlementPayload(settlement),
      })
    }

    for (const entry of await db.comments.toArray()) {
      if (!entry.pending || queued.has(entry.id)) continue

      await requireSync().enqueue({
        entityType: 'ExpenseComment',
        entityId: entry.id,
        operation: entry.isDeleted ? 'Delete' : 'Update',
        groupId: entry.groupId,
        payload: entry.isDeleted ? { id: entry.id } : toCommentPayload(entry),
      })
    }

    await refreshPendingCount()
    syncSoon()
  }

  async function discardRejected(operationId: string): Promise<void> {
    const operation = await db.outbox.get(operationId)
    if (!operation) return

    await requireSync().discard(operationId)

    const stillQueued = await db.outbox.where('entityId').equals(operation.entityId).count()
    if (stillQueued === 0) {
      if (operation.operation === 'Create') await removeLocalRow(operation)
      else await clearPendingMarker(operation)
    }

    await hydrate()
  }

  async function removeLocalRow(operation: OutboxOperation): Promise<void> {
    switch (operation.entityType) {
      case 'Expense':
        await db.expenses.delete(operation.entityId)
        break
      case 'Settlement':
        await db.settlements.delete(operation.entityId)
        break
      case 'ExpenseComment':
        await db.comments.delete(operation.entityId)
        break
    }
  }

  async function clearPendingMarker(operation: OutboxOperation): Promise<void> {
    switch (operation.entityType) {
      case 'Expense':
        await db.expenses.update(operation.entityId, { pending: false })
        break
      case 'Settlement':
        await db.settlements.update(operation.entityId, { pending: false })
        break
      case 'ExpenseComment':
        await db.comments.update(operation.entityId, { pending: false })
        break
    }
  }

  function syncSoon(): void {
    void sync().catch(() => {
    })
  }

  async function sync(): Promise<void> {
    if (!useAuthStore().isSignedIn) return

    isSyncing.value = true
    try {
      await requireSync().flush()
      await requireSync().pull()
      await hydrate()
    } finally {
      isSyncing.value = false
    }
  }

  async function resetToServer(): Promise<void> {
    await clearReplica()
    await hydrate()

    await useGroupsStore().loadAll()
    await sync()
  }

  async function unsentCount(): Promise<number> {
    return db.outbox.count()
  }

  async function refreshPendingCount(): Promise<void> {
    pendingCount.value = await requireSync().pendingCount()
    rejectedCount.value = await requireSync().rejectedCount()
  }

  async function requireGroup(groupId: string) {
    const group = await db.groups.get(groupId)
    if (!group) throw new Error('That group is not on this device.')
    return group
  }

  async function patch(expenseId: string, changes: Partial<LocalExpense>): Promise<void> {
    await db.expenses.update(expenseId, changes)
    const updated = await db.expenses.get(expenseId)
    if (updated) replaceExpense(updated)
  }

  function replaceExpense(expense: LocalExpense): void {
    const index = expenses.value.findIndex((candidate) => candidate.id === expense.id)
    if (index >= 0) expenses.value[index] = expense
    else expenses.value.push(expense)
    expenses.value = [...expenses.value]
  }

  return {
    expenses,
    settlements,
    comments,
    pendingCount,
    rejectedCount,
    isSyncing,
    unsyncedExpenses,
    attachSync,
    attachApi,
    hydrate,
    reconcile,
    discardRejected,
    resetToServer,
    unsentCount,
    forGroup,
    settlementsForGroup,
    commentsFor,
    add,
    edit,
    refile,
    remove,
    transfer,
    crossGroupBalance,
    offsetAcrossGroups,
    comment,
    removeComment,
    settle,
    unsettle,
    balanceFor,
    settleUpPlan,
    rawDebts,
    sync,
    refreshPendingCount,
  }
})

function computeShares(draft: ExpenseDraft) {
  if (draft.splitType === 'Itemized') {
    const items = draft.items ?? []
    if (items.length === 0) throw new Error('An itemized expense needs at least one item.')

    return calculateItemizedSplit(
      draft.amount,
      draft.currency,
      items.map((item) => ({
        amount: item.amount,
        quantity: item.quantity,
        memberIds: item.memberIds,
      })),
      draft.participantIds,
    )
  }

  return calculateSplit(
    draft.amount,
    draft.currency,
    draft.splitType,
    draft.participantIds.map((memberId) => ({
      memberId,
      value: draft.splitValues?.[memberId] ?? null,
    })),
  )
}

function toSettlementPayload(entity: LocalSettlement) {
  return {
    id: entity.id,
    groupId: entity.groupId,
    fromMemberId: entity.fromMemberId,
    toMemberId: entity.toMemberId,
    amount: entity.amount,
    currency: entity.currency,
    amountInBaseCurrency: entity.amountInBaseCurrency,
    settledAt: entity.settledAt,
    note: entity.note,
  }
}

function toCommentPayload(entity: LocalComment) {
  return {
    id: entity.id,
    expenseId: entity.expenseId,
    groupId: entity.groupId,
    authorMemberId: entity.authorMemberId,
    body: entity.body,
  }
}

function readPayers(
  draft: Pick<ExpenseDraft, 'payers' | 'paidByMemberId' | 'amount' | 'currency'>,
  members: Set<string>,
): Array<{ memberId: string; amount: number }> {
  const payers = draft.payers
  if (!payers || payers.length === 0) {
    return [{ memberId: draft.paidByMemberId, amount: roundMoney(draft.amount, draft.currency) }]
  }

  for (const payer of payers) {
    if (!members.has(payer.memberId)) {
      throw new Error('Everyone who paid must be a member of this group.')
    }
    if (!(payer.amount > 0)) {
      throw new Error('What each person paid must be greater than zero.')
    }
  }

  if (new Set(payers.map((payer) => payer.memberId)).size !== payers.length) {
    throw new Error('Somebody cannot appear twice among who paid.')
  }

  const total = roundMoney(
    payers.reduce((sum, payer) => sum + payer.amount, 0),
    draft.currency,
  )
  if (total !== roundMoney(draft.amount, draft.currency)) {
    throw new Error('What everyone paid has to add up to the expense.')
  }

  return payers.map((payer) => ({
    memberId: payer.memberId,
    amount: roundMoney(payer.amount, draft.currency),
  }))
}

function keepPayers(
  expense: LocalExpense,
  amount: number,
  currency: string,
): Array<{ memberId: string; amount: number }> {
  const existing = expense.payers ?? []
  if (existing.length === 0) return [{ memberId: expense.paidByMemberId, amount }]
  if (existing.length === 1) return [{ memberId: existing[0].memberId, amount }]

  const previous = existing.reduce((sum, payer) => sum + payer.amount, 0)
  if (previous <= 0) return [{ memberId: expense.paidByMemberId, amount }]
  if (roundMoney(previous, currency) === roundMoney(amount, currency)) {
    return existing.map((payer) => ({ memberId: payer.memberId, amount: payer.amount }))
  }

  const scaled = existing.map((payer) => ({
    memberId: payer.memberId,
    amount: roundMoney((amount * payer.amount) / previous, currency),
  }))

  const residue = roundMoney(
    amount - scaled.reduce((sum, payer) => sum + payer.amount, 0),
    currency,
  )
  if (residue !== 0) {
    const largest = scaled.reduce(
      (best, payer, index) => (payer.amount > scaled[best].amount ? index : best),
      0,
    )
    scaled[largest] = { ...scaled[largest], amount: scaled[largest].amount + residue }
  }

  return scaled
}

function mainPayer(payers: Array<{ memberId: string; amount: number }>): string {
  return [...payers].sort(
    (left, right) => right.amount - left.amount || left.memberId.localeCompare(right.memberId),
  )[0].memberId
}

function toWirePayload(expense: LocalExpense) {
  return {
    id: expense.id,
    groupId: expense.groupId,
    paidByMemberId: expense.paidByMemberId,
    description: expense.description,
    amount: expense.amount,
    currency: expense.currency,
    amountInBaseCurrency: expense.amountInBaseCurrency,
    exchangeRate: expense.exchangeRate,
    spentAt: expense.spentAt,
    splitType: expense.splitType,
    receiptId: expense.receiptId,
    notes: expense.notes,
    categoryKey: expense.categoryKey ?? null,
    payers: (expense.payers ?? []).map((payer) => ({
      memberId: payer.memberId,
      amount: payer.amount,
      amountInBaseCurrency: payer.amountInBaseCurrency,
    })),
    splits: expense.splits.map((split) => ({
      memberId: split.memberId,
      amount: split.amount,
      amountInBaseCurrency: split.amountInBaseCurrency,
      inputValue: split.inputValue,
    })),
    items: expense.items.map((item) => ({
      id: item.id,
      description: item.description,
      amount: item.amount,
      quantity: item.quantity,
      sortOrder: item.sortOrder,
      members: item.memberIds,
    })),
  }
}
