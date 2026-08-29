import { beforeEach, describe, expect, it, vi } from 'vitest'
import { db, getCursor, resetDatabase, setCursor } from '@/offline/db'
import { SyncEngine } from '@/offline/syncEngine'
import type { SyncApi } from '@/offline/syncEngine'

const groupId = 'group-1'
const memberId = 'member-1'

function fakeApi(overrides: Partial<SyncApi> = {}): SyncApi {
  return {
    push: vi.fn(async (request) => ({
      accepted: request.operations.map((operation) => ({
        operationId: operation.operationId,
        entityId: operation.entityId,
        serverSeq: 1,
        vectorClock: operation.vectorClock,
      })),
      conflicts: [],
      rejected: [],
      groupCursors: { [groupId]: 1 },
    })),
    pull: vi.fn(async () => ({
      entries: [],
      groupCursors: {},
      snapshots: [],
      hasMore: false,
    })),
    acknowledge: vi.fn(async () => {}),
    ...overrides,
  }
}

async function seedExpense(id: string, pending = true) {
  await db.expenses.put({
    id,
    groupId,
    paidByMemberId: memberId,
    description: 'Dinner',
    amount: 40,
    currency: 'CAD',
    amountInBaseCurrency: 40,
    exchangeRate: 1,
    spentAt: '2026-01-01T12:00:00Z',
    splitType: 'Equal',
    splits: [{ memberId, amount: 40, amountInBaseCurrency: 40, inputValue: null }],
    items: [],
    revision: 1,
    isDeleted: false,
    vectorClock: { 'device-a': 1 },
    serverSeq: 0,
    pending,
  })
}

