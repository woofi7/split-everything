import { describe, expect, it } from 'vitest'
import { sha256Hex } from '@/domain/sha256'

/**
 * SHA-256 without a secure context.
 *
 * crypto.subtle exists only in a secure context, so the statement importer could
 * not fingerprint a row when the app was served over plain HTTP on a LAN address,
 * which is how it gets used on a phone. There is no room for an approximation
 * here: the hash has to be the same one the server computes, or duplicate
 * detection quietly stops agreeing with it.
 */

describe('sha256Hex', () => {
  it('hashes the empty string', () => {
    expect(sha256Hex('')).toBe(
      'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
    )
  })

  it('hashes abc', () => {
    expect(sha256Hex('abc')).toBe(
      'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
    )
  })

  it('hashes a message that crosses a block boundary', () => {
    // 56 bytes: the length has to spill into a second block.
    expect(sha256Hex('a'.repeat(56))).toBe(
      'b35439a4ac6f0948b6d6f9e3c6af0f5f590ce20f1bde7090ef7970686ec6738a',
    )
  })

  it('hashes a long message', () => {
    expect(sha256Hex('a'.repeat(1000))).toBe(
      '41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3',
    )
  })

  it('hashes text outside ASCII the same way a byte-oriented hash must', () => {
    // Encoded as UTF-8 first, which is what the server does with the same string.
    // Escaped so this file stays plain ASCII.
    const accented = 'Airbnb M\u00e1laga'

    expect(sha256Hex(accented)).toHaveLength(64)
    expect(sha256Hex(accented)).not.toBe(sha256Hex('Airbnb Malaga'))
  })

  it('agrees with the platform implementation', async () => {
    // The real test: whatever the fallback does, it has to match what a secure
    // context would have produced for the same input.
    const samples = [
      '',
      'a',
      'hello world',
      '2026-01-05|42.50|CAD|metro',
      'Flight Montr\u00e9al to Madrid',
      'x'.repeat(63),
      'x'.repeat(64),
      'x'.repeat(65),
      'x'.repeat(119),
      'x'.repeat(120),
      'y'.repeat(255),
    ]

    for (const sample of samples) {
      const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(sample))
      const expected = [...new Uint8Array(digest)]
        .map((byte) => byte.toString(16).padStart(2, '0'))
        .join('')

      expect(sha256Hex(sample)).toBe(expected)
    }
  })

  it('agrees with the platform implementation on random input', async () => {
    for (let i = 0; i < 40; i++) {
      const length = Math.floor(Math.random() * 200)
      const bytes = new Uint8Array(length)
      crypto.getRandomValues(bytes)
      const sample = [...bytes].map((b) => String.fromCharCode(32 + (b % 90))).join('')

      const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(sample))
      const expected = [...new Uint8Array(digest)]
        .map((byte) => byte.toString(16).padStart(2, '0'))
        .join('')

      expect(sha256Hex(sample)).toBe(expected)
    }
  })
})
