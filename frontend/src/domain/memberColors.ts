/**
 * A colour per person, derived rather than stored.
 *
 * Derived so every device agrees without a round trip and without a column: the
 * same member id gives the same colour on a phone and a laptop, offline, and in
 * a group nobody has opened yet. Storing it would mean a migration, a default for
 * every existing member, and two devices disagreeing until the next sync.
 *
 * The palette is picked for a dark surface first and checked against the light
 * one, and the hues are spread so two people in the same group are unlikely to
 * land on neighbours.
 */

/**
 * The same twelve the server keeps in MemberPalette, in the same order.
 *
 * Duplicated rather than fetched: a colour picker that cannot draw itself until a
 * request comes back is worse than a list in two places, and the server refuses
 * anything outside its own copy, so a drift shows up as a refusal rather than as
 * a wrong colour.
 */
export const MEMBER_COLORS = [
  '#6366f1', // indigo
  '#f97316', // orange
  '#14b8a6', // teal
  '#ec4899', // pink
  '#84cc16', // lime
  '#8b5cf6', // violet
  '#f59e0b', // amber
  '#06b6d4', // cyan
  '#ef4444', // red
  '#22c55e', // green
  '#a855f7', // purple
  '#eab308', // yellow
] as const

/**
 * Stable hash of an id. Not a security hash: it only has to spread ids across the
 * palette and give the same answer everywhere, which rules out anything that
 * depends on insertion order or a random seed.
 */
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

/**
 * Colours for a group, nudged so no two members share one.
 *
 * The derived colour is kept wherever it is free, so a person's colour does not
 * change when someone else joins. A clash walks to the next unused entry, which
 * only moves the later member.
 */
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

/**
 * Readable text on a member's colour. Luminance rather than a lookup, so it stays
 * right if the palette changes.
 */
export function textOnColor(hex: string): string {
  const value = hex.replace('#', '')
  if (value.length !== 6) return '#ffffff'

  const r = parseInt(value.slice(0, 2), 16) / 255
  const g = parseInt(value.slice(2, 4), 16) / 255
  const b = parseInt(value.slice(4, 6), 16) / 255

  // Rec. 709 luma, which is close enough for a yes-or-no decision.
  const luma = 0.2126 * r + 0.7152 * g + 0.0722 * b

  return luma > 0.6 ? '#0f172a' : '#ffffff'
}
