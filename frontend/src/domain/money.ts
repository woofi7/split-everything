import { intlLocale } from '@/i18n'
/**
 * Money handling shared by every screen.
 *
 * Amounts are plain numbers rather than a decimal library: the app never
 * multiplies or divides money outside `splitting.ts`, and that module works in
 * integer minor units precisely so floating point never accumulates. Keeping the
 * value a number keeps it JSON-serialisable for the offline outbox without a
 * custom codec.
 */

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

/**
 * The precision a share is worked out and stored at, two decimals finer than the
 * currency people pay in.
 *
 * A share is not a payment. Half of 66.13 is 33.065, and forcing that to a cent
 * hands somebody the extra half-cent every time - always the same somebody, since
 * the tie has to break deterministically for two devices to agree. Over four
 * hundred expenses in one real group that came to 71 cents of drift.
 *
 * Currencies with no sub-unit keep whole units: a third of a yen is not a share of
 * anything. The server does exactly the same, in CurrencyPrecision.
 */
export function shareDecimals(currency?: string | null): number {
  const decimals = currencyDecimals(currency)
  return decimals === 0 ? 0 : Math.min(4, decimals + 2)
}

/** Rounds half to even at share precision, matching the server. */
export function roundShare(amount: number, currency?: string | null): number {
  return roundTo(amount, shareDecimals(currency))
}

/**
 * Rounds half to even, matching the server. Half-up here would make a
 * client-computed split disagree with the same split recomputed on the server,
 * and the difference would surface as a phantom cent of debt.
 */
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

/**
 * Money, in the language the app is being read in.
 *
 * The locale decides where the symbol goes and what separates the thousands, and
 * French puts the symbol after the number. It defaults to the app's own setting
 * rather than to the browser's, because the app's is the one the person chose, and
 * a caller can still name one for a test.
 */
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
    // An unknown code still has to render something usable rather than throwing
    // in the middle of a list.
    return `${amount.toFixed(decimals)} ${currency}`
  }
}

/** Same amount without the symbol, for inputs and CSV previews. */
export function formatAmount(amount: number, currency: string): string {
  return amount.toFixed(currencyDecimals(currency))
}

/**
 * Reads an amount a person typed or a statement printed.
 *
 * Deliberately permissive: a French keyboard produces "12,34", a bank prints
 * "1 234,56", and accounting exports write negatives as "(42.00)" or "42.00-".
 * Returns null rather than NaN so callers must handle the failure.
 */
export function parseAmountInput(input: string | null | undefined): number | null {
  if (!input) return null

  const trimmed = input.trim()
  if (!trimmed) return null

  const negative =
    trimmed.startsWith('-') || trimmed.endsWith('-') || /^\(.*\)$/.test(trimmed)

  const digitsOnly = trimmed.replace(/[^\d.,]/g, '')
  if (!/\d/.test(digitsOnly)) return null

  // Whichever separator appears last is the decimal point; the other groups.
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
