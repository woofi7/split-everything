import { intlLocale } from '@/i18n'

export type Granularity = 'day' | 'week' | 'month'

const MAX_BUCKETS = 400

export function parseBucket(bucket: string): Date {
  const [year, month, day] = bucket.split('T')[0].split('-').map(Number)

  return new Date(year, (month ?? 1) - 1, day ?? 1)
}

export function toBucket(date: Date): string {
  const pad = (part: number) => String(part).padStart(2, '0')

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function bucketOf(when: string | Date, granularity: Granularity): string {
  const date = when instanceof Date ? new Date(when) : new Date(when)

  if (granularity === 'month') return toBucket(new Date(date.getFullYear(), date.getMonth(), 1))

  if (granularity === 'week') {
    const monday = new Date(date.getFullYear(), date.getMonth(), date.getDate())
    monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7))
    return toBucket(monday)
  }

  return toBucket(new Date(date.getFullYear(), date.getMonth(), date.getDate()))
}

export function nextBucket(bucket: string, granularity: Granularity): string {
  const date = parseBucket(bucket)

  if (granularity === 'month') date.setMonth(date.getMonth() + 1)
  else date.setDate(date.getDate() + (granularity === 'week' ? 7 : 1))

  return toBucket(date)
}

export function bucketEnd(bucket: string, granularity: Granularity): Date {
  const date = parseBucket(nextBucket(bucket, granularity))
  date.setDate(date.getDate() - 1)

  return date
}

export function formatBucket(bucket: string, granularity: Granularity): string {
  const date = parseBucket(bucket)

  if (granularity === 'month') return date.toLocaleDateString(intlLocale.value, { month: 'long' })

  return date.toLocaleDateString(intlLocale.value, { day: 'numeric', month: 'short' })
}

export function formatMonthHeading(bucket: string, today: Date = new Date()): string {
  const date = parseBucket(bucket)
  const thisYear = date.getFullYear() === today.getFullYear()

  return date.toLocaleDateString(
    intlLocale.value,
    thisYear ? { month: 'long' } : { month: 'long', year: 'numeric' },
  )
}

export function formatBucketRange(bucket: string, granularity: Granularity): string {
  if (granularity !== 'week') return formatBucket(bucket, granularity)

  const from = formatBucket(bucket, 'day')
  const to = bucketEnd(bucket, 'week').toLocaleDateString(intlLocale.value, {
    day: 'numeric',
    month: 'short',
  })

  return `${from} - ${to}`
}

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

  const kept = new Set(filled.map((point) => point.bucket))
  if (points.some((point) => !kept.has(point.bucket))) return [...points]

  return filled
}
