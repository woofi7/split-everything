import { describe, expect, it } from 'vitest'
import { compileNamePattern, matchesAnyNamePattern } from '@/domain/namePatterns'

/**
 * Matching a typed pattern against an expense name.
 *
 * Globs, not regular expressions. "Loyer*" is what somebody writes when they mean
 * "anything starting with Loyer"; a regex reads it as "Loye then any number of r",
 * which matches "Loye" and misses every rent there has ever been.
 */
describe('matching a name against a pattern', () => {
  const matches = (pattern: string, name: string) => compileNamePattern(pattern)(name)

  describe('a pattern with no star', () => {
    it('matches a name that contains it', () => {
      expect(matches('Loyer', 'Loyer aout')).toBe(true)
      expect(matches('Loyer', 'Paiement loyer 2026')).toBe(true)
    })

    it('does not match a name that does not', () => {
      expect(matches('Loyer', 'Groceries at Metro')).toBe(false)
    })
  })

  describe('a pattern with a star', () => {
    it('matches from the beginning', () => {
      // The case that started this: Loyer* is not "Loye" and some r's.
      expect(matches('Loyer*', 'Loyer aout')).toBe(true)
      expect(matches('Loyer*', 'Loyer')).toBe(true)
      expect(matches('Loyer*', 'Paiement loyer')).toBe(false)
    })

    it('matches to the end', () => {
      expect(matches('*aout', 'Loyer aout')).toBe(true)
      expect(matches('*aout', 'aout Loyer')).toBe(false)
    })

    it('matches in the middle', () => {
      expect(matches('*loyer*', 'Paiement loyer aout')).toBe(true)
    })

    it('stands for any run of characters', () => {
      expect(matches('Loyer*2026', 'Loyer aout 2026')).toBe(true)
      expect(matches('Loyer*2026', 'Loyer aout 2025')).toBe(false)
    })
  })

  it('ignores case, because Loyer and loyer are the same rent', () => {
    expect(matches('loyer', 'LOYER AOUT')).toBe(true)
    expect(matches('LOYER*', 'loyer aout')).toBe(true)
  })

  it('treats punctuation as itself rather than as a pattern', () => {
    // A regex would read the dot as "any character" and the brackets as a group.
    expect(matches('Rent (flat)', 'Rent (flat) August')).toBe(true)
    expect(matches('a.b', 'axb')).toBe(false)
    expect(matches('a.b', 'a.b August')).toBe(true)
  })

  it('never throws on anything somebody might type', () => {
    for (const pattern of ['*((', '\\\\', '[a-', '?', '(', '+*']) {
      expect(() => compileNamePattern(pattern)('Loyer')).not.toThrow()
    }
  })

  it('matches nothing when the pattern is blank', () => {
    expect(matches('', 'Loyer')).toBe(false)
    expect(matches('   ', 'Loyer')).toBe(false)
  })

  it('takes any of several patterns', () => {
    expect(matchesAnyNamePattern('Hydro Quebec', ['Loyer*', 'Hydro*'])).toBe(true)
    expect(matchesAnyNamePattern('Pizza', ['Loyer*', 'Hydro*'])).toBe(false)
    expect(matchesAnyNamePattern('Pizza', [])).toBe(false)
  })
})
