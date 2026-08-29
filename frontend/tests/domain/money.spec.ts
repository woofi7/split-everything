import { describe, expect, it } from 'vitest'
import {
  currencyDecimals,
  minorUnit,
  roundMoney,
  formatMoney,
  parseAmountInput,
} from '@/domain/money'

describe('currency precision', () => {
  it.each([
    ['CAD', 2],
    ['USD', 2],
    ['EUR', 2],
    ['JPY', 0],
    ['KRW', 0],
    ['KWD', 3],
    ['TND', 3],
  ])('%s has %i minor digits', (currency, expected) => {
    expect(currencyDecimals(currency)).toBe(expected)
  })

  it('is case insensitive', () => {
    expect(currencyDecimals('jpy')).toBe(0)
  })

  it.each([undefined, '', 'ZZZ'])('falls back to two digits for %s', (currency) => {
    expect(currencyDecimals(currency)).toBe(2)
  })

  it.each([
    ['CAD', 0.01],
    ['JPY', 1],
    ['KWD', 0.001],
  ])('reports the minor unit of %s', (currency, expected) => {
    expect(minorUnit(currency)).toBe(expected)
  })
})

describe('rounding', () => {
  it('rounds to the currency precision', () => {
    expect(roundMoney(1.005, 'CAD')).toBe(1.0)
    expect(roundMoney(1.6, 'JPY')).toBe(2)
    expect(roundMoney(1.0005, 'KWD')).toBe(1.0)
  })

  it('matches the server on banker rounding, so a client-computed split agrees', () => {
    expect(roundMoney(2.5, 'JPY')).toBe(2)
    expect(roundMoney(3.5, 'JPY')).toBe(4)
  })

  it('handles negatives', () => {
    expect(roundMoney(-1.005, 'CAD')).toBe(-1.0)
  })
})

describe('formatting', () => {
  it('shows the right number of decimals per currency', () => {
    expect(formatMoney(1234.5, 'CAD')).toContain('1,234.50')
    expect(formatMoney(1234, 'JPY')).not.toContain('.')
  })

  it('renders the amount with a currency symbol', () => {
    expect(formatMoney(10, 'CAD')).toBe('$10.00')
  })

  it('distinguishes a foreign currency from the local one', () => {
    // A EUR expense in a CAD group has to be visibly foreign in a list.
    expect(formatMoney(10, 'EUR')).not.toBe(formatMoney(10, 'CAD'))
  })

  it('renders an unknown but well-formed code with the code itself', () => {
    expect(formatMoney(10, 'ZZZ')).toBe('ZZZ\u00a010.00')
  })

  it('falls back to a plain rendering for a malformed code', () => {
    // Intl throws on anything that is not three letters; a list must still render.
    expect(formatMoney(10, 'Z')).toBe('10.00 Z')
  })

  it('formats zero', () => {
    expect(formatMoney(0, 'CAD')).toContain('0.00')
  })

  it('formats a negative amount', () => {
    expect(formatMoney(-5, 'CAD')).toContain('5.00')
  })
})

describe('parsing what a person types', () => {
  it.each([
    ['12.34', 12.34],
    ['12,34', 12.34],
    ['1 234,56', 1234.56],
    ['1,234.56', 1234.56],
    ['$12.34', 12.34],
    ['12.34 CAD', 12.34],
    ['-8.50', -8.5],
    ['12', 12],
  ])('reads %s as %f', (input, expected) => {
    expect(parseAmountInput(input)).toBe(expected)
  })

  it.each(['', '   ', 'abc', '-', '.'])('rejects %s', (input) => {
    expect(parseAmountInput(input)).toBeNull()
  })

  it('treats a trailing minus as negative, as some statements print it', () => {
    expect(parseAmountInput('42.00-')).toBe(-42)
  })

  it('treats parentheses as negative, as accounting exports do', () => {
    expect(parseAmountInput('(42.00)')).toBe(-42)
  })
})
