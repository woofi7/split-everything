
import { sha256Hex } from '@/domain/sha256'

export const MERCHANT_TOKEN_COUNT = 2

export function normalizeMerchant(description: string | null | undefined): string {
  if (!description || !description.trim()) return ''

  let upper = description.toUpperCase()
  upper = upper.replace(/[^A-Z0-9 ]/g, ' ')
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

async function digestHex(payload: string): Promise<string> {
  if (!crypto.subtle) return sha256Hex(payload)

  const bytes = new TextEncoder().encode(payload)
  const digest = await crypto.subtle.digest('SHA-256', bytes)

  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')
}

function isoDate(date: Date): string {
  const year = date.getUTCFullYear().toString().padStart(4, '0')
  const month = (date.getUTCMonth() + 1).toString().padStart(2, '0')
  const day = date.getUTCDate().toString().padStart(2, '0')
  return `${year}-${month}-${day}`
}
