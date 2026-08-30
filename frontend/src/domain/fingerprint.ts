/**
 * Duplicate-detection fingerprint, mirroring the server implementation exactly.
 *
 * The statement importer never uploads the statement, so the only way to ask
 * "do I already have this transaction?" is to send a fingerprint. That makes this
 * a wire contract: the payload string, the normalisation and the truncation all
 * have to match SplitEverything.Domain.Algorithms.ExpenseFingerprint character
 * for character, and tests/domain/fingerprint.spec.ts pins the resulting hashes.
 */

import { sha256Hex } from '@/domain/sha256'

/** Leading tokens of the description that make up the merchant key. */
export const MERCHANT_TOKEN_COUNT = 2

export function normalizeMerchant(description: string | null | undefined): string {
  if (!description || !description.trim()) return ''

  let upper = description.toUpperCase()
  upper = upper.replace(/[^A-Z0-9 ]/g, ' ')
  // Strip the store number, terminal id and reference a card statement appends.
  upper = upper.replace(/\b\d{3,}\b/g, ' ')
  upper = upper.replace(/\s+/g, ' ').trim()

  return upper.split(' ').filter(Boolean).slice(0, MERCHANT_TOKEN_COUNT).join(' ')
}

export async function computeFingerprint(
  date: Date,
  amount: number,
  currency: string,
  description: string,
): Promise<string> {
  const payload = [
    isoDate(date),
    Math.abs(amount).toFixed(2),
    currency.toUpperCase(),
    normalizeMerchant(description),
  ].join('|')

  return (await digestHex(payload)).slice(0, 32)
}

/**
 * SHA-256 of a string, from the platform where it exists.
 *
 * crypto.subtle is secure-context only, so it is absent when the app is served
 * over plain HTTP on a LAN address, which is how it gets used on a phone. The
 * fallback runs the same algorithm rather than a different one, because these
 * hashes are compared with the server's.
 */
async function digestHex(payload: string): Promise<string> {
  if (!crypto.subtle) return sha256Hex(payload)

  const bytes = new TextEncoder().encode(payload)
  const digest = await crypto.subtle.digest('SHA-256', bytes)

  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')
}

/** UTC calendar date, so the same purchase fingerprints alike in any timezone. */
function isoDate(date: Date): string {
  const year = date.getUTCFullYear().toString().padStart(4, '0')
  const month = (date.getUTCMonth() + 1).toString().padStart(2, '0')
  const day = date.getUTCDate().toString().padStart(2, '0')
  return `${year}-${month}-${day}`
}
