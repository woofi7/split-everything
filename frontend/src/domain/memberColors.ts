
export const MEMBER_COLORS = [
  '#6366f1',
  '#f97316',
  '#14b8a6',
  '#ec4899',
  '#84cc16',
  '#8b5cf6',
  '#f59e0b',
  '#06b6d4',
  '#ef4444',
  '#22c55e',
  '#a855f7',
  '#eab308',
] as const

function hashId(id: string): number {
  let hash = 0

  for (let i = 0; i < id.length; i++) {
    hash = (hash * 31 + id.charCodeAt(i)) | 0
  }

  return Math.abs(hash)
}

export function memberColor(memberId: string): string {
  if (!memberId) return MEMBER_COLORS[0]
  return MEMBER_COLORS[hashId(memberId) % MEMBER_COLORS.length]
}

export function memberColors(memberIds: readonly string[]): Record<string, string> {
  const taken = new Set<string>()
  const assigned: Record<string, string> = {}

  for (const id of memberIds) {
    const preferred = memberColor(id)

    if (!taken.has(preferred)) {
      assigned[id] = preferred
      taken.add(preferred)
      continue
    }

    const start = MEMBER_COLORS.indexOf(preferred as (typeof MEMBER_COLORS)[number])
    let colour = preferred

    for (let step = 1; step <= MEMBER_COLORS.length; step++) {
      const candidate = MEMBER_COLORS[(start + step) % MEMBER_COLORS.length]
      if (!taken.has(candidate)) {
        colour = candidate
        break
      }
    }

    assigned[id] = colour
    taken.add(colour)
  }

  return assigned
}

export function textOnColor(hex: string): string {
  const value = hex.replace('#', '')
  if (value.length !== 6) return '#ffffff'

  const r = parseInt(value.slice(0, 2), 16) / 255
  const g = parseInt(value.slice(2, 4), 16) / 255
  const b = parseInt(value.slice(4, 6), 16) / 255

  const luma = 0.2126 * r + 0.7152 * g + 0.0722 * b

  return luma > 0.6 ? '#0f172a' : '#ffffff'
}
