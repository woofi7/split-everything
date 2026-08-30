import {
  db,
  getAllCursors,
  getDeviceId,
  setCursor,
  type LocalComment,
  type LocalExpense,
  type LocalSettlement,
  type OutboxOperation,
} from './db'
import { tickClock, type VectorClock } from '@/domain/vectorClock'
import type { SplitType } from '@/domain/splitting'
import { newId } from '@/domain/ids'

export interface SyncOperationRequest {
  operationId: string
  entityType: OutboxOperation['entityType']
  entityId: string
  operation: OutboxOperation['operation']
  groupId: string
  payloadJson: string
  vectorClock: VectorClock
  clientTimestamp: string
}

export interface SyncAccepted {
  operationId: string
  entityId: string
  serverSeq: number
  vectorClock: VectorClock
}

export interface SyncRejected {
  operationId: string
  entityId: string
  reason: string
  code: string
}

export interface SyncConflict {
  conflictId: string
  groupId: string
  entityType: string
  entityId: string
  storedPayloadJson: string
  storedVectorClock: VectorClock
  incomingPayloadJson: string
  incomingVectorClock: VectorClock
  conflictingFields: string[]
  detectedAt: string
}

export interface SyncPushResult {
  accepted: SyncAccepted[]
  conflicts: SyncConflict[]
  rejected: SyncRejected[]
  groupCursors: Record<string, number>
}

export interface SyncLogEntry {
  serverSeq: number
  groupId: string
  entityType: string
  entityId: string
  operation: string
  deviceId: string
  payloadJson: string
  vectorClock: VectorClock
  lineageId: string
  sourceGroupId?: string | null
  counterpartGroupId?: string | null
  createdAt: string
}

export interface SyncPullResult {
  entries: SyncLogEntry[]
  groupCursors: Record<string, number>
  snapshots: Array<{
    id: string
    groupId: string
    upToServerSeq: number
    cutoffAt: string
    vectorClock: VectorClock
    stateJson: string
  }>
  hasMore: boolean
}

export interface SyncApi {
  push(request: { deviceId: string; operations: SyncOperationRequest[] }): Promise<SyncPushResult>
  pull(request: {
    deviceId: string
    groupCursors: Record<string, number>
    maxEntries: number
  }): Promise<SyncPullResult>
  acknowledge(groupCursors: Record<string, number>): Promise<void>
}

export interface EnqueueRequest {
  entityType: OutboxOperation['entityType']
  entityId: string
  operation: OutboxOperation['operation']
  groupId: string
  payload: unknown
}

/** Guards against a server that always claims there is more to pull. */
const MAX_PULL_PAGES = 50

/**
 * Whether the server refused the request itself, rather than being unreachable.
 *
 * A refusal will be refused again just as firmly, so retrying it forever holds
 * the rows it touched hostage. 401 is a session to renew, 408 and 429 are "not
 * now", and a 5xx is the server's own problem: all of those are worth another go.
 */
function isRefusal(error: unknown): boolean {
  const status = (error as { status?: unknown } | null)?.status
  if (typeof status !== 'number') return false
  if (status === 401 || status === 408 || status === 429) return false

  return status >= 400 && status < 500
}
const PULL_BATCH_SIZE = 500

/**
 * The offline engine.
 *
 * Writes go into the local replica immediately and into an outbox; nothing waits
 * on the network. When connectivity returns, the outbox drains in the order the
 * user made the changes, and deltas are pulled from a per-group cursor.
 *
 * Three rules shape everything here:
 * - A queued change is never lost. A failed push leaves it queued and retries.
 * - A change the server will never accept is parked, not retried forever, or it
 *   would block every later change behind it.
 * - A pull never overwrites a local edit that has not been sent yet.
 */
export class SyncEngine {
  private flushing: Promise<SyncPushResult> | null = null
  private readonly api: SyncApi
  private readonly isOnline: () => boolean

  constructor(api: SyncApi, isOnline: () => boolean = () => navigator.onLine) {
    this.api = api
    this.isOnline = isOnline
  }

  async enqueue(request: EnqueueRequest): Promise<OutboxOperation> {
    const deviceId = await getDeviceId()
    const previous = await this.latestClockFor(request.entityId)

    const operation: OutboxOperation = {
      operationId: newId(),
      entityType: request.entityType,
      entityId: request.entityId,
      operation: request.operation,
      groupId: request.groupId,
      payloadJson: JSON.stringify(request.payload),
      // Ticked from the newest clock this device knows for the entity, so a run of
      // offline edits forms a causal chain instead of a pile of conflicts.
      vectorClock: tickClock(previous, deviceId),
      clientTimestamp: new Date().toISOString(),
      sequence: await this.nextSequence(),
      status: 'pending',
      attempts: 0,
      lastError: null,
    }

    await db.outbox.put(operation)
    return operation
  }