describe('offline sync engine', () => {
  beforeEach(async () => {
    await resetDatabase()
  })

  it('queues a change instead of sending it immediately', async () => {
    const api = fakeApi()
    const engine = new SyncEngine(api, () => false)

    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: { description: 'Dinner', amount: 40 },
    })

    expect(await db.outbox.count()).toBe(1)
    expect(api.push).not.toHaveBeenCalled()
  })

  it('stamps a queued change with a ticked clock', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)

    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    const [operation] = await db.outbox.toArray()
    expect(Object.values(operation.vectorClock).some((v) => v >= 1)).toBe(true)
  })

  it('advances the clock across successive changes to the same entity', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    const change = {
      entityType: 'Expense' as const,
      entityId: 'expense-1',
      groupId,
      payload: {},
    }

    await engine.enqueue({ ...change, operation: 'Create' })
    await engine.enqueue({ ...change, operation: 'Update' })

    const operations = await db.outbox.orderBy('sequence').toArray()
    const deviceId = Object.keys(operations[0].vectorClock)[0]
    expect(operations[1].vectorClock[deviceId]).toBeGreaterThan(operations[0].vectorClock[deviceId])
  })

  it('keeps the order the user made the changes in', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)

    for (const id of ['expense-1', 'expense-2', 'expense-3']) {
      await engine.enqueue({
        entityType: 'Expense',
        entityId: id,
        operation: 'Create',
        groupId,
        payload: {},
      })
    }

    const operations = await db.outbox.orderBy('sequence').toArray()
    expect(operations.map((o) => o.entityId)).toEqual(['expense-1', 'expense-2', 'expense-3'])
  })

  it('survives a reload with the queue intact', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    // A brand new engine over the same database, as after a restart.
    const revived = new SyncEngine(fakeApi(), () => false)

    expect(await revived.pendingCount()).toBe(1)
  })

  it('pushes the queue when it comes back online', async () => {
    const api = fakeApi()
    const engine = new SyncEngine(api, () => true)
    await seedExpense('expense-1')
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()

    expect(api.push).toHaveBeenCalledTimes(1)
    expect(await db.outbox.count()).toBe(0)
  })

  it('clears the pending marker once the server accepts a change', async () => {
    const engine = new SyncEngine(fakeApi(), () => true)
    await seedExpense('expense-1')
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()

    expect((await db.expenses.get('expense-1'))?.pending).toBe(false)
  })

  it('records the server cursor after a push', async () => {
    const engine = new SyncEngine(fakeApi(), () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()

    expect(await getCursor(groupId)).toBe(1)
  })

  it('does nothing while offline', async () => {
    const api = fakeApi()
    const engine = new SyncEngine(api, () => false)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()

    expect(api.push).not.toHaveBeenCalled()
    expect(await db.outbox.count()).toBe(1)
  })

  it('keeps the queue when a push fails, so nothing is lost', async () => {
    const api = fakeApi({
      push: vi.fn(async () => {
        throw new Error('network down')
      }),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()

    expect(await db.outbox.count()).toBe(1)
    const [operation] = await db.outbox.toArray()
    expect(operation.status).toBe('pending')
    expect(operation.attempts).toBe(1)
    expect(operation.lastError).toContain('network down')
  })

  it('retries a failed push on the next flush', async () => {
    let calls = 0
    const api = fakeApi({
      push: vi.fn(async (request) => {
        calls += 1
        if (calls === 1) throw new Error('network down')
        return {
          accepted: request.operations.map((operation) => ({
            operationId: operation.operationId,
            entityId: operation.entityId,
            serverSeq: 2,
            vectorClock: operation.vectorClock,
          })),
          conflicts: [],
          rejected: [],
          groupCursors: { [groupId]: 2 },
        }
      }),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()
    await engine.flush()

    expect(await db.outbox.count()).toBe(0)
  })

  it('keeps a rejected operation out of the queue but reports it', async () => {
    const api = fakeApi({
      push: vi.fn(async (request) => ({
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
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    const result = await engine.flush()

    // Retrying forever would block every later change behind an operation the
    // server will never accept.
    expect(result.rejected).toHaveLength(1)
    const [operation] = await db.outbox.toArray()
    expect(operation.status).toBe('rejected')
  })

  it('does not resend a rejected operation', async () => {
    const api = fakeApi({
      push: vi.fn(async (request) => ({
        accepted: [],
        conflicts: [],
        rejected: request.operations.map((operation) => ({
          operationId: operation.operationId,
          entityId: operation.entityId,
          reason: 'bad',
          code: 'InvalidPayload',
        })),
        groupCursors: {},
      })),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    await engine.flush()
    await engine.flush()

    expect(api.push).toHaveBeenCalledTimes(1)
  })

  it('stores a conflict for the user to resolve', async () => {
    const api = fakeApi({
      push: vi.fn(async (request) => ({
        accepted: [],
        conflicts: [
          {
            conflictId: 'conflict-1',
            groupId,
            entityType: 'Expense',
            entityId: request.operations[0].entityId,
            storedPayloadJson: '{"description":"Theirs"}',
            storedVectorClock: {},
            incomingPayloadJson: '{"description":"Mine"}',
            incomingVectorClock: {},
            conflictingFields: ['description'],
            detectedAt: '2026-01-01T00:00:00Z',
          },
        ],
        rejected: [],
        groupCursors: {},
      })),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Update',
      groupId,
      payload: {},
    })

    const result = await engine.flush()

    expect(result.conflicts).toHaveLength(1)
    expect(await db.conflicts.count()).toBe(1)
    // The losing edit is not silently dropped: it lives in the conflict record.
    expect((await db.conflicts.get('conflict-1'))?.conflictingFields).toEqual(['description'])
  })

  it('applies a pulled expense into the local replica', async () => {
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [
          {
            serverSeq: 5,
            groupId,
            entityType: 'Expense',
            entityId: 'expense-remote',
            operation: 'Create',
            deviceId: 'device-b',
            payloadJson: JSON.stringify({
              id: 'expense-remote',
              groupId,
              paidByMemberId: memberId,
              description: 'From another device',
              amount: 25,
              currency: 'CAD',
              amountInBaseCurrency: 25,
              exchangeRate: 1,
              spentAt: '2026-02-01T12:00:00Z',
              splitType: 1,
              splits: [{ memberId, amount: 25, amountInBaseCurrency: 25 }],
              items: [],
              revision: 1,
              isDeleted: false,
            }),
            vectorClock: { 'device-b': 1 },
            lineageId: 'lineage-1',
            sourceGroupId: null,
            counterpartGroupId: null,
            createdAt: '2026-02-01T12:00:00Z',
          },
        ],
        groupCursors: { [groupId]: 5 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    const stored = await db.expenses.get('expense-remote')
    expect(stored?.description).toBe('From another device')
    expect(stored?.pending).toBe(false)
    expect(await getCursor(groupId)).toBe(5)
  })

  it('applies a pulled delete as a tombstone', async () => {
    await seedExpense('expense-1', false)
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [
          {
            serverSeq: 6,
            groupId,
            entityType: 'Expense',
            entityId: 'expense-1',
            operation: 'Delete',
            deviceId: 'device-b',
            payloadJson: '{}',
            vectorClock: { 'device-b': 2 },
            lineageId: 'lineage-1',
            sourceGroupId: null,
            counterpartGroupId: null,
            createdAt: '2026-02-01T12:00:00Z',
          },
        ],
        groupCursors: { [groupId]: 6 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect((await db.expenses.get('expense-1'))?.isDeleted).toBe(true)
  })

  it('does not let a pull overwrite a row still marked unsent', async () => {
    await seedExpense('expense-1', true)
    await db.expenses.update('expense-1', { description: 'My unsent edit' })

    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [
          {
            serverSeq: 7,
            groupId,
            entityType: 'Expense',
            entityId: 'expense-1',
            operation: 'Update',
            deviceId: 'device-b',
            payloadJson: JSON.stringify({
              id: 'expense-1',
              groupId,
              paidByMemberId: memberId,
              description: 'Server version',
              amount: 40,
              currency: 'CAD',
              amountInBaseCurrency: 40,
              spentAt: '2026-01-01T12:00:00Z',
              splits: [],
              items: [],
              isDeleted: false,
            }),
            vectorClock: { 'device-b': 1 },
            lineageId: 'lineage-1',
            sourceGroupId: null,
            counterpartGroupId: null,
            createdAt: '2026-02-01T12:00:00Z',
          },
        ],
        groupCursors: { [groupId]: 7 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    // Overwriting would throw away work the person can still see on screen.
    expect((await db.expenses.get('expense-1'))?.description).toBe('My unsent edit')
  })

  it('does not let a pull overwrite an edit sitting in the outbox', async () => {
    await seedExpense('expense-1', false)
    await db.expenses.update('expense-1', { description: 'Queued edit' })
    const engine = new SyncEngine(fakeApi(), () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Update',
      groupId,
      payload: { description: 'Queued edit' },
    })

    const pullingEngine = new SyncEngine(
      fakeApi({
        pull: vi.fn(async () => ({
          entries: [
            {
              serverSeq: 9,
              groupId,
              entityType: 'Expense',
              entityId: 'expense-1',
              operation: 'Update',
              deviceId: 'device-b',
              payloadJson: JSON.stringify({
                id: 'expense-1',
                groupId,
                paidByMemberId: memberId,
                description: 'Server version',
                amount: 40,
                currency: 'CAD',
                spentAt: '2026-01-01T12:00:00Z',
                splits: [],
                items: [],
                isDeleted: false,
              }),
              vectorClock: { 'device-b': 1 },
              lineageId: 'lineage-1',
              sourceGroupId: null,
              counterpartGroupId: null,
              createdAt: '2026-02-01T12:00:00Z',
            },
          ],
          groupCursors: { [groupId]: 9 },
          snapshots: [],
          hasMore: false,
        })),
      }),
      () => true,
    )

    await pullingEngine.pull()

    expect((await db.expenses.get('expense-1'))?.description).toBe('Queued edit')
  })

  it('applies a pull once the local edit has been accepted', async () => {
    await seedExpense('expense-1', false)

    const engine = new SyncEngine(
      fakeApi({
        pull: vi.fn(async () => ({
          entries: [
            {
              serverSeq: 10,
              groupId,
              entityType: 'Expense',
              entityId: 'expense-1',
              operation: 'Update',
              deviceId: 'device-b',
              payloadJson: JSON.stringify({
                id: 'expense-1',
                groupId,
                paidByMemberId: memberId,
                description: 'Server version',
                amount: 40,
                currency: 'CAD',
                spentAt: '2026-01-01T12:00:00Z',
                splits: [],
                items: [],
                isDeleted: false,
              }),
              vectorClock: { 'device-b': 1 },
              lineageId: 'lineage-1',
              sourceGroupId: null,
              counterpartGroupId: null,
              createdAt: '2026-02-01T12:00:00Z',
            },
          ],
          groupCursors: { [groupId]: 10 },
          snapshots: [],
          hasMore: false,
        })),
      }),
      () => true,
    )

    await engine.pull()

    expect((await db.expenses.get('expense-1'))?.description).toBe('Server version')
  })

  it('pulls from the cursor it already has', async () => {
    await setCursor(groupId, 12)
    const api = fakeApi()
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect(api.pull).toHaveBeenCalledWith(
      expect.objectContaining({ groupCursors: { [groupId]: 12 } }),
    )
  })

  it('keeps pulling while the server says there is more', async () => {
    let call = 0
    const api = fakeApi({
      pull: vi.fn(async () => {
        call += 1
        return {
          entries: [],
          groupCursors: { [groupId]: call },
          snapshots: [],
          hasMore: call < 3,
        }
      }),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect(api.pull).toHaveBeenCalledTimes(3)
  })

  it('stops pulling at a safety limit rather than looping forever', async () => {
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [],
        groupCursors: { [groupId]: 1 },
        snapshots: [],
        // A server that always claims more would otherwise spin the client.
        hasMore: true,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect((api.pull as ReturnType<typeof vi.fn>).mock.calls.length).toBeLessThanOrEqual(50)
  })

  it('acknowledges the cursors it applied', async () => {
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [],
        groupCursors: { [groupId]: 9 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect(api.acknowledge).toHaveBeenCalledWith({ [groupId]: 9 })
  })

  it('does not pull while offline', async () => {
    const api = fakeApi()
    const engine = new SyncEngine(api, () => false)

    await engine.pull()

    expect(api.pull).not.toHaveBeenCalled()
  })

  it('runs one sync at a time', async () => {
    let inflight = 0
    let maxInflight = 0
    const api = fakeApi({
      push: vi.fn(async (request) => {
        inflight += 1
        maxInflight = Math.max(maxInflight, inflight)
        await new Promise((resolve) => setTimeout(resolve, 10))
        inflight -= 1
        return {
          accepted: request.operations.map((operation) => ({
            operationId: operation.operationId,
            entityId: operation.entityId,
            serverSeq: 1,
            vectorClock: operation.vectorClock,
          })),
          conflicts: [],
          rejected: [],
          groupCursors: {},
        }
      }),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })

    // Two overlapping flushes must not send the same operation twice.
    await Promise.all([engine.flush(), engine.flush()])

    expect(maxInflight).toBe(1)
  })

  it('reports the pending count for the sync indicator', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-2',
      operation: 'Create',
      groupId,
      payload: {},
    })

    expect(await engine.pendingCount()).toBe(2)
  })

  it('reports rejected operations separately from pending ones', async () => {
    const api = fakeApi({
      push: vi.fn(async (request) => ({
        accepted: [],
        conflicts: [],
        rejected: request.operations.map((operation) => ({
          operationId: operation.operationId,
          entityId: operation.entityId,
          reason: 'bad',
          code: 'InvalidPayload',
        })),
        groupCursors: {},
      })),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })
    await engine.flush()

    expect(await engine.pendingCount()).toBe(0)
    expect(await engine.rejectedCount()).toBe(1)
  })

  it('can discard a rejected operation the user gave up on', async () => {
    const api = fakeApi({
      push: vi.fn(async (request) => ({
        accepted: [],
        conflicts: [],
        rejected: request.operations.map((operation) => ({
          operationId: operation.operationId,
          entityId: operation.entityId,
          reason: 'bad',
          code: 'InvalidPayload',
        })),
        groupCursors: {},
      })),
    })
    const engine = new SyncEngine(api, () => true)
    await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })
    await engine.flush()
    const [operation] = await db.outbox.toArray()

    await engine.discard(operation.operationId)

    expect(await db.outbox.count()).toBe(0)
  })

  it('applies a pulled settlement', async () => {
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [
          {
            serverSeq: 3,
            groupId,
            entityType: 'Settlement',
            entityId: 'settlement-1',
            operation: 'Create',
            deviceId: 'device-b',
            payloadJson: JSON.stringify({
              id: 'settlement-1',
              groupId,
              fromMemberId: 'member-2',
              toMemberId: memberId,
              amount: 25,
              currency: 'CAD',
              amountInBaseCurrency: 25,
              settledAt: '2026-02-01T12:00:00Z',
              isDeleted: false,
            }),
            vectorClock: { 'device-b': 1 },
            lineageId: 'lineage-1',
            sourceGroupId: null,
            counterpartGroupId: null,
            createdAt: '2026-02-01T12:00:00Z',
          },
        ],
        groupCursors: { [groupId]: 3 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect((await db.settlements.get('settlement-1'))?.amount).toBe(25)
  })

  it('applies a pulled comment', async () => {
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [
          {
            serverSeq: 4,
            groupId,
            entityType: 'ExpenseComment',
            entityId: 'comment-1',
            operation: 'Create',
            deviceId: 'device-b',
            payloadJson: JSON.stringify({
              id: 'comment-1',
              expenseId: 'expense-1',
              groupId,
              authorMemberId: memberId,
              body: 'Nice one',
            }),
            vectorClock: { 'device-b': 1 },
            lineageId: 'lineage-1',
            sourceGroupId: null,
            counterpartGroupId: null,
            createdAt: '2026-02-01T12:00:00Z',
          },
        ],
        groupCursors: { [groupId]: 4 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    expect((await db.comments.get('comment-1'))?.body).toBe('Nice one')
  })

  it('ignores an entry it cannot parse rather than failing the whole pull', async () => {
    const api = fakeApi({
      pull: vi.fn(async () => ({
        entries: [
          {
            serverSeq: 8,
            groupId,
            entityType: 'Expense',
            entityId: 'broken',
            operation: 'Create',
            deviceId: 'device-b',
            payloadJson: 'not json',
            vectorClock: {},
            lineageId: 'lineage-1',
            sourceGroupId: null,
            counterpartGroupId: null,
            createdAt: '2026-02-01T12:00:00Z',
          },
        ],
        groupCursors: { [groupId]: 8 },
        snapshots: [],
        hasMore: false,
      })),
    })
    const engine = new SyncEngine(api, () => true)

    await engine.pull()

    // The cursor still advances, or the client would re-fetch the bad entry forever.
    expect(await getCursor(groupId)).toBe(8)
    expect(await db.expenses.get('broken')).toBeUndefined()
  })
})

describe('pulled payload tolerance', () => {
  beforeEach(async () => {
    await resetDatabase()
  })

  function pullingEngine(payload: Record<string, unknown>, entityType = 'Expense') {
    return new SyncEngine(
      fakeApi({
        pull: vi.fn(async () => ({
          entries: [
            {
              serverSeq: 20,
              groupId,
              entityType,
              entityId: 'entity-1',
              operation: 'Create',
              deviceId: 'device-b',
              payloadJson: JSON.stringify(payload),
              vectorClock: { 'device-b': 1 },
              lineageId: 'lineage-1',
              sourceGroupId: null,
              counterpartGroupId: null,
              createdAt: '2026-02-01T12:00:00Z',
            },
          ],
          groupCursors: { [groupId]: 20 },
          snapshots: [],
          hasMore: false,
        })),
      }),
      () => true,
    )
  }

  it('fills in the fields a sparse payload leaves out', async () => {
    // The server omits nulls, so the client must not choke on a minimal snapshot.
    await pullingEngine({ id: 'entity-1', paidByMemberId: memberId, description: 'Sparse' }).pull()

    const stored = await db.expenses.get('entity-1')
    expect(stored?.currency).toBe('CAD')
    expect(stored?.exchangeRate).toBe(1)
    expect(stored?.revision).toBe(1)
    expect(stored?.splits).toEqual([])
    expect(stored?.items).toEqual([])
  })

  it('reads a numeric split type, which the sync snapshot uses', async () => {
    await pullingEngine({
      id: 'entity-1',
      paidByMemberId: memberId,
      description: 'Numeric',
      amount: 10,
      splitType: 2,
    }).pull()

    expect((await db.expenses.get('entity-1'))?.splitType).toBe('Shares')
  })

  it('reads a named split type, which the HTTP API uses', async () => {
    await pullingEngine({
      id: 'entity-1',
      paidByMemberId: memberId,
      description: 'Named',
      amount: 10,
      splitType: 'Percentage',
    }).pull()

    expect((await db.expenses.get('entity-1'))?.splitType).toBe('Percentage')
  })

  it('falls back to an equal split for an unrecognised type', async () => {
    await pullingEngine({
      id: 'entity-1',
      paidByMemberId: memberId,
      description: 'Odd',
      amount: 10,
      splitType: 'Nonsense',
    }).pull()

    expect((await db.expenses.get('entity-1'))?.splitType).toBe('Equal')
  })

  it('accepts items keyed as members or memberIds', async () => {
    await pullingEngine({
      id: 'entity-1',
      paidByMemberId: memberId,
      description: 'Itemized',
      amount: 10,
      items: [
        { description: 'A', amount: 5, members: [memberId] },
        { description: 'B', amount: 5, memberIds: [memberId] },
      ],
    }).pull()

    const stored = await db.expenses.get('entity-1')
    expect(stored?.items.every((item) => item.memberIds.length === 1)).toBe(true)
  })

  it('fills in a sparse settlement payload', async () => {
    await pullingEngine(
      { id: 'entity-1', fromMemberId: 'a', toMemberId: 'b' },
      'Settlement',
    ).pull()

    const stored = await db.settlements.get('entity-1')
    expect(stored?.currency).toBe('CAD')
    expect(stored?.amount).toBe(0)
  })

  it('fills in a sparse comment payload', async () => {
    await pullingEngine({ id: 'entity-1', expenseId: 'e', body: 'Hi' }, 'ExpenseComment').pull()

    expect((await db.comments.get('entity-1'))?.isDeleted).toBe(false)
  })

  it('ignores an entity type it does not handle', async () => {
    await pullingEngine({ id: 'entity-1' }, 'CategoryRule').pull()

    expect(await getCursor(groupId)).toBe(20)
  })

  it('ignores a delete for an entity type it does not handle', async () => {
    const engine = new SyncEngine(
      fakeApi({
        pull: vi.fn(async () => ({
          entries: [
            {
              serverSeq: 21,
              groupId,
              entityType: 'CategoryRule',
              entityId: 'entity-1',
              operation: 'Delete',
              deviceId: 'device-b',
              payloadJson: '{}',
              vectorClock: {},
              lineageId: 'lineage-1',
              sourceGroupId: null,
              counterpartGroupId: null,
              createdAt: '2026-02-01T12:00:00Z',
            },
          ],
          groupCursors: { [groupId]: 21 },
          snapshots: [],
          hasMore: false,
        })),
      }),
      () => true,
    )

    await engine.pull()

    expect(await getCursor(groupId)).toBe(21)
  })

  it('retries a rejected operation the user fixed', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    const operation = await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Create',
      groupId,
      payload: {},
    })
    await db.outbox.update(operation.operationId, { status: 'rejected', lastError: 'bad' })

    await engine.retry(operation.operationId)

    const reloaded = await db.outbox.get(operation.operationId)
    expect(reloaded?.status).toBe('pending')
    expect(reloaded?.lastError).toBeNull()
  })

  it('ticks from the newest queued clock for an entity, not the stored one', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    await seedExpense('expense-1', false)

    const first = await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Update',
      groupId,
      payload: {},
    })
    const second = await engine.enqueue({
      entityType: 'Expense',
      entityId: 'expense-1',
      operation: 'Update',
      groupId,
      payload: {},
    })

    const device = Object.keys(second.vectorClock).find((key) => key !== 'device-a')!
    expect(second.vectorClock[device]).toBe(first.vectorClock[device] + 1)
  })

  it('ticks from a settlement clock when that is what the entity is', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    await db.settlements.put({
      id: 'settlement-1',
      groupId,
      fromMemberId: 'a',
      toMemberId: 'b',
      amount: 10,
      currency: 'CAD',
      amountInBaseCurrency: 10,
      settledAt: '2026-01-01T00:00:00Z',
      isDeleted: false,
      vectorClock: { 'device-z': 4 },
      serverSeq: 1,
      pending: false,
    })

    const operation = await engine.enqueue({
      entityType: 'Settlement',
      entityId: 'settlement-1',
      operation: 'Update',
      groupId,
      payload: {},
    })

    expect(operation.vectorClock['device-z']).toBe(4)
  })

  it('ticks from a comment clock when that is what the entity is', async () => {
    const engine = new SyncEngine(fakeApi(), () => false)
    await db.comments.put({
      id: 'comment-1',
      expenseId: 'expense-1',
      groupId,
      authorMemberId: 'a',
      parentCommentId: null,
      body: 'Hi',
      createdAt: '2026-01-01T00:00:00Z',
      isDeleted: false,
      vectorClock: { 'device-y': 7 },
      pending: false,
    })

    const operation = await engine.enqueue({
      entityType: 'ExpenseComment',
      entityId: 'comment-1',
      operation: 'Update',
      groupId,
      payload: {},
    })

    expect(operation.vectorClock['device-y']).toBe(7)
  })
})
