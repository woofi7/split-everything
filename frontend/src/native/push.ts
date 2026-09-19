import { Capacitor } from '@capacitor/core'
import type { ApiClient } from '@/api/client'

export type PushChannel = 'WebPush' | 'Apns' | 'Fcm'

export type PushState = 'unsupported' | 'insecure' | 'denied' | 'off' | 'on'

export type PushOutcome = 'on' | 'denied' | 'unsupported' | 'unconfigured' | 'failed'

export async function pushState(): Promise<PushState> {
  if (Capacitor.isNativePlatform()) {
    const { PushNotifications } = await import('@capacitor/push-notifications')
    const status = await PushNotifications.checkPermissions()

    if (status.receive === 'denied') return 'denied'
    return status.receive === 'granted' ? 'on' : 'off'
  }

  if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
    return window.isSecureContext ? 'unsupported' : 'insecure'
  }

  if (typeof Notification === 'undefined') return 'unsupported'
  if (Notification.permission === 'denied') return 'denied'

  const registration = await navigator.serviceWorker.getRegistration()
  const subscription = await registration?.pushManager.getSubscription()

  return subscription ? 'on' : 'off'
}

export async function registerForPush(api: ApiClient, deviceId: string): Promise<PushOutcome> {
  return Capacitor.isNativePlatform()
    ? registerNative(api, deviceId)
    : registerWebPush(api, deviceId)
}

async function registerNative(api: ApiClient, deviceId: string): Promise<PushOutcome> {
  const { PushNotifications } = await import('@capacitor/push-notifications')

  const status = await PushNotifications.checkPermissions()
  const granted =
    status.receive === 'granted'
      ? true
      : (await PushNotifications.requestPermissions()).receive === 'granted'

  if (!granted) return 'denied'

  const channel: PushChannel = Capacitor.getPlatform() === 'ios' ? 'Apns' : 'Fcm'

  return new Promise<PushOutcome>((resolve) => {
    void PushNotifications.addListener('registration', async (token) => {
      try {
        await api.post('/notifications', {
          channel,
          endpoint: token.value,
          p256dh: null,
          auth: null,
          deviceId,
        })
        resolve('on')
      } catch {
        resolve('failed')
      }
    })

    void PushNotifications.addListener('registrationError', () => resolve('failed'))
    void PushNotifications.register()
  })
}

async function registerWebPush(api: ApiClient, deviceId: string): Promise<PushOutcome> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return 'unsupported'

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') return 'denied'

  const registration = await navigator.serviceWorker.ready

  const { publicKey } = await api.get<{ publicKey: string }>('/notifications/vapid-key')
  if (!publicKey) return 'unconfigured'

  let applicationServerKey: Uint8Array<ArrayBuffer>
  try {
    applicationServerKey = decodeVapidKey(publicKey)
  } catch {
    return 'unconfigured'
  }

  if (applicationServerKey.length !== 65) return 'unconfigured'

  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey,
  })

  const json = subscription.toJSON()

  await api.post('/notifications', {
    channel: 'WebPush' satisfies PushChannel,
    endpoint: subscription.endpoint,
    p256dh: json.keys?.p256dh ?? null,
    auth: json.keys?.auth ?? null,
    deviceId,
  })

  return 'on'
}

export function decodeVapidKey(base64Url: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64Url.length % 4)) % 4)
  const base64 = (base64Url + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(base64)

  const bytes = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes
}

export async function unregisterPush(api: ApiClient): Promise<void> {
  if (Capacitor.isNativePlatform()) {
    const { PushNotifications } = await import('@capacitor/push-notifications')
    await PushNotifications.removeAllListeners()
    return
  }

  if (!('serviceWorker' in navigator)) return

  const registration = await navigator.serviceWorker.ready
  const subscription = await registration.pushManager.getSubscription()
  if (!subscription) return

  await api.delete('/notifications', { endpoint: subscription.endpoint })
  await subscription.unsubscribe()
}
