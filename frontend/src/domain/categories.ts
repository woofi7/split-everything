
export interface Category {
  key: string
  name: string
  iconName: string
  colorHex: string
  sortOrder: number
  keywords: string[]
}

export function categoryFor(
  key: string | null | undefined,
  categories: readonly Category[],
): Category | null {
  if (!key) return null
  return categories.find((category) => category.key === key) ?? null
}

export function fold(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
}

function stem(word: string): string {
  if (word.length > 4 && word.endsWith('ies')) return `${word.slice(0, -3)}y`
  if (word.length > 3 && word.endsWith('s') && !word.endsWith('ss')) return word.slice(0, -1)
  return word
}

function words(text: string): string {
  const stems = fold(text).split(/[^a-z0-9]+/).filter(Boolean).map(stem)
  return ` ${stems.join(' ')} `
}

export function categoriseByKeywords(
  description: string,
  categories: readonly Category[],
): string | null {
  const haystack = fold(description ?? '')
  if (!haystack.trim()) return null

  const haystackWords = words(haystack)
  let best: { key: string; length: number; sortOrder: number } | null = null

  for (const category of categories) {
    const needles = [
      ...(category.keywords ?? []).map((keyword) => ({ text: keyword, wholeWordOnly: false })),
      { text: category.name, wholeWordOnly: true },
    ]

    for (const { text, wholeWordOnly } of needles) {
      const needle = fold(text ?? '').trim()
      if (!needle) continue

      const needleWords = words(needle)
      const matches =
        (!wholeWordOnly && haystack.includes(needle)) ||
        (needleWords.trim() !== '' && haystackWords.includes(needleWords))
      if (!matches) continue

      const candidate = { key: category.key, length: needle.length, sortOrder: category.sortOrder }
      if (
        best === null ||
        candidate.length > best.length ||
        (candidate.length === best.length && candidate.sortOrder < best.sortOrder)
      ) {
        best = candidate
      }
    }
  }

  return best?.key ?? null
}

export interface FiledExpense {
  groupId: string
  description: string
  categoryKey?: string | null
  spentAt: string
  isDeleted?: boolean
}

export function categoriseByHistory(
  description: string,
  groupId: string,
  history: readonly FiledExpense[],
  categories: readonly Category[],
): string | null {
  const wanted = words(description ?? '')
  if (!wanted.trim()) return null

  const known = new Set(categories.map((category) => category.key))

  const match = history
    .filter(
      (expense) =>
        !expense.isDeleted &&
        expense.categoryKey &&
        known.has(expense.categoryKey) &&
        words(expense.description) === wanted,
    )
    .sort(
      (a, b) =>
        Number(b.groupId === groupId) - Number(a.groupId === groupId) ||
        b.spentAt.localeCompare(a.spentAt),
    )[0]

  return match?.categoryKey ?? null
}

export function guessCategory(
  description: string,
  groupId: string,
  history: readonly FiledExpense[],
  categories: readonly Category[],
): string | null {
  return (
    categoriseByHistory(description, groupId, history, categories) ??
    categoriseByKeywords(description, categories)
  )
}
