import { describe, expect, it } from 'vitest'
import {
  categoriseByHistory,
  categoriseByKeywords,
  categoryFor,
  fold,
  guessCategory,
  type Category,
} from '@/domain/categories'

const category = (key: string, keywords: string[], sortOrder = 0): Category => ({
  key,
  name: key,
  iconName: 'tag',
  colorHex: '#64748b',
  sortOrder,
  keywords,
})

const list: Category[] = [
  category('groceries', ['metro', 'iga', 'epicerie'], 10),
  category('transport', ['uber', 'essence'], 20),
  category('dining', ['uber eats', 'resto'], 30),
  category('other', [], 999),
]

describe('filing an expense by what it is called', () => {
  it('finds the category a word points at', () => {
    expect(categoriseByKeywords('Metro plus', list)).toBe('groceries')
    expect(categoriseByKeywords('Essence Petro', list)).toBe('transport')
  })

  it('lets the longest keyword win', () => {
    expect(categoriseByKeywords('UBER EATS Montreal', list)).toBe('dining')
  })

  it('reads a name the way it was typed, accents or not', () => {
    expect(categoriseByKeywords('Épicerie du coin', list)).toBe('groceries')
    expect(categoriseByKeywords('EPICERIE DU COIN', list)).toBe('groceries')
  })

  it('files nothing it has no word for', () => {
    expect(categoriseByKeywords('Cadeau pour Emma', list)).toBeNull()
    expect(categoriseByKeywords('   ', list)).toBeNull()
    expect(categoriseByKeywords('Metro', [])).toBeNull()
  })

  it('breaks a tie on the order the group put its list in', () => {
    const tied: Category[] = [
      category('second', ['abc'], 20),
      category('first', ['abc'], 10),
    ]

    expect(categoriseByKeywords('ABC store', tied)).toBe('first')
  })

  it('names the category a key belongs to, and admits when it cannot', () => {
    expect(categoryFor('groceries', list)?.key).toBe('groceries')

    expect(categoryFor('ski', list)).toBeNull()
    expect(categoryFor(null, list)).toBeNull()
  })

  it('folds a name the way the matching does', () => {
    expect(fold('Dépanneur')).toBe('depanneur')
    expect(fold('ÉPICERIE')).toBe('epicerie')
  })
})

describe('filing an expense by the words it shares with a keyword', () => {
  const named = (key: string, name: string, keywords: string[]): Category => ({
    ...category(key, keywords),
    name,
  })

  const defaults: Category[] = [
    named('groceries', 'Groceries', ['grocery', 'epicerie']),
    named('dining', 'Dining out', ['bar ', 'resto']),
    named('gifts', 'Gifts', ['cadeau']),
  ]

  it('reads a plural the way it reads the singular', () => {
    expect(categoriseByKeywords('Groceries', defaults)).toBe('groceries')
    expect(categoriseByKeywords('grocery run', defaults)).toBe('groceries')
    expect(categoriseByKeywords('Épiceries du coin', defaults)).toBe('groceries')
  })

  it('files an expense called after the category itself', () => {
    expect(categoriseByKeywords('Gift for Emma', defaults)).toBe('gifts')
    expect(categoriseByKeywords('Dining out with Léa', defaults)).toBe('dining')
  })

  it('matches a category name only as a whole word', () => {
    expect(categoriseByKeywords('Giftshop', defaults)).toBeNull()
  })
})

describe('filing an expense the way the same one was filed before', () => {
  const cats: Category[] = [
    category('groceries', ['metro'], 10),
    category('fun', [], 20),
    category('other', [], 999),
  ]

  const filed = (description: string, categoryKey: string | null, extra = {}) => ({
    groupId: 'group-1',
    description,
    categoryKey,
    spentAt: '2026-09-01T12:00:00Z',
    isDeleted: false,
    ...extra,
  })

  it('reuses the category the same description got last time', () => {
    const history = [filed('Climbing gym', 'fun')]

    expect(categoriseByHistory('climbing  GYM', 'group-1', history, cats)).toBe('fun')
    expect(guessCategory('Climbing gym', 'group-1', history, cats)).toBe('fun')
  })

  it('lets what people chose win over a keyword', () => {
    const history = [filed('Metro tickets', 'other')]

    expect(guessCategory('Metro tickets', 'group-1', history, cats)).toBe('other')
    expect(guessCategory('Metro', 'group-1', history, cats)).toBe('groceries')
  })

  it('prefers this group, then the most recent choice', () => {
    const history = [
      filed('Bowling', 'other', { groupId: 'group-2', spentAt: '2026-09-20T12:00:00Z' }),
      filed('Bowling', 'fun', { spentAt: '2026-08-01T12:00:00Z' }),
      filed('Bowling', 'groceries', { spentAt: '2026-07-01T12:00:00Z' }),
    ]

    expect(categoriseByHistory('Bowling', 'group-1', history, cats)).toBe('fun')
  })

  it('ignores deleted expenses and categories the group no longer has', () => {
    const history = [
      filed('Bowling', 'fun', { isDeleted: true }),
      filed('Bowling', 'sports'),
      filed('Bowling', null),
    ]

    expect(categoriseByHistory('Bowling', 'group-1', history, cats)).toBeNull()
  })
})
