import { beforeEach, describe, expect, it } from 'vitest'
import { db, resetDatabase, getDeviceId, getCursor, setCursor } from '@/offline/db'

describe('local database', () => {
  beforeEach(async () => {
    await resetDatabase()
  })

  it('creates a device id on first use', async () => {
    const deviceId = await getDeviceId()

    expect(deviceId).toMatch(/^[0-9a-f-]{36}$/)
  })

  it('keeps the same device id across calls', async () => {
    const first = await getDeviceId()
    const second = await getDeviceId()

    // The device id is the key in every vector clock. A new one per session would
    // make each launch look like a brand new peer and conflict with itself.
    expect(second).toBe(first)
  })

  it('starts every group cursor at zero', async () => {
    expect(await getCursor('group-1')).toBe(0)
  })

  it('remembers a cursor', async () => {
    await setCursor('group-1', 42)

    expect(await getCursor('group-1')).toBe(42)
  })

  it('never moves a cursor backwards', async () => {
    await setCursor('group-1', 42)
    await setCursor('group-1', 7)

    // Replaying history the device already applied would resurrect deleted rows.
    expect(await getCursor('group-1')).toBe(42)
  })

  it('tracks cursors per group', async () => {
    await setCursor('group-1', 10)
    await setCursor('group-2', 3)

    expect(await getCursor('group-1')).toBe(10)
    expect(await getCursor('group-2')).toBe(3)
  })

  it('stores an expense and reads it back', async () => {
    await db.expenses.put({
      id: 'expense-1',
      groupId: 'group-1',
      paidByMemberId: 'member-1',
      description: 'Dinner',
      amount: 40,
      currency: 'CAD',
      amountInBaseCurrency: 40,
      exchangeRate: 1,
      spentAt: '2026-01-01T12:00:00Z',
      splitType: 'Equal',
      splits: [{ memberId: 'member-1', amount: 40, amountInBaseCurrency: 40, inputValue: null }],
      items: [],
      revision: 1,
      isDeleted: false,
      vectorClock: { 'device-a': 1 },
      serverSeq: 1,
      pending: false,
    })

    const stored = await db.expenses.get('expense-1')
    expect(stored?.description).toBe('Dinner')
  })

  it('can list a group expenses by date', async () => {
    for (const [id, date] of [
      ['expense-1', '2026-01-01T12:00:00Z'],
      ['expense-2', '2026-03-01T12:00:00Z'],
    ]) {
      await db.expenses.put({
        id,
        groupId: 'group-1',
        paidByMemberId: 'member-1',
        description: id,
        amount: 10,
        currency: 'CAD',
        amountInBaseCurrency: 10,
        exchangeRate: 1,
        spentAt: date,
        splitType: 'Equal',
        splits: [],
        items: [],
        revision: 1,
        isDeleted: false,
        vectorClock: {},
        serverSeq: 0,
        pending: false,
      })
    }

    const expenses = await db.expenses.where('groupId').equals('group-1').sortBy('spentAt')

    expect(expenses.map((e) => e.id)).toEqual(['expense-1', 'expense-2'])
  })

  it('resets cleanly between sessions', async () => {
    await setCursor('group-1', 42)
    await resetDatabase()

    expect(await getCursor('group-1')).toBe(0)
  })
})
