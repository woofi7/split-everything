import { describe, expect, it } from 'vitest'
import { fuzzyMatch, fuzzySearch } from '@/domain/fuzzySearch'

describe('fuzzy match', () => {
  it('matches an exact string', () => {
    expect(fuzzyMatch('house', 'house')).not.toBeNull()
  })

  it('matches a prefix', () => {
    expect(fuzzyMatch('hou', 'house')).not.toBeNull()
  })

  it('matches characters spread through the target', () => {
    // The point of fuzzy matching: initials and abbreviations find things.
    expect(fuzzyMatch('hs', 'house')).not.toBeNull()
    expect(fuzzyMatch('bt', 'bus ticket')).not.toBeNull()
  })

  it('is case insensitive both ways', () => {
    expect(fuzzyMatch('HOUSE', 'house')).not.toBeNull()
    expect(fuzzyMatch('house', 'HOUSE')).not.toBeNull()
  })

  it('refuses a string whose characters are not all present', () => {
    expect(fuzzyMatch('xyz', 'house')).toBeNull()
  })

  it('refuses characters that are present but out of order', () => {
    // Subsequence matching, deliberately: order carries meaning when someone
    // types an abbreviation.
    expect(fuzzyMatch('esuoh', 'house')).toBeNull()
  })

  it('matches everything on an empty query', () => {
    expect(fuzzyMatch('', 'house')).not.toBeNull()
  })

  it('refuses a query longer than the target', () => {
    expect(fuzzyMatch('household', 'house')).toBeNull()
  })

  it('reports which characters matched, so they can be highlighted', () => {
    const match = fuzzyMatch('hs', 'house')

    expect(match?.indices).toEqual([0, 3])
  })

  it('reports contiguous indices for a prefix', () => {
    expect(fuzzyMatch('hou', 'house')?.indices).toEqual([0, 1, 2])
  })

  it('ignores whitespace in the query', () => {
    // People type spaces when they pause; they should not break the match.
    expect(fuzzyMatch('h s', 'house')?.indices).toEqual([0, 3])
  })
})

describe('fuzzy scoring', () => {
  const score = (query: string, target: string) => fuzzyMatch(query, target)?.score ?? -Infinity

  it('scores an exact match above a prefix match', () => {
    expect(score('house', 'house')).toBeGreaterThan(score('house', 'household'))
  })

  it('scores a prefix above a match starting mid-word', () => {
    expect(score('house', 'house')).toBeGreaterThan(score('house', 'my house'))
  })

  it('scores consecutive characters above scattered ones', () => {
    expect(score('hou', 'house')).toBeGreaterThan(score('hoe', 'house'))
  })

  it('rewards matching at a word boundary', () => {
    // "bt" should prefer "bus ticket" over "abbot", where the letters are buried.
    expect(score('bt', 'bus ticket')).toBeGreaterThan(score('bt', 'abbot'))
  })

  it('prefers a shorter target when the match is otherwise equal', () => {
    expect(score('car', 'car')).toBeGreaterThan(score('car', 'car rental agency'))
  })

  it('gives an empty query the same score for every target', () => {
    expect(score('', 'house')).toBe(score('', 'car'))
  })
})

describe('fuzzy search over a list', () => {
  const items = [
    { name: 'house', keywords: ['home', 'building'] },
    { name: 'hotel', keywords: ['travel', 'stay'] },
    { name: 'car', keywords: ['vehicle', 'drive'] },
    { name: 'bus ticket', keywords: ['transport'] },
    { name: 'shopping cart', keywords: ['groceries', 'store'] },
  ]

  const search = (query: string) =>
    fuzzySearch(query, items, (item) => [item.name, ...item.keywords]).map((r) => r.item.name)

  it('returns everything for an empty query, in the original order', () => {
    expect(search('')).toEqual(['house', 'hotel', 'car', 'bus ticket', 'shopping cart'])
  })

  it('returns only what matches', () => {
    expect(search('car')).toEqual(expect.arrayContaining(['car', 'shopping cart']))
    expect(search('car')).not.toContain('hotel')
  })

  it('puts the best match first', () => {
    expect(search('car')[0]).toBe('car')
  })

  it('matches on a keyword as well as the name', () => {
    // Nobody looking for a house types "house" if they think of it as home.
    expect(search('home')).toContain('house')
  })

  it('scores a name match above a keyword match', () => {
    const results = fuzzySearch('hotel', items, (item) => [item.name, ...item.keywords])

    expect(results[0].item.name).toBe('hotel')
  })

  it('returns nothing when nothing matches', () => {
    expect(search('zzzz')).toEqual([])
  })

  it('reports the matched indices of the field that matched', () => {
    const results = fuzzySearch('hs', items, (item) => [item.name])

    expect(results[0].indices).toEqual([0, 3])
  })

  it('is deterministic for equally scoring items', () => {
    const first = search('t')
    const second = search('t')

    expect(second).toEqual(first)
  })

  it('handles an empty list', () => {
    expect(fuzzySearch('car', [], () => [])).toEqual([])
  })

  it('skips an item with no searchable text', () => {
    const results = fuzzySearch('car', [{ name: '' }], () => [])

    expect(results).toEqual([])
  })

  it('caps the results when asked', () => {
    expect(fuzzySearch('', items, (item) => [item.name], 2)).toHaveLength(2)
  })
})
