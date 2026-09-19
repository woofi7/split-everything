import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { isoDate, lastExpenseDate, rememberExpenseDate, today } from '@/domain/lastExpenseDate'

describe('the date an expense starts on', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.useRealTimers())

  it('has nothing to offer on a device that has added nothing', () => {
    expect(lastExpenseDate()).toBeNull()
  })

  it('offers back the date last used, within the day it was used', () => {
    rememberExpenseDate('2026-03-14')

    expect(lastExpenseDate()).toBe('2026-03-14')
  })

  it('forgets a date used on an earlier day', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 8, 13, 21, 0))
    rememberExpenseDate('2026-08-04')
    expect(lastExpenseDate()).toBe('2026-08-04')

    vi.setSystemTime(new Date(2026, 8, 14, 9, 0))

    expect(lastExpenseDate()).toBeNull()
  })

  it('keeps it across the evening it was typed in', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 8, 14, 20, 15))
    rememberExpenseDate('2026-08-04')

    vi.setSystemTime(new Date(2026, 8, 14, 23, 45))

    expect(lastExpenseDate()).toBe('2026-08-04')
  })

  it('ignores anything that is not a calendar date', () => {
    localStorage.setItem('split-everything.last-expense-date', 'yesterday')

    expect(lastExpenseDate()).toBeNull()
  })

  it('ignores the shape the first version stored, which never expired', () => {
    localStorage.setItem('split-everything.last-expense-date', '2026-08-04')

    expect(lastExpenseDate()).toBeNull()
  })

  it('refuses to store anything that is not one either', () => {
    rememberExpenseDate('14/03/2026')

    expect(lastExpenseDate()).toBeNull()
  })

  it('reads today in the timezone the person is in, not in UTC', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-03-14T23:30:00-04:00'))

    expect(today()).toBe('2026-03-14')
  })

  it('pads a single-figure month and day, which is what a date input wants', () => {
    expect(isoDate(new Date(2026, 2, 7))).toBe('2026-03-07')
  })

  it('survives a browser that refuses storage outright', () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('The browser is blocking site data.')
    })
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('The browser is blocking site data.')
    })

    expect(() => rememberExpenseDate('2026-03-14')).not.toThrow()
    expect(lastExpenseDate()).toBeNull()

    getItem.mockRestore()
    setItem.mockRestore()
  })
})
