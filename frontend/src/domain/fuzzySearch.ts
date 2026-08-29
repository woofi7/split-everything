/**
 * Subsequence fuzzy matching with scoring, as an icon picker needs.
 *
 * Deliberately subsequence-based rather than edit-distance: when someone types
 * "bt" they mean the initials of "bus ticket", and order carries that meaning.
 * Edit distance would rank "abbot" alongside it and lose the intent.
 *
 * Scoring rewards, in order of weight: a match at the very start, a match at a
 * word boundary, consecutive characters, and a shorter target. Those four are
 * what make a picker feel like it read your mind rather than filtered a list.
 */

export interface FuzzyMatch {
  score: number
  /** Positions in the target that matched, for highlighting. */
  indices: number[]
}

export interface FuzzyResult<T> {
  item: T
  score: number
  indices: number[]
  /** Index of the field that produced the match, so the caller can highlight it. */
  fieldIndex: number
}

const SCORE_START = 40
const SCORE_WORD_BOUNDARY = 20
const SCORE_CONSECUTIVE = 15
const SCORE_MATCH = 4
const PENALTY_GAP = 2
const PENALTY_LENGTH = 0.4
const SCORE_EXACT = 60

/**
 * Scores one query against one target, or null when the target does not contain
 * the query's characters in order.
 */
export function fuzzyMatch(query: string, target: string): FuzzyMatch | null {
  // Whitespace is what people type while pausing; it should not decide a match.
  const needle = query.replace(/\s+/g, '').toLowerCase()
  const haystack = target.toLowerCase()

  if (needle.length === 0) {
    // Everything matches, and equally: the caller keeps its own order.
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
      // A wide gap means the letters are incidental rather than intended.
      score -= Math.min(found - previousIndex - 1, 6) * PENALTY_GAP
    }

    score += SCORE_MATCH
    previousIndex = found
    cursor = found + 1
  }

  if (haystack === needle) score += SCORE_EXACT

  // Shorter targets win a tie: "car" beats "car rental agency" for "car".
  score -= haystack.length * PENALTY_LENGTH

  return { score, indices }
}

function isWordBoundary(text: string, index: number): boolean {
  const previous = text[index - 1]
  return previous === ' ' || previous === '-' || previous === '_' || previous === '/'
}

/**
 * Searches a list, matching each item against several fields.
 *
 * The first field is treated as the primary one (an icon's own name), so a name
 * match outranks a keyword match of the same quality. Ties break on the item's
 * original position, which keeps the grid from reshuffling as someone types.
 */
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

      // A later field is a keyword, worth less than the name it describes.
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
