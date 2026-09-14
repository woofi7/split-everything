import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { isoDate, lastExpenseDate, rememberExpenseDate, today } from '@/domain/lastExpenseDate'

/**
 * The date a new expense starts on.
 *
 * Remembered so a run of receipts from the same evening is typed once rather than
 * six times, and read back defensively: it is a device preference, which means it
 * is a string somebody could have edited and a storage that can refuse to answer.
 */

describe('the date an expense starts on', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.useRealTimers())

  it('has nothing to offer on a device that has added nothing', () => {
    expect(lastExpenseDate()).toBeNull()
  })

  it('offers back the date last used', () => {
    rememberExpenseDate('2026-03-14')

    expect(lastExpenseDate()).toBe('2026-03-14')
  })

  it('ignores anything that is not a calendar date', () => {
    localStorage.setItem('split-everything.last-expense-date', 'yesterday')

    // A date input given nonsense shows blank with nothing to say why, so the form
    // falls back to today instead.
    expect(lastExpenseDate()).toBeNull()
  })

  it('refuses to store anything that is not one either', () => {
    rememberExpenseDate('14/03/2026')

    expect(lastExpenseDate()).toBeNull()
  })

  it('reads today in the timezone the person is in, not in UTC', () => {
    // Nine at night in Montreal is already tomorrow in UTC, and a form that opens
    // on a day that has not happened yet files the expense in the wrong week of
    // every total on the stats screen.
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
