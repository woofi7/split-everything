
export type SwipeDirection = 'left' | 'right'

export interface TouchTravel {
  dx: number
  dy: number
  elapsedMs: number
}

const MIN_DISTANCE_PX = 60

const OFF_AXIS_RATIO = 1.5

const MAX_DURATION_MS = 1000

export function readSwipe(travel: TouchTravel): SwipeDirection | null {
  const across = Math.abs(travel.dx)

  if (across < MIN_DISTANCE_PX) return null
  if (across < Math.abs(travel.dy) * OFF_AXIS_RATIO) return null
  if (travel.elapsedMs > MAX_DURATION_MS) return null

  return travel.dx < 0 ? 'left' : 'right'
}
