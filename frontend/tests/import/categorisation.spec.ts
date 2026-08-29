import { describe, expect, it } from 'vitest'
import { categoriseRow, learnFromCorrection, rankRules } from '@/import/categorisation'
import type { CategoryRule } from '@/import/categorisation'

const rule = (
  keyword: string,
  categoryId: string,
  overrides: Partial<CategoryRule> = {},
): CategoryRule => ({
  id: `rule-${keyword}`,
  keyword,
  categoryId,
  categoryKey: categoryId,
  suggestedGroupId: null,
  weight: 1,
  hitCount: 0,
  isEnabled: true,
  isBuiltIn: true,
  ...overrides,
})

describe('auto-categorisation', () => {
  const rules = [
    rule('UBER EATS', 'dining'),
    rule('UBER', 'transport'),
    rule('METRO', 'groceries'),
    rule('NETFLIX', 'subscriptions'),
  ]

  it('matches a merchant to its category', () => {
    expect(categoriseRow('UBER EATS 8829 TORONTO ON', rules)?.categoryKey).toBe('dining')
  })

  it('prefers the longest matching keyword', () => {
    // "UBER" also matches, but "UBER EATS" is the more specific merchant.
    expect(categoriseRow('UBER EATS TORONTO', rules)?.categoryKey).toBe('dining')
  })

  it('still matches the shorter keyword when the longer one does not apply', () => {
    expect(categoriseRow('UBER TRIP TORONTO', rules)?.categoryKey).toBe('transport')
  })

  it('is case insensitive', () => {
    expect(categoriseRow('netflix.com', rules)?.categoryKey).toBe('subscriptions')
  })

  it('returns nothing for a merchant it does not know', () => {
    expect(categoriseRow('SOMEWHERE BRAND NEW', rules)).toBeNull()
  })

  it('ignores a rule the user disabled', () => {
    const disabled = [rule('METRO', 'groceries', { isEnabled: false })]

    expect(categoriseRow('METRO PLUS MARCHE', disabled)).toBeNull()
  })

  it('prefers a rule the user corrected over a built-in one of the same length', () => {
    const mixed = [
      rule('METRO', 'transport', { isBuiltIn: true, id: 'builtin' }),
      rule('METRO', 'groceries', { isBuiltIn: false, weight: 5, id: 'learned' }),
    ]

    expect(categoriseRow('METRO PLUS', mixed)?.categoryKey).toBe('groceries')
  })

  it('carries the suggested group when a rule has one', () => {
    const withGroup = [rule('HYDRO', 'utilities', { suggestedGroupId: 'group-1' })]

    expect(categoriseRow('HYDRO QUEBEC', withGroup)?.suggestedGroupId).toBe('group-1')
  })

  it.each(['', '   '])('returns nothing for a blank description (%s)', (description) => {
    expect(categoriseRow(description, rules)).toBeNull()
  })

  it('handles an empty ruleset', () => {
    expect(categoriseRow('METRO', [])).toBeNull()
  })
})

describe('learning from a correction', () => {
  it('creates a rule from the merchant the user re-categorised', () => {
    const learned = learnFromCorrection('POISSONNERIE LA MER MONTREAL', 'groceries', [])

    // Only the merchant key is kept, never the full statement line.
    expect(learned.keyword).toBe('POISSONNERIE LA')
    expect(learned.categoryKey).toBe('groceries')
    expect(learned.isBuiltIn).toBe(false)
  })

  it('outweighs the built-in rule it corrects', () => {
    const builtIn = rule('METRO', 'transport')
    const learned = learnFromCorrection('METRO PLUS MARCHE', 'groceries', [builtIn])

    expect(categoriseRow('METRO PLUS MARCHE', [builtIn, learned])?.categoryKey).toBe('groceries')
  })

  it('strengthens an existing learned rule rather than adding another', () => {
    const first = learnFromCorrection('METRO PLUS', 'groceries', [])
    const second = learnFromCorrection('METRO PLUS', 'groceries', [first])

    expect(second.id).toBe(first.id)
    expect(second.hitCount).toBe(first.hitCount + 1)
    expect(second.weight).toBeGreaterThan(first.weight)
  })

  it('re-points a learned rule when the user changes their mind', () => {
    const first = learnFromCorrection('METRO PLUS', 'groceries', [])
    const second = learnFromCorrection('METRO PLUS', 'dining', [first])

    expect(second.id).toBe(first.id)
    expect(second.categoryKey).toBe('dining')
  })

  it('refuses to learn from a description with no usable merchant', () => {
    expect(() => learnFromCorrection('   ', 'groceries', [])).toThrow()
  })
})

describe('rule ranking', () => {
  it('puts the most specific and most trusted rules first', () => {
    const ranked = rankRules([
      rule('A', 'one', { weight: 1 }),
      rule('LONGER KEYWORD', 'two', { weight: 1 }),
      rule('A', 'three', { weight: 9, isBuiltIn: false }),
    ])

    expect(ranked[0].categoryKey).toBe('two')
  })

  it('drops disabled rules', () => {
    const ranked = rankRules([
      rule('A', 'one'),
      rule('B', 'two', { isEnabled: false }),
    ])

    expect(ranked).toHaveLength(1)
  })
})
