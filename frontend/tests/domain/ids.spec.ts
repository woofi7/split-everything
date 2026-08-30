import { afterEach, describe, expect, it } from 'vitest'
import { newId } from '@/domain/ids'
import { computeFingerprint } from '@/domain/fingerprint'

/**
 * Identifiers have to work without a secure context.
 *
 * crypto.randomUUID exists only in a secure context, so it is missing when the
 * app is served over plain HTTP on a LAN address, which is exactly how it gets
 * tested on a phone. getDeviceId runs during startup, so reaching for it
 * unguarded meant the app never mounted and the screen stayed blank.
 */

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

const original = crypto.randomUUID

afterEach(() => {
  Object.defineProperty(crypto, 'randomUUID', { value: original, configurable: true })
})

function withoutRandomUuid(): void {
  Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true })
}

describe('newId', () => {
  it('produces a v4 uuid', () => {
    expect(newId()).toMatch(UUID_V4)
  })

  it('produces a v4 uuid with no crypto.randomUUID', () => {
    withoutRandomUuid()

    expect(newId()).toMatch(UUID_V4)
  })

  it('does not repeat itself without crypto.randomUUID', () => {
    withoutRandomUuid()

    const ids = new Set(Array.from({ length: 500 }, () => newId()))

    expect(ids.size).toBe(500)
  })

  it('uses the platform generator when there is one', () => {
    Object.defineProperty(crypto, 'randomUUID', {
      value: () => '11111111-1111-4111-8111-111111111111',
      configurable: true,
    })

    expect(newId()).toBe('11111111-1111-4111-8111-111111111111')
  })
})

describe('computeFingerprint outside a secure context', () => {
  const originalSubtle = crypto.subtle

  afterEach(() => {
    Object.defineProperty(crypto, 'subtle', { value: originalSubtle, configurable: true })
  })

  it('produces the same fingerprint the platform would have', async () => {
    const withPlatform = await computeFingerprint(
      new Date('2026-01-05T00:00:00Z'), 42.5, 'CAD', 'Metro')

    Object.defineProperty(crypto, 'subtle', { value: undefined, configurable: true })

    const withFallback = await computeFingerprint(
      new Date('2026-01-05T00:00:00Z'), 42.5, 'CAD', 'Metro')

    // It decides whether a statement row is a duplicate, and it is compared with
    // hashes the server computed, so a different answer here is worse than none.
    expect(withFallback).toBe(withPlatform)
  })
})
