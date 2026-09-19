
export function compileNamePattern(pattern: string): (name: string) => boolean {
  const trimmed = pattern.trim()
  if (!trimmed) return () => false

  const lowered = trimmed.toLowerCase()

  if (!lowered.includes('*')) {
    return (name) => name.toLowerCase().includes(lowered)
  }

  const expression = new RegExp(
    `^${lowered.split('*').map(escapeRegExp).join('.*')}$`,
    'i',
  )

  return (name) => expression.test(name.trim())
}

export function matchesAnyNamePattern(name: string, patterns: readonly string[]): boolean {
  return patterns.some((pattern) => compileNamePattern(pattern)(name))
}

function escapeRegExp(literal: string): string {
  return literal.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}
