import { computed, ref } from 'vue'
import { fr } from './fr'

export type Locale = 'en' | 'fr'

export const LOCALES: readonly { tag: Locale; label: string }[] = [
  { tag: 'en', label: 'English' },
  { tag: 'fr', label: 'Francais' },
]

const dictionaries: Record<Locale, Record<string, string>> = { en: {}, fr }

const current = ref<Locale>('en')

export const locale = computed(() => current.value)

export const intlLocale = computed(() => (current.value === 'fr' ? 'fr-CA' : 'en-CA'))

export function setLocale(next: string | null | undefined): void {
  current.value = resolveLocale(next)
}

export function resolveLocale(tag: string | null | undefined): Locale {
  const language = (tag ?? '').trim().toLowerCase().split(/[-_]/)[0]

  return language === 'fr' ? 'fr' : 'en'
}

export function t(key: string, values?: Record<string, string | number>): string {
  const translated = dictionaries[current.value][key] ?? key

  if (!values) return translated

  return translated.replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? String(values[name]) : whole,
  )
}
