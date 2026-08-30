import { describe, expect, it } from 'vitest'
import {
  bucketEnd,
  fillBuckets,
  formatBucket,
  formatBucketRange,
  nextBucket,
  parseBucket,
  toBucket,
} from '@/domain/buckets'

/**
 * The time buckets the stats endpoint hands over.
 *
 * A bucket is a date on a calendar rather than an instant, and the endpoint only
 * answers with the ones something happened in. A chart needs the ones in between
 * as well, or its axis is not time at all.
 */

const point = (bucket: string, amount = 0) => ({ bucket, amount })

describe('time buckets', () => {
  it('reads a bucket as a calendar date, not as an instant', () => {
    const date = parseBucket('2026-01-01')

    // Parsed as midnight UTC and read west of it, this was the last of December.
    expect(date.getFullYear()).toBe(2026)
    expect(date.getMonth()).toBe(0)
    expect(date.getDate()).toBe(1)
  })

  it('writes a date back the way it came', () => {
    expect(toBucket(parseBucket('2026-09-04'))).toBe('2026-09-04')
  })

  it('steps a day, a week and a month along', () => {
    expect(nextBucket('2026-01-31', 'day')).toBe('2026-02-01')
    expect(nextBucket('2026-05-11', 'week')).toBe('2026-05-18')
    expect(nextBucket('2026-01-01', 'month')).toBe('2026-02-01')
    // Across a year, which is where naive arithmetic gives up.
    expect(nextBucket('2026-12-01', 'month')).toBe('2027-01-01')
  })

  it('knows the last day a bucket covers', () => {
    expect(toBucket(bucketEnd('2026-05-11', 'week'))).toBe('2026-05-17')
    expect(toBucket(bucketEnd('2026-02-01', 'month'))).toBe('2026-02-28')
    expect(toBucket(bucketEnd('2026-05-11', 'day'))).toBe('2026-05-11')
  })

  describe('what a bar is called', () => {
    it('names a month and nothing else', () => {
      // The year is the same for every bar beside it, and a chart of twelve
      // "Sep 26"s reads as a table.
      expect(formatBucket('2026-09-01', 'month')).toBe('September')
      expect(formatBucket('2026-10-01', 'month')).toBe('October')
    })

    it('dates a day', () => {
      expect(formatBucket('2026-05-16', 'day')).toMatch(/16/)
      expect(formatBucket('2026-05-16', 'day')).toMatch(/May/)
    })

    it('spells out what a week covers, when asked about one', () => {
      // A bar labelled by its Monday says nothing about where it ends.
      const range = formatBucketRange('2026-05-11', 'week')

      expect(range).toMatch(/11/)
      expect(range).toMatch(/17/)
      expect(range).toContain(' - ')
    })

    it('leaves a day and a month as they are', () => {
      expect(formatBucketRange('2026-09-01', 'month')).toBe('September')
      expect(formatBucketRange('2026-05-16', 'day')).toBe(formatBucket('2026-05-16', 'day'))
    })
  })

  describe('filling the gaps', () => {
    it('puts back the days nothing happened on', () => {
      const filled = fillBuckets(
        [point('2026-01-01', 10), point('2026-01-04', 20)],
        'day',
        (bucket) => point(bucket),
      )

      expect(filled.map((p) => p.bucket)).toEqual([
        '2026-01-01',
        '2026-01-02',
        '2026-01-03',
        '2026-01-04',
      ])
      expect(filled.map((p) => p.amount)).toEqual([10, 0, 0, 20])
    })

    it('fills weeks by the week and months by the month', () => {
      expect(
        fillBuckets([point('2026-05-11'), point('2026-06-01')], 'week', (b) => point(b)).map(
          (p) => p.bucket,
        ),
      ).toEqual(['2026-05-11', '2026-05-18', '2026-05-25', '2026-06-01'])

      expect(
        fillBuckets([point('2026-11-01'), point('2027-01-01')], 'month', (b) => point(b)).map(
          (p) => p.bucket,
        ),
      ).toEqual(['2026-11-01', '2026-12-01', '2027-01-01'])
    })

    it('leaves a single bucket alone, having nothing to span', () => {
      expect(fillBuckets([point('2026-01-01', 10)], 'day', (b) => point(b))).toHaveLength(1)
      expect(fillBuckets([], 'day', (b) => point(b))).toEqual([])
    })

    it('keeps what it was given rather than drawing a year of hairlines', () => {
      // A decade of days is three and a half thousand bars, which is not a chart.
      const sparse = [point('2016-01-01', 10), point('2026-01-01', 20)]

      expect(fillBuckets(sparse, 'day', (b) => point(b))).toEqual(sparse)
    })

    it('keeps what it was given rather than losing a bucket off its grid', () => {
      // A week bucket that is not a Monday would fall between the steps, and a
      // wrong chart is worse than a gappy one.
      const odd = [point('2026-05-11', 10), point('2026-05-13', 5), point('2026-05-25', 20)]

      expect(fillBuckets(odd, 'week', (b) => point(b))).toEqual(odd)
    })
  })
})
