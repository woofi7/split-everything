import Dexie, { type Table } from 'dexie'
import type { SplitType } from '@/domain/splitting'
import type { VectorClock } from '@/domain/vectorClock'
import { newId } from '@/domain/ids'

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
  /**
   * Active members, as the list endpoint reports it. The list carries a count but
   * no roster, so this is the only member information a group has until a detail
   * read fills `members` in.
   */
  memberCount?: number
  /** How a new expense here is split unless someone says otherwise. */
  defaultSplitType?: SplitType
  /** Member id to weight, for a default that needs values. */
  defaultSplitValues?: Record<string, number> | null
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
  /**
   * Their colour in this group, as the group stores it.
   *
   * Optional because rows written before the column existed do not have one, and
   * a client falls back to deriving one until the group says otherwise.
   */
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

/**
 * One person's contribution to what an expense cost.
 *
 * Not a split: this is whose pocket it came out of, and it only needs more than one
 * entry when more than one pocket was involved.
 */
export interface LocalPayer {
  memberId: string
  amount: number
  amountInBaseCurrency: number
}

export interface LocalExpense {
  id: string
  groupId: string
  /** The largest payer, which is the name the lists show. */
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
  /**
   * Who put money in. Always at least one, and they sum to the amount.
   *
   * Optional on the type only for rows written by an older build of the app, which
   * had no such field: everything that reads it falls back to the single payer.
   */
  payers?: LocalPayer[]
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

/**
 * One line of the activity feed, kept so the screen has something to show offline.
 *
 * The server composes these sentences - who did what to which expense, in words -
 * so they cannot be worked out from the local rows the way the stats can. They are
 * stored as they arrive instead, and the feed reads from here first.
 */
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

export class SplitEverythingDb extends Dexie {
  groups!: Table<LocalGroup, string>
  expenses!: Table<LocalExpense, string>
  settlements!: Table<LocalSettlement, string>
  comments!: Table<LocalComment, string>
  outbox!: Table<OutboxOperation, string>
  conflicts!: Table<LocalConflict, string>
  activity!: Table<LocalActivity, number>
  meta!: Table<MetaRow, string>

  constructor() {
    super('split-everything')

    // Version 1 is left exactly as it shipped. Editing it in place would make
    // Dexie refuse to open a database created by an earlier build, which is every
    // install already out there.
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

    // Categories were removed from the app. Only what changed is listed: the
    // category index goes from expenses, and null deletes the table outright.
    this.version(2).stores({
      expenses: 'id, groupId, spentAt, [groupId+spentAt], paidByMemberId, pending',
      categories: null,
    })

    // The activity feed, kept for offline. The server writes these sentences, so
    // unlike everything else here they cannot be recomputed from local rows.
    this.version(3).stores({
      activity: 'id, groupId, occurredAt, [groupId+occurredAt]',
    })

    // Who paid, which can now be several people. No index and no new table: payers
    // live inside the expense they belong to, so this version exists to fill the
    // field in for rows already on the device rather than to change the schema.
    // Without it, every expense saved before this build would read as having no
    // payer at all and drop out of the balances.
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
  }
}

export const db = new SplitEverythingDb()

/**
 * Another tab is holding the replica at an older schema version.
 *
 * IndexedDB will not upgrade a database while an older connection is open, and it
 * does not fail either: it waits, with no timeout. Dexie asks the other
 * connection to step aside, but a tab a phone has frozen in the background cannot
 * run any code to hear that, so the wait never ends and every read on this tab
 * hangs with it. Silently, and before the first render, which is how it produced
 * a white screen rather than an error.
 *
 * Reported rather than thrown, because there is nothing the code can do about it
 * and everything the person can: close the other tabs.
 */
const blockedListeners = new Set<() => void>()
let isBlocked = false

db.on('blocked', () => {
  isBlocked = true
  for (const listener of blockedListeners) listener()
})

export function onDatabaseBlocked(listener: () => void): void {
  blockedListeners.add(listener)
  // Late subscribers hear about it too: the event fires while the app is still
  // starting up, which is exactly when the listener is being attached.
  if (isBlocked) listener()
}

/** Test seam: the blocked flag outlives a single test otherwise. */
export function resetBlockedState(): void {
  isBlocked = false
  blockedListeners.clear()
}

const DEVICE_ID_KEY = 'deviceId'
const CURSOR_PREFIX = 'cursor:'

/**
 * The device id keys every vector clock, so it must survive reloads: a fresh one
 * per session would make the same install look like a new peer each launch and
 * conflict with its own earlier writes.
 *
 * Held in memory as well as stored, because the API client needs it synchronously
 * on every request.
 */
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

/** The id as it stands, for callers that cannot await. Null before the first read. */
export function deviceIdNow(): string | null {
  return cachedDeviceId
}

/**
 * Mints a new device id, abandoning the old one.
 *
 * A device id keys every vector clock, so the server never moves one between
 * accounts: two accounts writing under one id would interleave their histories.
 * A different account on the same install is therefore a new install, and this is
 * what makes it one.
 */
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

/**
 * Empties every replicated table and every sync cursor, keeping the device id.
 *
 * Told apart from resetDatabase on purpose: that one hands the install to another
 * account and the device id goes with it. This one keeps the same device and asks
 * the server for its history again, so the vector clocks stay continuous.
 */
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

  // Cursors, but not the device id: asking from zero is the point.
  const cursors = await db.meta.filter((row) => row.key.startsWith(CURSOR_PREFIX)).toArray()
  await db.meta.bulkDelete(cursors.map((row) => row.key))
}

export async function resetDatabase(): Promise<void> {
  // The cache mirrors a row that is about to go, so it goes too.
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

/**
 * Whether the replica is answering at all.
 *
 * IndexedDB has no timeout: a request that cannot proceed waits, silently and
 * forever. Every screen reads from here, and each one holds a loading flag that
 * is cleared after the read, so one wedged read is a spinner that never stops.
 *
 * Asked rather than inferred, because the reasons differ and the symptom does
 * not: an upgrade another tab is blocking, a browser that has revoked storage
 * access, a private window that has run out of quota.
 */
export async function isReplicaResponsive(timeoutMs = 8000): Promise<boolean> {
  let timer: ReturnType<typeof setTimeout> | undefined

  const answered = db.meta.get(DEVICE_ID_KEY).then(
    () => true,
    // A refusal is an answer: the replica is reachable and said no.
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
