import Dexie, { type Table } from 'dexie'
import type { SplitType } from '@/domain/splitting'
import type { VectorClock } from '@/domain/vectorClock'

/**
 * The local replica.
 *
 * Every screen reads from here, never from the network directly, so the app is
 * identical online and offline: sync writes into these tables and the UI reacts.
 * Rows carry the same shape the API returns plus a `pending` flag, so an
 * optimistic local write is indistinguishable to the UI from a confirmed one.
 */

export interface LocalGroup {
  id: string
  name: string
  description?: string | null
  baseCurrency: string
  iconName?: string | null
  colorHex: string
  isArchived: boolean
  lineageId: string
  members: LocalMember[]
  myNetBalance: number
  totalSpend: number
  expenseCount: number
  updatedAt: string
  pending?: boolean
}

export interface LocalMember {
  id: string
  userId?: string | null
  displayName: string
  avatarUrl?: string | null
  role: string
  status: string
  isPlaceholder: boolean
  netBalance: number
}

export interface LocalSplit {
  memberId: string
  amount: number
  amountInBaseCurrency: number
  inputValue: number | null
}

export interface LocalItem {
  id?: string | null
  description: string
  amount: number
  quantity: number
  sortOrder: number
  memberIds: string[]
}

export interface LocalExpense {
  id: string
  groupId: string
  paidByMemberId: string
  description: string
  amount: number
  currency: string
  amountInBaseCurrency: number
  exchangeRate: number
  spentAt: string
  categoryId?: string | null
  splitType: SplitType
  receiptId?: string | null
  notes?: string | null
  splits: LocalSplit[]
  items: LocalItem[]
  revision: number
  isDeleted: boolean
  vectorClock: VectorClock
  serverSeq: number
  /** True while the change is still only local. Drives the "not synced" marker. */
  pending: boolean
}

export interface LocalSettlement {
  id: string
  groupId: string
  fromMemberId: string
  toMemberId: string
  amount: number
  currency: string
  amountInBaseCurrency: number
  settledAt: string
  note?: string | null
  isDeleted: boolean
  vectorClock: VectorClock
  serverSeq: number
  pending: boolean
}

export interface LocalComment {
  id: string
  expenseId: string
  groupId: string
  authorMemberId: string
  authorName?: string
  parentCommentId?: string | null
  body: string
  createdAt: string
  isDeleted: boolean
  vectorClock: VectorClock
  pending: boolean
}

export interface LocalCategory {
  id: string
  key: string
  name: string
  emoji: string
  colorHex: string
  sortOrder: number
}

export type OutboxStatus = 'pending' | 'inflight' | 'rejected'

export interface OutboxOperation {
  operationId: string
  entityType: 'Expense' | 'Settlement' | 'ExpenseComment'
  entityId: string
  operation: 'Create' | 'Update' | 'Delete'
  groupId: string
  payloadJson: string
  vectorClock: VectorClock
  clientTimestamp: string
  /** Monotonic, so a queue drains in the order the user made the changes. */
  sequence: number
  status: OutboxStatus
  attempts: number
  lastError?: string | null
}

export interface LocalConflict {
  conflictId: string
  groupId: string
  entityType: string
  entityId: string
  storedPayloadJson: string
  incomingPayloadJson: string
  conflictingFields: string[]
  detectedAt: string
}

export interface MetaRow {
  key: string
  value: string
}

export class SplitEverythingDb extends Dexie {
  groups!: Table<LocalGroup, string>
  expenses!: Table<LocalExpense, string>
  settlements!: Table<LocalSettlement, string>
  comments!: Table<LocalComment, string>
  categories!: Table<LocalCategory, string>
  outbox!: Table<OutboxOperation, string>
  conflicts!: Table<LocalConflict, string>
  meta!: Table<MetaRow, string>

  constructor() {
    super('split-everything')

    this.version(1).stores({
      groups: 'id, name, isArchived',
      expenses: 'id, groupId, spentAt, [groupId+spentAt], categoryId, paidByMemberId, pending',
      settlements: 'id, groupId, settledAt, pending',
      comments: 'id, expenseId, groupId, createdAt',
      categories: 'id, key, sortOrder',
      // Drained in sequence order, so a create is never sent after the edit that
      // depends on it.
      outbox: 'operationId, sequence, status, groupId, entityId',
      conflicts: 'conflictId, groupId, entityId',
      meta: 'key',
    })
  }
}

export const db = new SplitEverythingDb()

const DEVICE_ID_KEY = 'deviceId'
const CURSOR_PREFIX = 'cursor:'

/**
 * The device id keys every vector clock, so it must survive reloads. A fresh one
 * per session would make the same install look like a new peer each launch and
 * conflict with its own earlier writes.
 */
export async function getDeviceId(): Promise<string> {
  const existing = await db.meta.get(DEVICE_ID_KEY)
  if (existing) return existing.value

  const deviceId = crypto.randomUUID()
  await db.meta.put({ key: DEVICE_ID_KEY, value: deviceId })
  return deviceId
}

export async function getCursor(groupId: string): Promise<number> {
  const row = await db.meta.get(`${CURSOR_PREFIX}${groupId}`)
  return row ? Number(row.value) : 0
}

/** Monotonic: replaying applied history would resurrect deleted rows. */
export async function setCursor(groupId: string, serverSeq: number): Promise<void> {
  const current = await getCursor(groupId)
  if (serverSeq <= current) return

  await db.meta.put({ key: `${CURSOR_PREFIX}${groupId}`, value: String(serverSeq) })
}

export async function getAllCursors(): Promise<Record<string, number>> {
  const rows = await db.meta.filter((row) => row.key.startsWith(CURSOR_PREFIX)).toArray()

  return Object.fromEntries(
    rows.map((row) => [row.key.slice(CURSOR_PREFIX.length), Number(row.value)]),
  )
}

export async function resetDatabase(): Promise<void> {
  await Promise.all([
    db.groups.clear(),
    db.expenses.clear(),
    db.settlements.clear(),
    db.comments.clear(),
    db.categories.clear(),
    db.outbox.clear(),
    db.conflicts.clear(),
    db.meta.clear(),
  ])
}