  async pendingCount(): Promise<number> {
    return db.outbox.where('status').anyOf('pending', 'inflight').count()
  }

  async rejectedCount(): Promise<number> {
    return db.outbox.where('status').equals('rejected').count()
  }

  async discard(operationId: string): Promise<void> {
    await db.outbox.delete(operationId)
  }

  /** Retries an operation the user fixed, moving it back into the queue. */
  async retry(operationId: string): Promise<void> {
    await db.outbox.update(operationId, { status: 'pending', lastError: null })
  }

  async flush(): Promise<SyncPushResult> {
    // Overlapping flushes would send the same operation twice.
    if (this.flushing) return this.flushing

    this.flushing = this.doFlush().finally(() => {
      this.flushing = null
    })

    return this.flushing
  }

  private async doFlush(): Promise<SyncPushResult> {
    const empty: SyncPushResult = { accepted: [], conflicts: [], rejected: [], groupCursors: {} }
    if (!this.isOnline()) return empty

    const pending = await db.outbox.where('status').equals('pending').sortBy('sequence')
    if (pending.length === 0) return empty

    const deviceId = await getDeviceId()

    try {
      const result = await this.api.push({
        deviceId,
        operations: pending.map((operation) => ({
          operationId: operation.operationId,
          entityType: operation.entityType,
          entityId: operation.entityId,
          operation: operation.operation,
          groupId: operation.groupId,
          payloadJson: operation.payloadJson,
          vectorClock: operation.vectorClock,
          clientTimestamp: operation.clientTimestamp,
        })),
      })

      await this.applyPushResult(pending, result)
      return result
    } catch (error) {
      // Nothing is discarded: the queue is the durable record of the user's work.
      // But a refusal is not a connection problem, and treating the two alike is
      // what left rows waiting on a request that would never be accepted.
      const message = error instanceof Error ? error.message : String(error)
      const refused = isRefusal(error)

      for (const operation of pending) {
        await db.outbox.update(operation.operationId, {
          status: refused ? 'rejected' : 'pending',
          attempts: operation.attempts + 1,
          lastError: message,
        })

        if (refused) await this.release(operation)
      }

      return empty
    }
  }

  private async applyPushResult(
    sent: OutboxOperation[],
    result: SyncPushResult,
  ): Promise<void> {
    const acceptedIds = new Set(result.accepted.map((a) => a.operationId))
    const conflictedEntityIds = new Set(result.conflicts.map((c) => c.entityId))
    const rejectedById = new Map(result.rejected.map((r) => [r.operationId, r]))

    for (const operation of sent) {
      if (acceptedIds.has(operation.operationId)) {
        await db.outbox.delete(operation.operationId)
        await this.markSynced(operation)
        continue
      }

      const rejection = rejectedById.get(operation.operationId)
      if (rejection) {
        // Parked rather than retried: the server will never accept it, and retrying
        // would stall every later change in the queue.
        await db.outbox.update(operation.operationId, {
          status: 'rejected',
          attempts: operation.attempts + 1,
          lastError: `${rejection.code}: ${rejection.reason}`,
        })
        await this.release(operation)
        continue
      }

      if (conflictedEntityIds.has(operation.entityId)) {
        await db.outbox.delete(operation.operationId)
        continue
      }

      // Neither accepted, rejected nor conflicted: the server did not speak to it,
      // so leave it queued for the next attempt.
      await db.outbox.update(operation.operationId, { status: 'pending' })
    }

    for (const conflict of result.conflicts) {
      await db.conflicts.put({
        conflictId: conflict.conflictId,
        groupId: conflict.groupId,
        entityType: conflict.entityType,
        entityId: conflict.entityId,
        storedPayloadJson: conflict.storedPayloadJson,
        incomingPayloadJson: conflict.incomingPayloadJson,
        conflictingFields: conflict.conflictingFields,
        detectedAt: conflict.detectedAt,
      })
    }

    for (const [groupId, serverSeq] of Object.entries(result.groupCursors)) {
      await setCursor(groupId, serverSeq)
    }
  }

