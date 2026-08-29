import { describe, expect, it } from 'vitest'
import { decodeVapidKey } from '@/native/push'

describe('VAPID key decoding', () => {
  it('decodes a base64url key to raw bytes', () => {
    // "hello" as base64url, with the padding the browser omits.
    const bytes = decodeVapidKey('aGVsbG8')

    expect(new TextDecoder().decode(bytes)).toBe('hello')
  })

  it('handles the url-safe alphabet', () => {
    const standard = btoa('\xfb\xff\xfe')
    const urlSafe = standard.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')

    expect([...decodeVapidKey(urlSafe)]).toEqual([251, 255, 254])
  })

  it('produces a Uint8Array, which is what PushManager requires', () => {
    expect(decodeVapidKey('aGVsbG8')).toBeInstanceOf(Uint8Array)
  })

  it('handles a key needing no padding', () => {
    expect(decodeVapidKey(btoa('abcd').replace(/=+$/, '')).length).toBe(4)
  })
})
