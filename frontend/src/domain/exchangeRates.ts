import type { ApiClient } from '@/api/client'

const KEY = 'split-everything.exchange-rates'

export interface ExchangeRate {
  rate: number
  isEstimate: boolean
}

type Cache = Record<string, Record<string, number>>

function read(): Cache {
  try {
    const parsed = JSON.parse(localStorage.getItem(KEY) ?? '{}') as unknown
    return parsed && typeof parsed === 'object' ? (parsed as Cache) : {}
  } catch {
    return {}
  }
}

function remember(pair: string, date: string, rate: number): void {
  const cache = read()
  cache[pair] = { ...cache[pair], [date]: rate }

  try {
    localStorage.setItem(KEY, JSON.stringify(cache))
  } catch {
  }
}

function closestKnown(byDate: Record<string, number> | undefined, date: string): number | null {
  const dates = Object.keys(byDate ?? {}).sort()
  if (dates.length === 0) return null

  const onOrBefore = dates.filter((known) => known <= date).pop()
  return byDate![onOrBefore ?? dates[0]]
}

export async function exchangeRate(
  api: ApiClient | null,
  from: string,
  to: string,
  date: string,
): Promise<ExchangeRate | null> {
  if (from === to) return { rate: 1, isEstimate: false }

  const pair = `${from}-${to}`
  const known = read()[pair]?.[date]
  if (known) return { rate: known, isEstimate: false }

  if (api) {
    try {
      const { rate } = await api.get<{ rate: number }>('/currency/rate', { from, to, date })
      if (rate > 0) {
        remember(pair, date, rate)
        return { rate, isEstimate: false }
      }
    } catch {
    }
  }

  const fallback = closestKnown(read()[pair], date)
  return fallback ? { rate: fallback, isEstimate: true } : null
}
