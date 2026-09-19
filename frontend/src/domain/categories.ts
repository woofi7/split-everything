/**
 * What an expense was for.
 *
 * A category is a key, a name and some words. The key is what the expense stores,
 * so renaming a category keeps everything filed under it and deleting one leaves
 * its expenses pointing at a name nobody recognises - which reads as unfiled, and
 * is a far better outcome than a delete that empties a year of expenses.
 *
 * The words are what make it worth having at all. Nobody fills in a dropdown on
 * every expense; they type "Metro" and the app should already know. So the picker
 * starts from a guess and is only ever a correction.
 */

export interface Category {
  key: string
  name: string
  iconName: string
  colorHex: string
  sortOrder: number
  /** Words that file an expense here without anybody choosing. Lower case. */
  keywords: string[]
}

/** The category a key names, or null for one the list no longer has. */
export function categoryFor(
  key: string | null | undefined,
  categories: readonly Category[],
): Category | null {
  if (!key) return null
  return categories.find((category) => category.key === key) ?? null
}

/**
 * Folded for matching: lower case, and accents removed.
 *
 * "Épicerie" has to match "epicerie" typed without the accent, and "Dépanneur"
 * has to match a bank statement that shouts DEPANNEUR in ASCII. Half this app is
 * used in French on a phone keyboard where accents cost an extra press.
 */
export function fold(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
}

/**
 * The category a description falls into, or null when nothing matches.
 *
 * Longest keyword wins, whatever order the list is in: "uber eats" has to beat
 * "uber", or every takeaway is filed as a taxi. Among keywords of the same length
 * the earlier category wins, which is the order the group arranged its list in.
 */
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
