import { intlLocale } from '@/i18n'

const DECIMAL_OVERRIDES: Record<string, number> = {
  BIF: 0, CLP: 0, DJF: 0, GNF: 0, ISK: 0, JPY: 0, KMF: 0, KRW: 0,
  PYG: 0, RWF: 0, UGX: 0, UYI: 0, VND: 0, VUV: 0, XAF: 0, XOF: 0, XPF: 0,
  BHD: 3, IQD: 3, JOD: 3, KWD: 3, LYD: 3, OMR: 3, TND: 3,
}

export const DEFAULT_DECIMALS = 2

export function currencyDecimals(currency?: string | null): number {
  if (!currency) return DEFAULT_DECIMALS
  const override = DECIMAL_OVERRIDES[currency.toUpperCase()]
  return override ?? DEFAULT_DECIMALS
}

export function minorUnit(currency?: string | null): number {
  const decimals = currencyDecimals(currency)
  return Number((10 ** -decimals).toFixed(decimals))
}

export function shareDecimals(currency?: string | null): number {
  const decimals = currencyDecimals(currency)
  return decimals === 0 ? 0 : Math.min(4, decimals + 2)
}

export function roundShare(amount: number, currency?: string | null): number {
  return roundTo(amount, shareDecimals(currency))
}

export function roundMoney(amount: number, currency?: string | null): number {
  return roundTo(amount, currencyDecimals(currency))
}

function roundTo(amount: number, decimals: number): number {
  const factor = 10 ** decimals
  const scaled = amount * factor
  const floor = Math.floor(scaled)
  const fraction = scaled - floor

  let rounded: number
  const epsilon = 1e-9
  if (Math.abs(fraction - 0.5) < epsilon) {
    rounded = floor % 2 === 0 ? floor : floor + 1
  } else {
    rounded = Math.round(scaled)
  }

  return Number((rounded / factor).toFixed(decimals))
}

export function formatMoney(
  amount: number,
  currency: string,
  locale = intlLocale.value,
): string {
  const decimals = currencyDecimals(currency)

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    }).format(amount)
  } catch {
    return `${amount.toFixed(decimals)} ${currency}`
  }
}

export function formatAmount(amount: number, currency: string): string {
  return amount.toFixed(currencyDecimals(currency))
}

export function parseAmountInput(input: string | null | undefined): number | null {
  if (!input) return null

  const trimmed = input.trim()
  if (!trimmed) return null

  const negative =
    trimmed.startsWith('-') || trimmed.endsWith('-') || /^\(.*\)$/.test(trimmed)

  const digitsOnly = trimmed.replace(/[^\d.,]/g, '')
  if (!/\d/.test(digitsOnly)) return null

  const lastDot = digitsOnly.lastIndexOf('.')
  const lastComma = digitsOnly.lastIndexOf(',')
  const decimalSeparator = lastComma > lastDot ? ',' : '.'
  const groupSeparator = decimalSeparator === ',' ? '.' : ','

  const normalized = digitsOnly
    .split(groupSeparator)
    .join('')
    .replace(decimalSeparator, '.')

  const value = Number(normalized)
  if (!Number.isFinite(value)) return null

  return negative ? -Math.abs(value) : value
}
