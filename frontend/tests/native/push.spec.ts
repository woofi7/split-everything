import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeVapidKey, registerForPush } from '@/native/push'
import type { ApiClient } from '@/api/client'

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

/**
 * What turning notifications on reports back.
 *
 * This existed as a boolean, and the profile screen turned every false into
 * "Notifications were not allowed." A server deployed without its VAPID pair then
 * told the phone it had refused permission it had actually granted, which sent
 * people into their browser's site settings to fix something that was not there.
 */
describe('registering for notifications', () => {
  const api = (publicKey: string) => ({
    get: vi.fn(async () => ({ publicKey })),
    post: vi.fn(async () => ({})),
    delete: vi.fn(async () => ({})),
  })

  /** A browser that can do push, with the permission answer under test. */
  function browserThat(permission: NotificationPermission): void {
    vi.stubGlobal('Notification', {
      permission,
      requestPermission: vi.fn(async () => permission),
    })

    vi.stubGlobal('PushManager', class {})

    const subscription = {
      endpoint: 'https://push.example/abc',
      toJSON: () => ({ keys: { p256dh: 'key', auth: 'auth' } }),
    }

    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: {
        ready: Promise.resolve({ pushManager: { subscribe: vi.fn(async () => subscription) } }),
      },
    })
  }

  afterEach(() => {
    vi.unstubAllGlobals()
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  it('says the server is unconfigured when it hands back no key', async () => {
    browserThat('granted')
    const client = api('')

    // The exact production symptom: permission granted, nothing to subscribe to.
    expect(await registerForPush(client as unknown as ApiClient, 'device')).toBe('unconfigured')
    expect(client.post).not.toHaveBeenCalled()
  })

  it('says denied only when permission was actually refused', async () => {
    browserThat('denied')

    expect(await registerForPush(api('BKey') as unknown as ApiClient, 'device')).toBe('denied')
  })

  it('subscribes and registers the device when everything is in place', async () => {
    browserThat('granted')
    const client = api('aGVsbG8')

    expect(await registerForPush(client as unknown as ApiClient, 'device-7')).toBe('on')
    expect(client.post).toHaveBeenCalledWith('/notifications', {
      channel: 'WebPush',
      endpoint: 'https://push.example/abc',
      p256dh: 'key',
      auth: 'auth',
      deviceId: 'device-7',
    })
  })

  it('says unsupported when the browser has no push at all', async () => {
    // No PushManager and no service worker: an old browser, not a refusal.
    expect(await registerForPush(api('BKey') as unknown as ApiClient, 'device')).toBe('unsupported')
  })
})
