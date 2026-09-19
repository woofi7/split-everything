
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

export function categoriseByKeywords(
  description: string,
  categories: readonly Category[],
): string | null {
  const haystack = fold(description ?? '')
  if (!haystack.trim()) return null

  let best: { key: string; length: number; sortOrder: number } | null = null

  for (const category of categories) {
    for (const keyword of category.keywords ?? []) {
      const needle = fold(keyword).trim()
      if (!needle || !haystack.includes(needle)) continue

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
