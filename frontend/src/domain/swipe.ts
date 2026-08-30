/**
 * Reading a sideways swipe out of where a finger started and where it stopped.
 *
 * Kept apart from the DOM so the thresholds can be argued with in a test rather
 * than on a phone. All three of them exist to tell a swipe from the two gestures
 * it sits between: a tap, which barely moves, and a scroll, which moves a long
 * way but down the screen rather than across it.
 */

export type SwipeDirection = 'left' | 'right'

export interface TouchTravel {
  /** Across the screen, positive to the right. */
  dx: number
  /** Down the screen, positive downwards. */
  dy: number
  elapsedMs: number
}

/**
 * How far across a finger has to travel to mean it.
 *
 * Comfortably past the slop a browser allows a tap, so pressing a card and
 * lifting is never read as a swipe.
 */
const MIN_DISTANCE_PX = 60

/**
 * How much further across than down.
 *
 * A scroll that drifts sideways is still a scroll, and changing group under
 * someone reading a list is the worst thing this could do. Anything shallower
 * than about 34 degrees off the vertical is left to the scroller.
 */
const OFF_AXIS_RATIO = 1.5

/**
 * How long the whole thing may take.
 *
 * A finger resting on the screen for a while and then drifting off is not a
 * swipe. Generous, because a deliberate swipe across a large phone is not quick.
 */
const MAX_DURATION_MS = 1000

/** The direction swiped, or nothing if that was not a swipe. */
export function readSwipe(travel: TouchTravel): SwipeDirection | null {
  const across = Math.abs(travel.dx)

  if (across < MIN_DISTANCE_PX) return null
  if (across < Math.abs(travel.dy) * OFF_AXIS_RATIO) return null
  if (travel.elapsedMs > MAX_DURATION_MS) return null

  return travel.dx < 0 ? 'left' : 'right'
}