  /**
   * Lets go of a local row whose change the server will not take.
   *
   * The marker is the whole problem: a row that claims to be unsent is skipped by
   * every pull, on purpose, so that a remote revision cannot overwrite work the
   * person can still see. Leave the marker on a change that has been refused and
   * that protection becomes a trap. The row is frozen wrong, the server can never
   * correct it, and a refused deletion in particular hides the expense on this
   * device for good.
   *
   * So the row goes back to being the server's to describe. What the person tried
   * to do is not lost: the operation stays in the queue as refused, with its
   * payload and the reason, for the screen that lists those.
   */
  private async release(operation: OutboxOperation): Promise<void> {
    const stillQueued = await db.outbox
      .where('entityId')
      .equals(operation.entityId)
      .filter((row) => row.status === 'pending' || row.status === 'inflight')
      .count()

    // A later edit to the same thing is still going: it owns the row now.
    if (stillQueued > 0) return

    // A creation the server refused exists nowhere else, so there is nothing for a
    // pull to replace it with. Left on screen it would keep counting towards
    // balances that no one else can see.
    if (operation.operation === 'Create') {
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
      return
    }

    // Anything else: unmark it, and bring back what a refused deletion hid, so
    // the next pull can replace it with whatever the server actually holds.
    const restore = operation.operation === 'Delete'
      ? { pending: false, isDeleted: false }
      : { pending: false }

    switch (operation.entityType) {
      case 'Expense':
        await db.expenses.update(operation.entityId, restore)
        break
      case 'Settlement':
        await db.settlements.update(operation.entityId, restore)
        break
      case 'ExpenseComment':
        await db.comments.update(operation.entityId, restore)
        break
    }
  }

