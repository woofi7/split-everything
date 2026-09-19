
export interface FuzzyMatch {
  score: number
  indices: number[]
}

export interface FuzzyResult<T> {
  item: T
  score: number
  indices: number[]
  fieldIndex: number
}

const SCORE_START = 40
const SCORE_WORD_BOUNDARY = 20
const SCORE_CONSECUTIVE = 15
const SCORE_MATCH = 4
const PENALTY_GAP = 2
const PENALTY_LENGTH = 0.4
const SCORE_EXACT = 60

export function fuzzyMatch(query: string, target: string): FuzzyMatch | null {
  const needle = query.replace(/\s+/g, '').toLowerCase()
  const haystack = target.toLowerCase()

  if (needle.length === 0) {
    return { score: 0, indices: [] }
  }
  if (needle.length > haystack.length) return null

  const indices: number[] = []
  let score = 0
  let cursor = 0
  let previousIndex = -1

  for (const character of needle) {
    const found = haystack.indexOf(character, cursor)
    if (found === -1) return null

    indices.push(found)

    if (found === 0) {
      score += SCORE_START
    } else if (isWordBoundary(haystack, found)) {
      score += SCORE_WORD_BOUNDARY
    }

    if (found === previousIndex + 1) {
      score += SCORE_CONSECUTIVE
    } else if (previousIndex >= 0) {
      score -= Math.min(found - previousIndex - 1, 6) * PENALTY_GAP
    }

    score += SCORE_MATCH
    previousIndex = found
    cursor = found + 1
  }

  if (haystack === needle) score += SCORE_EXACT

  score -= haystack.length * PENALTY_LENGTH

  return { score, indices }
}

function isWordBoundary(text: string, index: number): boolean {
  const previous = text[index - 1]
  return previous === ' ' || previous === '-' || previous === '_' || previous === '/'
}

export function fuzzySearch<T>(
  query: string,
  items: readonly T[],
  fields: (item: T) => readonly string[],
  limit?: number,
): FuzzyResult<T>[] {
  const results: Array<FuzzyResult<T> & { order: number }> = []

  for (let order = 0; order < items.length; order++) {
    const item = items[order]
    const candidates = fields(item)
    if (candidates.length === 0) continue

    let bestScore = -Infinity
    let bestIndices: number[] = []
    let bestField = -1

    for (let fieldIndex = 0; fieldIndex < candidates.length; fieldIndex++) {
      const candidate = candidates[fieldIndex]
      if (!candidate) continue

      const match = fuzzyMatch(query, candidate)
      if (!match) continue

      const adjusted = match.score - fieldIndex * 8

      if (adjusted > bestScore) {
        bestScore = adjusted
        bestIndices = match.indices
        bestField = fieldIndex
      }
    }

    if (bestField >= 0) {
      results.push({ item, score: bestScore, indices: bestIndices, fieldIndex: bestField, order })
    }
  }

  results.sort((left, right) =>
    right.score === left.score ? left.order - right.order : right.score - left.score,
  )

  const trimmed = limit === undefined ? results : results.slice(0, limit)
  return trimmed.map(({ item, score, indices, fieldIndex }) => ({ item, score, indices, fieldIndex }))
}
