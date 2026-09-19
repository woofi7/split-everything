
export interface AccentTheme {
  name: string
  label: string
  shades: readonly [string, string, string]
}

export const ACCENT_THEMES: readonly AccentTheme[] = [
  { name: 'indigo', label: 'Indigo', shades: ['#818cf8', '#6366f1', '#4f46e5'] },
  { name: 'violet', label: 'Violet', shades: ['#c4b5fd', '#a78bfa', '#7c3aed'] },
  { name: 'sky', label: 'Sky', shades: ['#7dd3fc', '#38bdf8', '#0284c7'] },
  { name: 'teal', label: 'Teal', shades: ['#5eead4', '#2dd4bf', '#0d9488'] },
  { name: 'green', label: 'Green', shades: ['#86efac', '#4ade80', '#16a34a'] },
  { name: 'amber', label: 'Amber', shades: ['#fcd34d', '#fbbf24', '#d97706'] },
  { name: 'rose', label: 'Rose', shades: ['#fda4af', '#fb7185', '#e11d48'] },
  { name: 'slate', label: 'Slate', shades: ['#cbd5e1', '#94a3b8', '#475569'] },
]

export const DEFAULT_ACCENT = 'indigo'

export function findAccent(name: string | null | undefined): AccentTheme | undefined {
  if (!name) return undefined

  const wanted = name.trim().toLowerCase()
  return ACCENT_THEMES.find((theme) => theme.name === wanted)
}

export function resolveAccent(name: string | null | undefined): AccentTheme {
  return findAccent(name) ?? findAccent(DEFAULT_ACCENT)!
}

export function groupColor(
  group: { themeName?: string | null; colorHex?: string | null } | null | undefined,
): string {
  const theme = findAccent(group?.themeName)
  if (theme) return theme.shades[2]

  return group?.colorHex || resolveAccent(DEFAULT_ACCENT).shades[2]
}

export function accentVariables(theme: AccentTheme): Record<string, string> {
  const [light, middle, fill] = theme.shades

  return {
    '--color-brand-400': light,
    '--color-brand-500': middle,
    '--color-brand-600': fill,
  }
}
