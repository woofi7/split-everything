import { afterEach, describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { LOCALES, intlLocale, locale, resolveLocale, setLocale, t } from '@/i18n'
import { fr } from '@/i18n/fr'

/**
 * The app in English and in French.
 *
 * The English string is the key, so a missing translation degrades to English
 * rather than to a dotted identifier. The last test in here is the one that
 * matters most: it reads the source and fails when a string has been added to a
 * screen and not to the dictionary, which is the only way that stays true.
 */

afterEach(() => setLocale('en'))

describe('the app languages', () => {
  it('offers English and French, each named in itself', () => {
    expect(LOCALES.map((choice) => choice.tag)).toEqual(['en', 'fr'])
    expect(LOCALES.map((choice) => choice.label)).toEqual(['English', 'Francais'])
  })

  it('starts in English', () => {
    expect(locale.value).toBe('en')
    expect(t('Settle up')).toBe('Settle up')
  })

  it('answers in French once asked', () => {
    setLocale('fr')

    expect(t('Settle up')).toBe('Regler'.replace('Regler', fr['Settle up']))
    expect(t('Settle up')).not.toBe('Settle up')
  })

  it('returns the English text for anything untranslated', () => {
    setLocale('fr')

    // Better a screen in one language than a screen with a hole in it.
    expect(t('A string nobody has translated')).toBe('A string nobody has translated')
  })

  it('substitutes values by name', () => {
    expect(t('{count} waiting to sync', { count: 3 })).toBe('3 waiting to sync')

    setLocale('fr')
    expect(t('{count} waiting to sync', { count: 3 })).toContain('3')
  })

  it('leaves a placeholder alone when nothing was given for it', () => {
    expect(t('{count} waiting to sync')).toBe('{count} waiting to sync')
  })

  it('takes a regional tag for its language', () => {
    expect(resolveLocale('fr-CA')).toBe('fr')
    expect(resolveLocale('FR')).toBe('fr')
    expect(resolveLocale('en-GB')).toBe('en')
  })

  it('falls back to English for a language it does not have', () => {
    expect(resolveLocale('de')).toBe('en')
    expect(resolveLocale(null)).toBe('en')
    expect(resolveLocale(undefined)).toBe('en')
  })

  it('names the locale Intl wants, so money and dates follow the app', () => {
    expect(intlLocale.value).toBe('en-CA')

    setLocale('fr')
    expect(intlLocale.value).toBe('fr-CA')
  })

  /**
   * Every string on a screen has to be in the dictionary.
   *
   * Read out of the source rather than listed here, because a list would be the
   * thing that goes stale. A screen added tomorrow with an untranslated string
   * fails this the moment it is written.
   */
  it('translates every string the app asks for', () => {
    const keys = new Set<string>()

    const walk = (dir: string) => {
      for (const entry of readdirSync(dir)) {
        const path = join(dir, entry)
        if (statSync(path).isDirectory()) {
          walk(path)
          continue
        }
        if (!/\.(ts|vue)$/.test(entry) || path.includes('i18n')) continue

        const source = readFileSync(path, 'utf8')
        // t('...') and t("..."), the only two ways a string reaches the dictionary.
        for (const match of source.matchAll(/\bt\(\s*'((?:[^'\\]|\\.)+)'/g)) {
          keys.add(match[1].replace(/\\'/g, "'"))
        }
        for (const match of source.matchAll(/\bt\(\s*"((?:[^"\\]|\\.)+)"/g)) {
          keys.add(match[1].replace(/\\"/g, '"'))
        }
      }
    }

    walk('src')

    const missing = [...keys].filter((key) => !(key in fr)).sort()
    expect(missing, `untranslated: ${missing.join(' | ')}`).toEqual([])
    // And the dictionary is worth its size: this catches a bad regex here too.
    expect(keys.size).toBeGreaterThan(150)
  })

  it('has no English left in the French dictionary by accident', () => {
    // A value identical to its key is either a word that is the same in both, or a
    // line somebody forgot. The ones that really are the same are named here.
    const sameInBoth = new Set([
      'Split Everything',
      'Alice',
      'Date',
      'Note',
      'Total',
      'Profile',
      'Exact',
      // "Version 0.2.3" reads the same in both, placeholder and all.
      'Version {version}',
    ])

    const suspicious = Object.entries(fr)
      .filter(([key, value]) => key === value && !sameInBoth.has(key))
      .map(([key]) => key)

    expect(suspicious).toEqual([])
  })
})
