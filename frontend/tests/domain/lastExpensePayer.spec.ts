import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { lastExpensePayer, rememberExpensePayer } from '@/domain/lastExpensePayer'

describe('who paid the last expense', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.useRealTimers())

  it('has nobody to offer on a device that has added nothing', () => {
    expect(lastExpensePayer('group-1')).toBeNull()
  })

  it('offers back the payer last used in that group, within the day', () => {
    rememberExpensePayer('group-1', 'member-bob')
    rememberExpensePayer('group-2', 'member-carol')

    expect(lastExpensePayer('group-1')).toBe('member-bob')
    expect(lastExpensePayer('group-2')).toBe('member-carol')
  })

  it('forgets a payer chosen on an earlier day', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 8, 13, 21, 0))
    rememberExpensePayer('group-1', 'member-bob')

    vi.setSystemTime(new Date(2026, 8, 14, 9, 0))

    expect(lastExpensePayer('group-1')).toBeNull()
  })

  it('survives whatever else is sitting in storage', () => {
    localStorage.setItem('split-everything.last-expense-payer', 'not json')

    expect(lastExpensePayer('group-1')).toBeNull()
    rememberExpensePayer('group-1', 'member-bob')
    expect(lastExpensePayer('group-1')).toBe('member-bob')
  })

  it('survives a browser that refuses storage outright', () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('The browser is blocking site data.')
    })
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('The browser is blocking site data.')
    })

    expect(() => rememberExpensePayer('group-1', 'member-bob')).not.toThrow()
    expect(lastExpensePayer('group-1')).toBeNull()

    getItem.mockRestore()
    setItem.mockRestore()
  })
})
