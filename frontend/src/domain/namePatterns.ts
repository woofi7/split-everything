/**
 * Matching an expense name against a pattern somebody typed.
 *
 * Globs rather than regular expressions. "Loyer*" is what a person writes when they
 * mean "anything starting with Loyer", and a regex reads it as "Loye followed by any
 * number of r" - which matches "Loye" and misses every rent there has ever been. A
 * pattern nobody can predict is worse than one that cannot express much.
 *
 * The rules, which fit in a sentence: a pattern with no star matches a name that
 * contains it, and a star stands for any run of characters. So "Loyer" catches
 * "Paiement loyer aout", "Loyer*" catches names beginning with it, and "*aout"
 * catches names ending that way. Case is ignored throughout, because "Loyer" and
 * "loyer" are the same rent.
 */

/** Turns one pattern into a test. Anything can be a pattern, so this never throws. */
export function compileNamePattern(pattern: string): (name: string) => boolean {
  const trimmed = pattern.trim()
  if (!trimmed) return () => false

  const lowered = trimmed.toLowerCase()

  // No star: the plain, forgiving reading of a typed name.
  if (!lowered.includes('*')) {
    return (name) => name.toLowerCase().includes(lowered)
  }

  // With a star the pattern describes the whole name, or "Loyer*" would mean the
  // same as "Loyer" and the star would be decoration.
  const expression = new RegExp(
    `^${lowered.split('*').map(escapeRegExp).join('.*')}$`,
    'i',
  )

  return (name) => expression.test(name.trim())
}

/** Whether any of the patterns matches the name. */
export function matchesAnyNamePattern(name: string, patterns: readonly string[]): boolean {
  return patterns.some((pattern) => compileNamePattern(pattern)(name))
}

/** Every character a regex would read as punctuation, made literal. */
function escapeRegExp(literal: string): string {
  return literal.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}
