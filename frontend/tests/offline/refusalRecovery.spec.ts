import { beforeEach, describe, expect, it, vi } from 'vitest'
import { db, resetDatabase } from '@/offline/db'
import { SyncEngine } from '@/offline/syncEngine'

const GROUP = 'group-1'

function expenseRow(overrides: Record<string, unknown> = {}) {
  return {
    id: 'expense-1',
    groupId: GROUP,
    paidByMemberId: 'member-alice',
    description: 'Dinner',
    amount: 30,
    currency: 'CAD',
    amountInBaseCurrency: 30,
    exchangeRate: 1,
    spentAt: '2026-01-01T12:00:00.000Z',
    splitType: 'Equal' as const,
    splits: [],
    items: [],
    revision: 1,
    isDeleted: false,
    vectorClock: {},
    serverSeq: 1,
    pending: true,
    ...overrides,
  }
}

async function queue(operation: 'Create' | 'Update' | 'Delete', status = 'pending') {
  await db.outbox.put({
    operationId: `op-${operation}`,
    entityType: 'Expense',
    entityId: 'expense-1',
    operation,
    groupId: GROUP,
    payloadJson: JSON.stringify({ id: 'expense-1' }),
    vectorClock: {},
    clientTimestamp: new Date().toISOString(),
    sequence: 1,
    status: status as 'pending',
    attempts: 0,
    lastError: null,
  })
}

function refusingApi() {
  return {
    push: vi.fn(async (request: { operations: Array<{ operationId: string; entityId: string }> }) => ({
      accepted: [],
      conflicts: [],
      rejected: request.operations.map((operation) => ({
        operationId: operation.operationId,
        entityId: operation.entityId,
        reason: 'An expense needs a description.',
        code: 'InvalidPayload',
      })),
      groupCursors: {},
    })),
    pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
    acknowledge: vi.fn(async () => {}),
  }
}

function failingApi(status: number) {
  return {
    push: vi.fn(async () => {
      throw Object.assign(new Error(`The server returned ${status}.`), { status })
    }),
    pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
    acknowledge: vi.fn(async () => {}),
  }
}

describe('a change the server refuses', () => {
  beforeEach(async () => {
    await resetDatabase()
  })

  it('brings back an expense whose deletion was refused', async () => {
    await db.expenses.put(expenseRow({ isDeleted: true }))
    await queue('Delete')

    await new SyncEngine(refusingApi(), () => true).flush()

    const row = await db.expenses.get('expense-1')
    expect(row!.isDeleted).toBe(false)
    expect(row!.pending).toBe(false)
  })

  it('unmarks an edit that was refused, so a pull can correct it', async () => {
    await db.expenses.put(expenseRow({ description: 'Local only' }))
    await queue('Update')

    await new SyncEngine(refusingApi(), () => true).flush()

    expect((await db.expenses.get('expense-1'))!.pending).toBe(false)
  })

  it('removes a creation that was refused', async () => {
    await db.expenses.put(expenseRow())
    await queue('Create')

    await new SyncEngine(refusingApi(), () => true).flush()

    expect(await db.expenses.get('expense-1')).toBeUndefined()
  })

  it('keeps the refusal on record, with its reason', async () => {
    await db.expenses.put(expenseRow())
    await queue('Update')

    await new SyncEngine(refusingApi(), () => true).flush()

    const parked = await db.outbox.get('op-Update')
    expect(parked!.status).toBe('rejected')
    expect(parked!.lastError).toContain('needs a description')
    expect(parked!.payloadJson).toContain('expense-1')
  })

  it('leaves the row alone while a later edit is still queued', async () => {
    await db.expenses.put(expenseRow({ isDeleted: true }))
    await queue('Delete')
    await db.outbox.put({
      ...(await db.outbox.get('op-Delete'))!,
      operationId: 'op-later',
      operation: 'Update',
      sequence: 2,
      status: 'pending',
    })

    const api = {
      push: vi.fn(async (request: { operations: Array<{ operationId: string; entityId: string }> }) => ({
        accepted: [],
        conflicts: [],
        rejected: request.operations
          .filter((operation) => operation.operationId === 'op-Delete')
          .map((operation) => ({
            operationId: operation.operationId,
            entityId: operation.entityId,
            reason: 'An expense needs a description.',
            code: 'InvalidPayload',
          })),
        groupCursors: {},
      })),
      pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
      acknowledge: vi.fn(async () => {}),
    }
    await new SyncEngine(api, () => true).flush()

    expect((await db.expenses.get('expense-1'))!.pending).toBe(true)
  })

  describe('when the whole request is refused', () => {
    it('parks the queue and releases the rows on a 400', async () => {
      await db.expenses.put(expenseRow({ isDeleted: true }))
      await queue('Delete')

      await new SyncEngine(failingApi(400), () => true).flush()

      expect((await db.outbox.get('op-Delete'))!.status).toBe('rejected')
      expect((await db.expenses.get('expense-1'))!.isDeleted).toBe(false)
    })

    it('keeps waiting on a 500, which is the server having a bad day', async () => {
      await db.expenses.put(expenseRow({ isDeleted: true }))
      await queue('Delete')

      await new SyncEngine(failingApi(500), () => true).flush()

      expect((await db.outbox.get('op-Delete'))!.status).toBe('pending')
      expect((await db.expenses.get('expense-1'))!.isDeleted).toBe(true)
    })

    it('keeps waiting when there is no status at all, which is offline', async () => {
      await db.expenses.put(expenseRow())
      await queue('Update')

      const offline = {
        push: vi.fn(async () => {
          throw new Error('Could not reach the server.')
        }),
        pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
        acknowledge: vi.fn(async () => {}),
      }
      await new SyncEngine(offline, () => true).flush()

      expect((await db.outbox.get('op-Update'))!.status).toBe('pending')
      expect((await db.expenses.get('expense-1'))!.pending).toBe(true)
    })

    it('keeps waiting on a 401, which is a session to renew', async () => {
      await db.expenses.put(expenseRow())
      await queue('Update')

      await new SyncEngine(failingApi(401), () => true).flush()

      expect((await db.outbox.get('op-Update'))!.status).toBe('pending')
    })

    it('keeps waiting on a 429, which means not now rather than no', async () => {
      await db.expenses.put(expenseRow())
      await queue('Update')

      await new SyncEngine(failingApi(429), () => true).flush()

      expect((await db.outbox.get('op-Update'))!.status).toBe('pending')
    })
  })

  it('lets the next pull replace a released row', async () => {
    await db.expenses.put(expenseRow({ isDeleted: true, description: 'Stale' }))
    await queue('Delete')

    const api = refusingApi()
    const engine = new SyncEngine(api, () => true)
    await engine.flush()

    api.pull = vi.fn(async () => ({
      entries: [{
        serverSeq: 9,
        groupId: GROUP,
        entityType: 'Expense',
        entityId: 'expense-1',
        operation: 'Update',
        payloadJson: JSON.stringify({
          id: 'expense-1', groupId: GROUP, paidByMemberId: 'member-alice',
          description: 'What the server holds', amount: 30, currency: 'CAD',
          amountInBaseCurrency: 30, exchangeRate: 1, spentAt: '2026-01-01T12:00:00.000Z',
          splitType: 0, revision: 2, isDeleted: false, splits: [], items: [],
        }),
        vectorClock: {},
        occurredAt: new Date().toISOString(),
      }],
      groupCursors: { [GROUP]: 9 },
      snapshots: [],
      hasMore: false,
    })) as never
    await engine.pull()

    expect((await db.expenses.get('expense-1'))!.description).toBe('What the server holds')
  })
})
