/**
 * The app in English and in French.
 *
 * The English string is the key: `t('Settle up')` reads as what it renders, so a
 * template stays legible and a missing translation degrades to English rather than
 * to a dotted identifier nobody can place. There is no English dictionary to keep
 * in step for the same reason.
 *
 * Reactive, because the language is an account setting and changing it has to
 * redraw what is on screen rather than wait for a reload.
 */
import { computed, ref } from 'vue'
import { fr } from './fr'

export type Locale = 'en' | 'fr'

export const LOCALES: readonly { tag: Locale; label: string }[] = [
  // Each named in its own language, which is how somebody looking for it reads.
  { tag: 'en', label: 'English' },
  { tag: 'fr', label: 'Francais' },
]

const dictionaries: Record<Locale, Record<string, string>> = { en: {}, fr }

const current = ref<Locale>('en')

export const locale = computed(() => current.value)

/** The tag Intl wants: the app's two languages as they are spoken here. */
export const intlLocale = computed(() => (current.value === 'fr' ? 'fr-CA' : 'en-CA'))

export function setLocale(next: string | null | undefined): void {
  current.value = resolveLocale(next)
}

/** The language a tag asks for, or English. Mirrors AppLocales on the server. */
export function resolveLocale(tag: string | null | undefined): Locale {
  const language = (tag ?? '').trim().toLowerCase().split(/[-_]/)[0]

  return language === 'fr' ? 'fr' : 'en'
}

/**
 * The string to show.
 *
 * Values are substituted by name: `t('{count} people', { count: 3 })`. A key with
 * no translation returns itself, which is the English text, so a screen is never
 * blank because somebody forgot a line.
 */
export function t(key: string, values?: Record<string, string | number>): string {
  const translated = dictionaries[current.value][key] ?? key

  if (!values) return translated

  return translated.replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? String(values[name]) : whole,
  )
}
