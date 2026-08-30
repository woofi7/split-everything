import { describe, expect, it } from 'vitest'
import {
  ACCENT_THEMES,
  DEFAULT_ACCENT,
  accentVariables,
  findAccent,
  resolveAccent,
} from '@/domain/themes'

/**
 * The accent colours the whole application can wear.
 *
 * One name stands for three shades, because that is what the brand tokens are: a
 * light tint, a middle, and a fill. The names have to match the server's list,
 * which is the authority on what may be stored.
 */

describe('the accent themes', () => {
  it('offers eight', () => {
    expect(ACCENT_THEMES).toHaveLength(8)
  })

  it('matches the names the server will accept', () => {
    // AppThemes.Names, in the same order. A name this client offers and the server
    // refuses is a colour that cannot be saved.
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
    // The tokens in main.css: an account with no preference must look exactly as
    // it did before there were themes at all.
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
    // An older client meeting a newer server's name, or a value hand-edited into
    // storage: better the default than every button losing its colour.
    expect(resolveAccent('chartreuse').name).toBe('indigo')
    expect(resolveAccent(undefined).name).toBe('indigo')
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
