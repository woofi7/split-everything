/**
 * The accent colours the whole application can wear.
 *
 * One name stands for three shades, because that is what the app's brand tokens
 * are: a light tint for text and small marks on a dark surface, a middle, and a
 * fill for buttons. Storing a single colour would pin the other two down forever,
 * and choosing them by hand keeps each theme readable in both light and dark
 * rather than hoping a generated ramp is.
 *
 * The names are the same eight the server keeps in AppThemes, in the same order.
 * The server holds the names and refuses anything else; the shades live here,
 * because a picker that cannot draw itself until a request comes back is worse
 * than a list in two places.
 */

export interface AccentTheme {
  name: string
  /** What the picker calls it. */
  label: string
  /**
   * Light, middle, fill: brand 400, 500 and 600. The light one has to read as
   * text on a dark surface, and the fill has to hold white text on itself, in
   * both themes.
   */
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

/** What an account with no preference of its own wears. */
export const DEFAULT_ACCENT = 'indigo'

/** The theme by that name, or nothing if it is not one of ours. */
export function findAccent(name: string | null | undefined): AccentTheme | undefined {
  if (!name) return undefined

  const wanted = name.trim().toLowerCase()
  return ACCENT_THEMES.find((theme) => theme.name === wanted)
}

/**
 * The theme to wear.
 *
 * Anything unknown falls back rather than leaving the app with no accent at all:
 * an older client meeting a newer server's name, or a value hand-edited into
 * storage.
 */
export function resolveAccent(name: string | null | undefined): AccentTheme {
  return findAccent(name) ?? findAccent(DEFAULT_ACCENT)!
}

/**
 * The theme as the variables the stylesheet reads.
 *
 * Set on the root element rather than swapped by a class, so every utility built
 * from these tokens follows without anything having to know which theme is on.
 */
export function accentVariables(theme: AccentTheme): Record<string, string> {
  const [light, middle, fill] = theme.shades

  return {
    '--color-brand-400': light,
    '--color-brand-500': middle,
    '--color-brand-600': fill,
  }
}
