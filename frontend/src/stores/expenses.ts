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
import type { SyncEngine } from '@/offline/syncEngine'
import { newId } from '@/domain/ids'

export interface ExpenseDraft {
  groupId: string
  paidByMemberId: string
  description: string
  amount: number
  currency: string
  spentAt: Date
  splitType: SplitType
  participantIds: string[]
  /** Per-member input for percentage, shares and exact splits. */
  splitValues?: Record<string, number>
  items?: LocalItem[]
  categoryId?: string | null
  receiptId?: string | null
  notes?: string | null
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

/**
 * Expenses, settlements and comments, all written locally first.
 *
 * Every mutation follows the same shape: validate, compute the real amounts with
 * the same algorithm the server uses, write to the local replica, and queue the
 * operation. Nothing waits on the network, and the balances on screen are computed
 * from local rows so they are correct offline.
 *
 * Currency conversion is the one thing deliberately left to the server: the client
 * has no rate, so a foreign-currency expense carries rate 1 until the server
 * freezes the real one and the row comes back through sync.
 */
export const useExpensesStore = defineStore('expenses', () => {
  const expenses = ref<LocalExpense[]>([])
  const settlements = ref<LocalSettlement[]>([])
  const comments = ref<LocalComment[]>([])
  const pendingCount = ref(0)
  const rejectedCount = ref(0)
  const isSyncing = ref(false)
  let engine: SyncEngine | null = null

  function attachSync(syncEngine: SyncEngine): void {
    engine = syncEngine
  }

  function requireSync(): SyncEngine {
    if (!engine) throw new Error('The expenses store has no sync engine attached.')
    return engine
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
    for (const participant of draft.participantIds) {
      if (!memberIds.has(participant)) {
        throw new Error('Every participant must be a member of this group.')
      }
    }

    const shares = computeShares(draft)

    // The client has no exchange rate. Storing the entered amount and letting the
    // server freeze the real rate keeps a single source of truth for FX.
    const isBaseCurrency = draft.currency === group.baseCurrency
    const expense: LocalExpense = {
      id: newId(),
      groupId: draft.groupId,
      paidByMemberId: draft.paidByMemberId,
      description,
      amount: roundMoney(draft.amount, draft.currency),
      currency: draft.currency,
      amountInBaseCurrency: roundMoney(draft.amount, draft.currency),
      exchangeRate: 1,
      spentAt: draft.spentAt.toISOString(),
      categoryId: draft.categoryId ?? null,
      splitType: draft.splitType,
      receiptId: draft.receiptId ?? null,
      notes: draft.notes ?? null,
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

    // Keep the local clock in step with what was queued, so a follow-up edit
    // builds on it rather than looking like a concurrent write.
    await patch(expense.id, { vectorClock: operation.vectorClock })
    await refreshPendingCount()
    syncSoon()

    return (await db.expenses.get(expense.id))!
  }

  async function edit(
    expenseId: string,
    changes: Partial<Pick<ExpenseDraft, 'description' | 'amount' | 'currency' | 'spentAt' | 'splitType' | 'participantIds' | 'splitValues' | 'items' | 'categoryId' | 'notes' | 'receiptId' | 'paidByMemberId'>>,
  ): Promise<LocalExpense> {
    const existing = await db.expenses.get(expenseId)
    if (!existing) throw new Error('That expense is not on this device.')

    // Guard only: editing an expense in a group this device no longer has would
    // leave the split unverifiable.
    await requireGroup(existing.groupId)

    const description = (changes.description ?? existing.description).trim()
    if (!description) throw new Error('An expense needs a description.')

    const amount = changes.amount ?? existing.amount
    if (!(amount > 0)) throw new Error('An expense amount must be greater than zero.')

    const participantIds =
      changes.participantIds ?? existing.splits.map((split) => split.memberId)

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
      paidByMemberId: changes.paidByMemberId ?? existing.paidByMemberId,
      description,
      amount: roundMoney(amount, changes.currency ?? existing.currency),
      currency: changes.currency ?? existing.currency,
      amountInBaseCurrency: roundMoney(amount, changes.currency ?? existing.currency),
      spentAt: (changes.spentAt ?? new Date(existing.spentAt)).toISOString(),
      splitType: changes.splitType ?? existing.splitType,
      categoryId: changes.categoryId ?? existing.categoryId,
      receiptId: changes.receiptId ?? existing.receiptId,
      notes: changes.notes ?? existing.notes,
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

  async function remove(expenseId: string): Promise<void> {
    const existing = await db.expenses.get(expenseId)
    if (!existing) throw new Error('That expense is not on this device.')

    // A tombstone, not a delete: peers still offline have to learn of it.
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

  /**
   * Removes a comment. The server allows only its author, or an admin.
   *
   * A tombstone rather than a delete, like everything else here: a device still
   * offline has to learn the comment is gone rather than keep showing it.
   */
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

  function balanceFor(groupId: string): MemberBalance[] {
    const groupExpenses = forGroup(groupId).map((expense) => ({
      payerMemberId: expense.paidByMemberId,
      amount: expense.amountInBaseCurrency,
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
      memberIds.add(expense.payerMemberId)
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
      payerMemberId: expense.paidByMemberId,
      amount: expense.amountInBaseCurrency,
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

  /**
   * Repairs a replica whose queue and pending markers have drifted apart.
   *
   * Two ordinary things strand a change, and both leave a row reading "waiting to
   * sync" with no way out:
   *
   * - A parked operation. It was refused once and nothing retries it, yet a
   *   refusal describes the server as it was, not as it is: a fixed server
   *   accepts what it used to reject.
   * - A row marked unsent with nothing queued for it. A conflict drops the
   *   operation, and an accepted operation can leave the marker behind when a
   *   later operation for the same row is refused in the same push. Nothing will
   *   send it, and a pull skips it because it looks like unsent local work, so
   *   the row is invisible in both directions.
   *
   * Run once at startup. A change that really is unacceptable is simply parked
   * again, which costs one push.
   */
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

  /**
   * Abandons a change the server refused.
   *
   * The marker has to go with it. Leaving a row marked unsent with nothing queued
   * puts it straight back into the state reconcile exists to repair, so discarding
   * would appear to do nothing. A refused creation never existed on the server, so
   * there is nothing to fall back to and the row goes; anything else keeps the row
   * and lets the next pull replace it with the server's version.
   */
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

  /**
   * Drains the queue in the background, without blocking the caller.
   *
   * Every mutation ends with this. A write is finished the moment it is in the
   * outbox and on screen, so the person never waits on the network; but something
   * has to actually send it, or the row stays marked as waiting until the app is
   * reloaded. Pulling afterwards brings back the server's canonical row, which is
   * how a foreign-currency expense picks up the real exchange rate.
   */
  function syncSoon(): void {
    void sync().catch(() => {
      // Offline or unreachable. The operation is still queued and the pending
      // indicator already says so, so there is nothing to report here.
    })
  }

  async function sync(): Promise<void> {
    isSyncing.value = true
    try {
      await requireSync().flush()
      await requireSync().pull()
      await hydrate()
    } finally {
      isSyncing.value = false
    }
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
    hydrate,
    reconcile,
    discardRejected,
    forGroup,
    settlementsForGroup,
    commentsFor,
    add,
    edit,
    remove,
    comment,
    removeComment,
    settle,
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

/** The shape the server's sync endpoint expects for an expense operation. */
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
    categoryId: expense.categoryId,
    splitType: expense.splitType,
    receiptId: expense.receiptId,
    notes: expense.notes,
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
