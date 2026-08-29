import { normalizeMerchant } from '@/domain/fingerprint'

export interface CategoryRule {
  id: string
  keyword: string
  categoryId: string
  categoryKey: string
  suggestedGroupId: string | null
  weight: number
  hitCount: number
  isEnabled: boolean
  isBuiltIn: boolean
}

/**
 * Local merchant-to-category matching for the statement importer.
 *
 * Runs on the device against the user's own ruleset, which starts from a built-in
 * list and improves from their corrections. Only the merchant keyword and the
 * category it maps to are ever stored - never the statement line - so the
 * ruleset can sync as ordinary preference data without carrying statement
 * content off the device.
 */
export function categoriseRow(
  description: string,
  rules: CategoryRule[],
): CategoryRule | null {
  if (!description || !description.trim()) return null

  const haystack = description.toUpperCase()

  for (const rule of rankRules(rules)) {
    if (haystack.includes(rule.keyword.toUpperCase())) return rule
  }

  return null
}

/**
 * Most specific first, then most trusted.
 *
 * Keyword length is the specificity signal: "UBER EATS" must beat "UBER", or every
 * food delivery would be filed as transport. Among equally specific rules, the
 * ones the user corrected outrank the built-in guesses.
 */
export function rankRules(rules: CategoryRule[]): CategoryRule[] {
  return rules
    .filter((rule) => rule.isEnabled)
    .slice()
    .sort((left, right) => {
      const byLength = right.keyword.length - left.keyword.length
      if (byLength !== 0) return byLength

      const byLearned = Number(left.isBuiltIn) - Number(right.isBuiltIn)
      if (byLearned !== 0) return byLearned

      const byWeight = right.weight - left.weight
      if (byWeight !== 0) return byWeight

      return left.keyword < right.keyword ? -1 : 1
    })
}

/**
 * Turns a correction into a rule, or strengthens the one that already covers this
 * merchant. Learned rules carry extra weight so they win against the built-in
 * guess they are correcting.
 */
export function learnFromCorrection(
  description: string,
  categoryKey: string,
  existing: CategoryRule[],
  categoryId = categoryKey,
): CategoryRule {
  const keyword = normalizeMerchant(description)
  if (!keyword) {
    throw new Error('That description has no merchant name to learn from.')
  }

  const previous = existing.find(
    (rule) => !rule.isBuiltIn && rule.keyword.toUpperCase() === keyword,
  )

  if (previous) {
    return {
      ...previous,
      categoryKey,
      categoryId,
      hitCount: previous.hitCount + 1,
      weight: previous.weight + 1,
      isEnabled: true,
    }
  }

  return {
    id: crypto.randomUUID(),
    keyword,
    categoryId,
    categoryKey,
    suggestedGroupId: null,
    // Starts above the built-in weight of 1, so a single correction is enough to
    // override the shipped guess.
    weight: 10,
    hitCount: 1,
    isEnabled: true,
    isBuiltIn: false,
  }
}
