import Dexie, { type Table } from 'dexie'
import type { Category } from '@/domain/categories'
import type { SplitType } from '@/domain/splitting'
import type { VectorClock } from '@/domain/vectorClock'
import { newId } from '@/domain/ids'

export interface LocalGroup {
  id: string
  name: string
  description?: string | null
  baseCurrency: string
  iconName?: string | null
  colorHex: string
  themeName?: string | null
  isArchived: boolean
  lineageId: string
  members: LocalMember[]
  memberCount?: number
  defaultSplitType?: SplitType
  defaultSplitValues?: Record<string, number> | null
  ignoredNamePatterns?: string[] | null
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
  colorHex?: string | null
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

export interface LocalPayer {
  memberId: string
  amount: number
  amountInBaseCurrency: number
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
  splitType: SplitType
  receiptId?: string | null
  notes?: string | null
  categoryKey?: string | null
  payers?: LocalPayer[]
  splits: LocalSplit[]
  items: LocalItem[]
  revision: number
  isDeleted: boolean
  vectorClock: VectorClock
  serverSeq: number
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

export interface LocalActivity {
  id: number
  groupId: string | null
  groupName: string | null
  kind: string
  actorMemberId: string | null
  actorName: string | null
  subjectType: string | null
  subjectId: string | null
  summary: string
  occurredAt: string
}

export interface LocalCategories {
  groupId: string
  categories: Category[]
}

export class SplitEverythingDb extends Dexie {
  groups!: Table<LocalGroup, string>
  categories!: Table<LocalCategories, string>
  expenses!: Table<LocalExpense, string>
  settlements!: Table<LocalSettlement, string>
  comments!: Table<LocalComment, string>
  outbox!: Table<OutboxOperation, string>
  conflicts!: Table<LocalConflict, string>
  activity!: Table<LocalActivity, number>
  meta!: Table<MetaRow, string>
  constructor() {
    super('split-everything')

    this.version(1).stores({
      groups: 'id, name, isArchived',
      expenses: 'id, groupId, spentAt, [groupId+spentAt], categoryId, paidByMemberId, pending',
      settlements: 'id, groupId, settledAt, pending',
      comments: 'id, expenseId, groupId, createdAt',
      categories: 'id, key, sortOrder',
      outbox: 'operationId, sequence, status, groupId, entityId',
      conflicts: 'conflictId, groupId, entityId',
      meta: 'key',
    })

    this.version(2).stores({
      expenses: 'id, groupId, spentAt, [groupId+spentAt], paidByMemberId, pending',
      categories: null,
    })

    this.version(3).stores({
      activity: 'id, groupId, occurredAt, [groupId+occurredAt]',
    })

    this.version(4)
      .stores({})
      .upgrade((transaction) =>
        transaction
          .table<LocalExpense>('expenses')
          .toCollection()
          .modify((expense) => {
            if (expense.payers && expense.payers.length > 0) return

            expense.payers = [
              {
                memberId: expense.paidByMemberId,
                amount: expense.amount,
                amountInBaseCurrency: expense.amountInBaseCurrency,
              },
            ]
          }),
      )

    this.version(5).stores({
      categories: 'groupId',
    })
  }
}

export const db = new SplitEverythingDb()

const blockedListeners = new Set<() => void>()
let isBlocked = false

db.on('blocked', () => {
  isBlocked = true
  for (const listener of blockedListeners) listener()
})

export function onDatabaseBlocked(listener: () => void): void {
  blockedListeners.add(listener)
  if (isBlocked) listener()
}

export function resetBlockedState(): void {
  isBlocked = false
  blockedListeners.clear()
}

const DEVICE_ID_KEY = 'deviceId'
const CURSOR_PREFIX = 'cursor:'

let cachedDeviceId: string | null = null

export async function getDeviceId(): Promise<string> {
  if (cachedDeviceId) return cachedDeviceId

  const existing = await db.meta.get(DEVICE_ID_KEY)
  if (existing) {
    cachedDeviceId = existing.value
    return cachedDeviceId
  }

  const deviceId = newId()
  await db.meta.put({ key: DEVICE_ID_KEY, value: deviceId })
  cachedDeviceId = deviceId
  return deviceId
}

export function deviceIdNow(): string | null {
  return cachedDeviceId
}

export async function rotateDeviceId(): Promise<string> {
  const deviceId = newId()
  await db.meta.put({ key: DEVICE_ID_KEY, value: deviceId })
  cachedDeviceId = deviceId
  return deviceId
}

export async function getCursor(groupId: string): Promise<number> {
  const row = await db.meta.get(`${CURSOR_PREFIX}${groupId}`)
  return row ? Number(row.value) : 0
}

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

export async function clearReplica(): Promise<void> {
  await Promise.all([
    db.groups.clear(),
    db.expenses.clear(),
    db.settlements.clear(),
    db.comments.clear(),
    db.outbox.clear(),
    db.conflicts.clear(),
    db.activity.clear(),
  ])

  const cursors = await db.meta.filter((row) => row.key.startsWith(CURSOR_PREFIX)).toArray()
  await db.meta.bulkDelete(cursors.map((row) => row.key))
}

export async function resetDatabase(): Promise<void> {
  cachedDeviceId = null

  await Promise.all([
    db.groups.clear(),
    db.expenses.clear(),
    db.settlements.clear(),
    db.comments.clear(),
    db.outbox.clear(),
    db.conflicts.clear(),
    db.activity.clear(),
    db.meta.clear(),
  ])
}

export async function isReplicaResponsive(timeoutMs = 8000): Promise<boolean> {
  let timer: ReturnType<typeof setTimeout> | undefined

  const answered = db.meta.get(DEVICE_ID_KEY).then(
    () => true,
    () => true,
  )

  const timedOut = new Promise<boolean>((resolve) => {
    timer = setTimeout(() => resolve(false), timeoutMs)
  })

  try {
    return await Promise.race([answered, timedOut])
  } finally {
    clearTimeout(timer)
  }
}
