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

  it('offers back the date last used, within the day it was used', () => {
    rememberExpenseDate('2026-03-14')

    expect(lastExpenseDate()).toBe('2026-03-14')
  })

  /**
   * The bound that matters.
   *
   * Kept for ever, this is a trap: an evening spent entering August receipts left
   * every expense added afterwards dated in August, filed under a month heading
   * the dashboard keeps closed. It looked exactly like an expense that had not
   * saved, and the app said nothing.
   */
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

    // A date input given nonsense shows blank with nothing to say why, so the form
    // falls back to today instead.
    expect(lastExpenseDate()).toBeNull()
  })

  it('ignores the shape the first version stored, which never expired', () => {
    localStorage.setItem('split-everything.last-expense-date', '2026-08-04')

    // A device that met that version starts from today rather than from whenever
    // it last stopped typing.
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