  private async markSynced(operation: OutboxOperation): Promise<void> {
    const stillQueued = await db.outbox
      .where('entityId')
      .equals(operation.entityId)
      .filter((row) => row.status !== 'rejected')
      .count()

    // Only clear the marker once nothing for this entity is still waiting, or a
    // row would look synced while a later edit is still queued.
    if (stillQueued > 0) return

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

  async pull(): Promise<void> {
    if (!this.isOnline()) return

    const deviceId = await getDeviceId()

    for (let page = 0; page < MAX_PULL_PAGES; page++) {
      const groupCursors = await getAllCursors()

      const result = await this.api.pull({
        deviceId,
        groupCursors,
        maxEntries: PULL_BATCH_SIZE,
      })

      for (const entry of result.entries) await this.applyEntry(entry)

      for (const [groupId, serverSeq] of Object.entries(result.groupCursors)) {
        await setCursor(groupId, serverSeq)
      }

      if (Object.keys(result.groupCursors).length > 0) {
        await this.api.acknowledge(result.groupCursors)
      }

      if (!result.hasMore) break
    }
  }

  private async applyEntry(entry: SyncLogEntry): Promise<void> {
    // Unsent local work is work the person can still see on screen, so a remote
    // revision must not overwrite it. Both signals count: the outbox is the
    // authoritative queue, and the row's own pending flag covers a row marked
    // unconfirmed whose operation is not in the queue (mid-write, or a queue
    // pruned by a conflict).
    if (await this.hasUnsentLocalChange(entry.entityId)) return

    if (entry.operation === 'Delete') {
      await this.applyDelete(entry)
      return
    }

    let payload: Record<string, unknown>
    try {
      payload = JSON.parse(entry.payloadJson) as Record<string, unknown>
    } catch {
      // A payload we cannot read is skipped, but the cursor still advances so the
      // client does not re-fetch it forever.
      return
    }

    switch (entry.entityType) {
      case 'Expense':
        await db.expenses.put(toLocalExpense(payload, entry))
        break
      case 'Settlement':
        await db.settlements.put(toLocalSettlement(payload, entry))
        break
      case 'ExpenseComment':
        await db.comments.put(toLocalComment(payload, entry))
        break
    }
  }

  private async applyDelete(entry: SyncLogEntry): Promise<void> {
    const patch = { isDeleted: true, pending: false, vectorClock: entry.vectorClock }

    switch (entry.entityType) {
      case 'Expense':
        await db.expenses.update(entry.entityId, patch)
        break
      case 'Settlement':
        await db.settlements.update(entry.entityId, patch)
        break
      case 'ExpenseComment':
        await db.comments.update(entry.entityId, { isDeleted: true, pending: false })
        break
    }
  }

  private async hasUnsentLocalChange(entityId: string): Promise<boolean> {
    const queued = await db.outbox
      .where('entityId')
      .equals(entityId)
      .filter((row) => row.status === 'pending' || row.status === 'inflight')
      .count()
    if (queued > 0) return true

    const expense = await db.expenses.get(entityId)
    if (expense?.pending) return true

    const settlement = await db.settlements.get(entityId)
    if (settlement?.pending) return true

    const comment = await db.comments.get(entityId)
    return Boolean(comment?.pending)
  }

  /** Newest clock this device knows for an entity, so an edit chain stays causal. */
  private async latestClockFor(entityId: string): Promise<VectorClock> {
    const queued = await db.outbox
      .where('entityId')
      .equals(entityId)
      .filter((row) => row.status !== 'rejected')
      .sortBy('sequence')

    if (queued.length > 0) return queued[queued.length - 1].vectorClock

    const expense = await db.expenses.get(entityId)
    if (expense) return expense.vectorClock

    const settlement = await db.settlements.get(entityId)
    if (settlement) return settlement.vectorClock

    const comment = await db.comments.get(entityId)
    if (comment) return comment.vectorClock

    return {}
  }

  private async nextSequence(): Promise<number> {
    const last = await db.outbox.orderBy('sequence').last()
    return (last?.sequence ?? 0) + 1
  }
}

const SPLIT_TYPES: SplitType[] = ['Equal', 'Percentage', 'Shares', 'ExactAmount', 'Itemized']

/**
 * The server serialises enums as names over HTTP but as their numeric value inside
 * a sync payload snapshot, so both have to be accepted here.
 */
function readSplitType(value: unknown): SplitType {
  if (typeof value === 'string' && SPLIT_TYPES.includes(value as SplitType)) {
    return value as SplitType
  }
  if (typeof value === 'number' && SPLIT_TYPES[value]) return SPLIT_TYPES[value]
  return 'Equal'
}

/*
 * What a sync payload looks like before anything has checked it.
 *
 * The server's own shape is known, but a row in the log may have been written by a
 * client older or newer than this one, so every field below is read defensively and
 * coerced. Typing it as unknown would only move the casts inside; naming it here
 * says the looseness is the boundary rather than an oversight.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type WirePayload = Record<string, any>

function toLocalExpense(payload: WirePayload, entry: SyncLogEntry): LocalExpense {
  return {
    id: String(payload.id ?? entry.entityId),
    groupId: String(payload.groupId ?? entry.groupId),
    paidByMemberId: String(payload.paidByMemberId ?? ''),
    description: String(payload.description ?? ''),
    amount: Number(payload.amount ?? 0),
    currency: String(payload.currency ?? 'CAD'),
    amountInBaseCurrency: Number(payload.amountInBaseCurrency ?? payload.amount ?? 0),
    exchangeRate: Number(payload.exchangeRate ?? 1),
    spentAt: String(payload.spentAt ?? entry.createdAt),
    splitType: readSplitType(payload.splitType),
    receiptId: payload.receiptId ?? null,
    notes: payload.notes ?? null,
    splits: (payload.splits ?? []).map((split: WirePayload) => ({
      memberId: String(split.memberId),
      amount: Number(split.amount ?? 0),
      amountInBaseCurrency: Number(split.amountInBaseCurrency ?? split.amount ?? 0),
      inputValue: split.inputValue ?? null,
    })),
    items: (payload.items ?? []).map((item: WirePayload) => ({
      id: item.id ?? null,
      description: String(item.description ?? ''),
      amount: Number(item.amount ?? 0),
      quantity: Number(item.quantity ?? 1),
      sortOrder: Number(item.sortOrder ?? 0),
      memberIds: item.members ?? item.memberIds ?? [],
    })),
    revision: Number(payload.revision ?? 1),
    isDeleted: Boolean(payload.isDeleted ?? false),
    vectorClock: entry.vectorClock,
    serverSeq: entry.serverSeq,
    pending: false,
  }
}

function toLocalSettlement(payload: WirePayload, entry: SyncLogEntry): LocalSettlement {
  return {
    id: String(payload.id ?? entry.entityId),
    groupId: String(payload.groupId ?? entry.groupId),
    fromMemberId: String(payload.fromMemberId ?? ''),
    toMemberId: String(payload.toMemberId ?? ''),
    amount: Number(payload.amount ?? 0),
    currency: String(payload.currency ?? 'CAD'),
    amountInBaseCurrency: Number(payload.amountInBaseCurrency ?? payload.amount ?? 0),
    settledAt: String(payload.settledAt ?? entry.createdAt),
    note: payload.note ?? null,
    isDeleted: Boolean(payload.isDeleted ?? false),
    vectorClock: entry.vectorClock,
    serverSeq: entry.serverSeq,
    pending: false,
  }
}

function toLocalComment(payload: WirePayload, entry: SyncLogEntry): LocalComment {
  return {
    id: String(payload.id ?? entry.entityId),
    expenseId: String(payload.expenseId ?? ''),
    groupId: String(payload.groupId ?? entry.groupId),
    authorMemberId: String(payload.authorMemberId ?? ''),
    parentCommentId: payload.parentCommentId ?? null,
    body: String(payload.body ?? ''),
    createdAt: String(payload.createdAt ?? entry.createdAt),
    isDeleted: Boolean(payload.isDeleted ?? false),
    vectorClock: entry.vectorClock,
    pending: false,
  }
}
