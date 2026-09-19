import { describe, expect, it } from 'vitest'
import {
  ACCENT_THEMES,
  DEFAULT_ACCENT,
  accentVariables,
  findAccent,
  groupColor,
  resolveAccent,
} from '@/domain/themes'

describe('the accent themes', () => {
  it('offers eight', () => {
    expect(ACCENT_THEMES).toHaveLength(8)
  })

  it('matches the names the server will accept', () => {
    expect(ACCENT_THEMES.map((theme) => theme.name)).toEqual([
      'indigo',
      'violet',
      'sky',
      'teal',
      'green',
      'amber',
      'rose',
      'slate',
    ])
  })

  it('names each one for a person rather than for a stylesheet', () => {
    for (const theme of ACCENT_THEMES) {
      expect(theme.label).not.toBe('')
      expect(theme.label[0]).toBe(theme.label[0].toUpperCase())
    }
  })

  it('gives each one three distinct shades', () => {
    for (const theme of ACCENT_THEMES) {
      expect(theme.shades).toHaveLength(3)
      expect(new Set(theme.shades).size).toBe(3)
      for (const shade of theme.shades) expect(shade).toMatch(/^#[0-9a-f]{6}$/)
    }
  })

  it('has no two themes wearing the same fill', () => {
    const fills = ACCENT_THEMES.map((theme) => theme.shades[2])
    expect(new Set(fills).size).toBe(fills.length)
  })

  it('defaults to the indigo the stylesheet is written in', () => {
    expect(DEFAULT_ACCENT).toBe('indigo')
    expect(resolveAccent(null).shades).toEqual(['#818cf8', '#6366f1', '#4f46e5'])
  })

  it('finds a theme by name, whatever the case', () => {
    expect(findAccent('Teal')?.name).toBe('teal')
    expect(findAccent(' rose ')?.name).toBe('rose')
  })

  it('finds nothing for a name it does not have', () => {
    expect(findAccent('chartreuse')).toBeUndefined()
    expect(findAccent('')).toBeUndefined()
    expect(findAccent(null)).toBeUndefined()
  })

  it('falls back rather than leaving the app with no accent', () => {
    expect(resolveAccent('chartreuse').name).toBe('indigo')
    expect(resolveAccent(undefined).name).toBe('indigo')
  })

  describe('the colour of a group', () => {
    it('is the accent the group wears, where it has one', () => {
      expect(groupColor({ themeName: 'teal', colorHex: '#4f46e5' })).toBe(
        findAccent('teal')!.shades[2],
      )
    })

    it('is the colour stored on the group, where it does not', () => {
      expect(groupColor({ themeName: null, colorHex: '#f97316' })).toBe('#f97316')
    })

    it('falls back to the default rather than leaving a mark with no colour', () => {
      expect(groupColor({ themeName: null, colorHex: '' })).toBe(
        findAccent(DEFAULT_ACCENT)!.shades[2],
      )
      expect(groupColor(undefined)).toBe(findAccent(DEFAULT_ACCENT)!.shades[2])
    })

    it('ignores a name this client does not have', () => {
      expect(groupColor({ themeName: 'chartreuse', colorHex: '#f97316' })).toBe('#f97316')
    })
  })

  it('states the theme as the tokens the stylesheet reads', () => {
    const teal = findAccent('teal')!

    expect(accentVariables(teal)).toEqual({
      '--color-brand-400': teal.shades[0],
      '--color-brand-500': teal.shades[1],
      '--color-brand-600': teal.shades[2],
    })
  })
})
