import { describe, expect, it } from 'vitest'
import { categoriseByKeywords, categoryFor, fold, type Category } from '@/domain/categories'

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
