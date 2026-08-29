import { describe, expect, it } from 'vitest'
import {
  FALLBACK_ICON,
  ICONS,
  ICON_GROUPS,
  findIcon,
  iconSearchFields,
  resolveIcon,
} from '@/domain/icons'
import { fuzzySearch } from '@/domain/fuzzySearch'

describe('the icon catalogue', () => {
  it('is not empty', () => {
    expect(ICONS.length).toBeGreaterThan(100)
  })

  it('has no duplicate names, since the name is what gets stored', () => {
    const names = ICONS.map((icon) => icon.name)

    expect(new Set(names).size).toBe(names.length)
  })

  it('gives every icon a label and at least one keyword', () => {
    for (const icon of ICONS) {
      expect(icon.label.length, icon.name).toBeGreaterThan(0)
      expect(icon.keywords.length, icon.name).toBeGreaterThan(0)
    }
  })

  it('gives every icon a real Font Awesome definition', () => {
    for (const icon of ICONS) {
      expect(icon.definition, icon.name).toBeDefined()
      expect(icon.definition.iconName, icon.name).toBe(icon.name)
      // A definition carries the path data the renderer needs.
      expect(icon.definition.icon.length, icon.name).toBeGreaterThan(4)
    }
  })

  it('puts every icon in a declared group', () => {
    for (const icon of ICONS) {
      expect(ICON_GROUPS, icon.name).toContain(icon.group)
    }
  })

  it('leaves no group empty', () => {
    for (const group of ICON_GROUPS) {
      expect(ICONS.some((icon) => icon.group === group), group).toBe(true)
    }
  })

  it('keeps every stored name short enough for the column', () => {
    // The database column is 48 characters; a longer name would be truncated.
    for (const icon of ICONS) {
      expect(icon.name.length, icon.name).toBeLessThanOrEqual(48)
    }
  })

  it('stores plain ASCII names', () => {
    for (const icon of ICONS) {
      expect(/^[a-z0-9-]+$/.test(icon.name), icon.name).toBe(true)
    }
  })
})

describe('resolving a stored icon', () => {
  it('finds an icon by name', () => {
    expect(findIcon('house')?.label).toBe('House')
  })

  it('returns nothing for a name it does not know', () => {
    expect(findIcon('not-a-real-icon')).toBeNull()
  })

  it.each([null, undefined, ''])('returns nothing for %s', (name) => {
    expect(findIcon(name)).toBeNull()
  })

  it('falls back rather than throwing for an unknown name', () => {
    // A name written by a newer version of the app must not break a group list.
    expect(resolveIcon('removed-in-a-later-release')).toBe(FALLBACK_ICON)
  })

  it('falls back for a group that has no icon yet', () => {
    expect(resolveIcon(null)).toBe(FALLBACK_ICON)
  })

  it('resolves a known name to itself', () => {
    expect(resolveIcon('house').name).toBe('house')
  })

  it('has a fallback that is itself in the catalogue', () => {
    expect(ICONS).toContain(FALLBACK_ICON)
  })
})

describe('searching the catalogue', () => {
  const search = (query: string) =>
    fuzzySearch(query, ICONS, iconSearchFields).map((result) => result.item.name)

  it('finds an icon by its label', () => {
    expect(search('house')[0]).toBe('house')
  })

  it('finds an icon by a keyword rather than its name', () => {
    // The whole reason keywords exist: nobody searches "bolt" for a power bill.
    expect(search('hydro')).toContain('bolt')
    expect(search('electricity')).toContain('bolt')
  })

  it.each([
    ['rent', 'house'],
    ['groceries', 'cart-shopping'],
    ['uber', 'taxi'],
    ['petrol', 'gas-pump'],
    ['flight', 'plane'],
    ['wifi', 'wifi'],
    ['gym', 'dumbbell'],
    ['pharmacy', 'pills'],
    ['etransfer', 'money-bill-transfer'],
    ['flatmates', 'user-group'],
    ['netflix', 'tv'],
    ['dentist', 'tooth'],
  ])('finds something sensible for %s', (query, expected) => {
    expect(search(query)).toContain(expected)
  })

  it('finds an icon from an abbreviation', () => {
    expect(search('crd')).toContain('credit-card')
  })

  it('returns everything for an empty query', () => {
    expect(search('')).toHaveLength(ICONS.length)
  })

  it('keeps the catalogue order for an empty query, so the grid does not jump', () => {
    expect(search('')).toEqual(ICONS.map((icon) => icon.name))
  })

  it('returns nothing for a query that matches nothing', () => {
    expect(search('qqqqzzzz')).toEqual([])
  })

  it('is case insensitive', () => {
    expect(search('HOUSE')[0]).toBe('house')
  })

  it('reports which field matched, so the right text can be highlighted', () => {
    const results = fuzzySearch('hydro', ICONS, iconSearchFields)
    const bolt = results.find((result) => result.item.name === 'bolt')

    // Matched a keyword, not the label, so the label must not be highlighted.
    expect(bolt!.fieldIndex).toBeGreaterThan(0)
  })

  it('lists the searchable fields with the label first and the name last', () => {
    const fields = iconSearchFields(ICONS[0])

    expect(fields[0]).toBe(ICONS[0].label)
    expect(fields[fields.length - 1]).toBe(ICONS[0].name)
  })
})
