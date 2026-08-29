import { describe, expect, it } from 'vitest'
import { computeFingerprint, normalizeMerchant } from '@/domain/fingerprint'

const day = new Date('2026-08-31T12:00:00Z')

/**
 * This is a cross-boundary contract, not just a helper. The statement importer
 * runs entirely in the browser and asks the server "have I already got these?"
 * by fingerprint alone. If the two implementations disagree by one character,
 * duplicate detection silently stops working and people get double-charged.
 *
 * The expected hashes below are the values the C# implementation produces for the
 * same inputs, so a drift on either side fails here.
 */
describe('expense fingerprint', () => {
  it('matches the value the server computes for a known transaction', async () => {
    const fingerprint = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')

    expect(fingerprint).toBe('c47875b9384c326c74638e1329dc036e')
  })

  it('is 32 hex characters, like the server', async () => {
    const fingerprint = await computeFingerprint(day, 10, 'CAD', 'Coffee')

    expect(fingerprint).toMatch(/^[0-9a-f]{32}$/)
  })

  it('is stable for the same transaction', async () => {
    const a = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')
    const b = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')

    expect(a).toBe(b)
  })

  it('changes with the amount', async () => {
    const a = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')
    const b = await computeFingerprint(day, 42.51, 'CAD', 'Uber Eats')

    expect(a).not.toBe(b)
  })

  it('changes with the day', async () => {
    const a = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')
    const b = await computeFingerprint(new Date('2026-09-01T12:00:00Z'), 42.5, 'CAD', 'Uber Eats')

    expect(a).not.toBe(b)
  })

  it('changes with the currency', async () => {
    const a = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')
    const b = await computeFingerprint(day, 42.5, 'USD', 'Uber Eats')

    expect(a).not.toBe(b)
  })

  it('ignores the time of day', async () => {
    const a = await computeFingerprint(day, 10, 'CAD', 'Coffee')
    const b = await computeFingerprint(new Date('2026-08-31T23:59:00Z'), 10, 'CAD', 'Coffee')

    expect(a).toBe(b)
  })

  it('ignores currency case', async () => {
    const a = await computeFingerprint(day, 10, 'cad', 'Coffee')
    const b = await computeFingerprint(day, 10, 'CAD', 'Coffee')

    expect(a).toBe(b)
  })

  it('treats a refund like the charge it reverses', async () => {
    const a = await computeFingerprint(day, -10, 'CAD', 'Coffee')
    const b = await computeFingerprint(day, 10, 'CAD', 'Coffee')

    expect(a).toBe(b)
  })

  it('matches a hand-typed expense against the statement line that duplicates it', async () => {
    const statement = await computeFingerprint(day, 42.5, 'CAD', 'UBER EATS 8829 TORONTO ON')
    const manual = await computeFingerprint(day, 42.5, 'CAD', 'Uber Eats')

    expect(statement).toBe(manual)
  })

  it('does not collide two different merchants', async () => {
    const a = await computeFingerprint(day, 10, 'CAD', 'Metro')
    const b = await computeFingerprint(day, 10, 'CAD', 'Loblaws')

    expect(a).not.toBe(b)
  })
})

describe('merchant normalisation', () => {
  it.each([
    ['UBER   EATS', 'uber eats'],
    ['Uber-Eats', 'UBER EATS'],
    ['UBER EATS #1234', 'Uber Eats'],
    ['  Uber Eats  ', 'Uber Eats'],
  ])('normalises %s and %s the same way', (left, right) => {
    expect(normalizeMerchant(left)).toBe(normalizeMerchant(right))
  })

  it('keeps only the leading merchant tokens', () => {
    expect(normalizeMerchant('METRO PLUS MARCHE ANDRE TREMBLAY MONTREAL QC')).toBe('METRO PLUS')
  })

  it('strips long reference numbers', () => {
    expect(normalizeMerchant('AMZN MKTP CA 123456789')).toBe('AMZN MKTP')
  })

  it('keeps short numbers that are part of a name', () => {
    expect(normalizeMerchant('Cafe 22')).toBe('CAFE 22')
  })

  it.each(['', '   '])('normalises %s to nothing', (input) => {
    expect(normalizeMerchant(input)).toBe('')
  })

  it('keeps merchants that share a first word apart', () => {
    expect(normalizeMerchant('UBER EATS TORONTO')).not.toBe(normalizeMerchant('UBER TRIP TORONTO'))
  })
})
