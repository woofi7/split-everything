/**
 * The time buckets the stats endpoint hands over, and what to call them.
 *
 * A bucket is a date on a calendar rather than an instant: the day itself, the
 * Monday of its week, or the first of its month. Read as midnight UTC and then
 * rendered anywhere west of that, the first of a month becomes the last of the
 * month before, so every one of these builds its date from the parts of the string
 * and never parses it as a time.
 *
 * The endpoint answers with the buckets it has, which are the ones something
 * happened in. A chart wants the ones in between as well, or its axis is not time
 * at all: two bars side by side could be a day apart or a month.
 */

export type Granularity = 'day' | 'week' | 'month'

/**
 * How many buckets are worth drawing.
 *
 * A decade of days is three and a half thousand bars, which is not a chart and is
 * a lot of DOM on a phone. Past this the gaps are left out rather than the chart
 * abandoned: a crowded axis beats a frozen screen.
 */
const MAX_BUCKETS = 400

export function parseBucket(bucket: string): Date {
  const [year, month, day] = bucket.split('T')[0].split('-').map(Number)

  return new Date(year, (month ?? 1) - 1, day ?? 1)
}

export function toBucket(date: Date): string {
  const pad = (part: number) => String(part).padStart(2, '0')

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** The bucket after this one, a day, a week or a month along. */
export function nextBucket(bucket: string, granularity: Granularity): string {
  const date = parseBucket(bucket)

  if (granularity === 'month') date.setMonth(date.getMonth() + 1)
  else date.setDate(date.getDate() + (granularity === 'week' ? 7 : 1))

  return toBucket(date)
}

/** The last day this bucket covers: itself, its Sunday, or the end of its month. */
export function bucketEnd(bucket: string, granularity: Granularity): Date {
  const date = parseBucket(nextBucket(bucket, granularity))
  date.setDate(date.getDate() - 1)

  return date
}

/**
 * What to write under a bar.
 *
 * A month is named and nothing else, because the year is the same for every bar
 * beside it and a chart of twelve "Sep 26"s reads as a table. A day and a week are
 * both dated by their first day.
 */
export function formatBucket(bucket: string, granularity: Granularity): string {
  const date = parseBucket(bucket)

  if (granularity === 'month') return date.toLocaleDateString(undefined, { month: 'long' })

  return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
}

/**
 * What the bucket covers, spelled out for whoever asked about it.
 *
 * A week is a stretch of time rather than a date, and a bar labelled by its Monday
 * says nothing about where it ends. A day and a month already say it.
 */
export function formatBucketRange(bucket: string, granularity: Granularity): string {
  if (granularity !== 'week') return formatBucket(bucket, granularity)

  const from = formatBucket(bucket, 'day')
  const to = bucketEnd(bucket, 'week').toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
  })

  return `${from} - ${to}`
}

/**
 * The buckets between the first and the last, so the axis is time rather than a
 * list of the days something happened on.
 *
 * The answer falls back to what it was given rather than dropping anything: a
 * bucket off the grid this walks (a week that is not a Monday, say) would be lost,
 * and a wrong chart is worse than a gappy one.
 */
export function fillBuckets<T extends { bucket: string }>(
  points: readonly T[],
  granularity: Granularity,
  empty: (bucket: string) => T,
): T[] {
  if (points.length < 2) return [...points]

  const known = new Map(points.map((point) => [point.bucket, point]))
  const last = points[points.length - 1].bucket
  const filled: T[] = []

  let cursor = points[0].bucket
  while (cursor <= last) {
    if (filled.length >= MAX_BUCKETS) return [...points]

    filled.push(known.get(cursor) ?? empty(cursor))
    cursor = nextBucket(cursor, granularity)
  }

  // Everything it was given has to still be in there.
  const kept = new Set(filled.map((point) => point.bucket))
  if (points.some((point) => !kept.has(point.bucket))) return [...points]

  return filled
}
