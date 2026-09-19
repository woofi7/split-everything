import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeVapidKey, registerForPush } from '@/native/push'
import type { ApiClient } from '@/api/client'

describe('VAPID key decoding', () => {
  it('decodes a base64url key to raw bytes', () => {
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

describe('registering for notifications', () => {
  const REAL_KEY =
    'BDLIpARp5poJEsnhCHwluND9bDbYwZX2nMc3rKpQbPAjRDnLFQUFKyr3av2mffIbsNoWZc0D7UL6kQjxBwcIwTw'

  const api = (publicKey: string) => ({
    get: vi.fn(async () => ({ publicKey })),
    post: vi.fn(async () => ({})),
    delete: vi.fn(async () => ({})),
  })

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

    expect(await registerForPush(client as unknown as ApiClient, 'device')).toBe('unconfigured')
    expect(client.post).not.toHaveBeenCalled()
  })

  it('says denied only when permission was actually refused', async () => {
    browserThat('denied')

    expect(await registerForPush(api('BKey') as unknown as ApiClient, 'device')).toBe('denied')
  })

  it('subscribes and registers the device when everything is in place', async () => {
    browserThat('granted')
    const client = api(REAL_KEY)

    expect(await registerForPush(client as unknown as ApiClient, 'device-7')).toBe('on')
    expect(client.post).toHaveBeenCalledWith('/notifications', {
      channel: 'WebPush',
      endpoint: 'https://push.example/abc',
      p256dh: 'key',
      auth: 'auth',
      deviceId: 'device-7',
    })
  })

  it('refuses a key that is not one, rather than failing inside atob', async () => {
    browserThat('granted')

    expect(
      await registerForPush(api('mailto:someone@example.com') as unknown as ApiClient, 'device'),
    ).toBe('unconfigured')
  })

  it('refuses a key of the wrong length, which decodes cleanly and still is not one', async () => {
    browserThat('granted')

    expect(await registerForPush(api('aGVsbG8') as unknown as ApiClient, 'device')).toBe(
      'unconfigured',
    )
  })

  it('says unsupported when the browser has no push at all', async () => {
    expect(await registerForPush(api('BKey') as unknown as ApiClient, 'device')).toBe('unsupported')
  })
})
