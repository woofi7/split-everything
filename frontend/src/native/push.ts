import { Capacitor } from '@capacitor/core'
import type { ApiClient } from '@/api/client'

export type PushChannel = 'WebPush' | 'Apns' | 'Fcm'

/**
 * Notification registration across all three channels.
 *
 * Native APNs and FCM are the primary path, because iOS suspends a background web
 * app aggressively enough that Web Push alone is unreliable. Web Push is the
 * fallback for anyone in a plain browser. Both end up registering the same shape
 * with the API, so the server does not care which shell a device came from.
 */
/**
 * Where notifications stand on this device.
 *
 * Five answers rather than a boolean, because the reasons differ in what somebody
 * can do about them: an insecure origin needs the app served over https, a refused
 * permission needs the browser's own site settings, and "off" is a switch away.
 */
export type PushState = 'unsupported' | 'insecure' | 'denied' | 'off' | 'on'

export async function pushState(): Promise<PushState> {
  if (Capacitor.isNativePlatform()) {
    const { PushNotifications } = await import('@capacitor/push-notifications')
    const status = await PushNotifications.checkPermissions()

    if (status.receive === 'denied') return 'denied'
    return status.receive === 'granted' ? 'on' : 'off'
  }

  if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
    // A plain-HTTP origin has no service worker at all, which is the common case
    // on a local network and worth saying rather than calling unsupported.
    return window.isSecureContext ? 'unsupported' : 'insecure'
  }

  if (typeof Notification === 'undefined') return 'unsupported'
  if (Notification.permission === 'denied') return 'denied'

  const registration = await navigator.serviceWorker.getRegistration()
  const subscription = await registration?.pushManager.getSubscription()

  return subscription ? 'on' : 'off'
}

export async function registerForPush(api: ApiClient, deviceId: string): Promise<boolean> {
  return Capacitor.isNativePlatform()
    ? registerNative(api, deviceId)
    : registerWebPush(api, deviceId)
}

async function registerNative(api: ApiClient, deviceId: string): Promise<boolean> {
  const { PushNotifications } = await import('@capacitor/push-notifications')

  const status = await PushNotifications.checkPermissions()
  const granted =
    status.receive === 'granted'
      ? true
      : (await PushNotifications.requestPermissions()).receive === 'granted'

  if (!granted) return false

  const channel: PushChannel = Capacitor.getPlatform() === 'ios' ? 'Apns' : 'Fcm'

  return new Promise<boolean>((resolve) => {
    // The token arrives asynchronously from the OS, so registration completes in
    // the listener rather than after the register() call.
    void PushNotifications.addListener('registration', async (token) => {
      try {
        await api.post('/notifications', {
          channel,
          endpoint: token.value,
          p256dh: null,
          auth: null,
          deviceId,
        })
        resolve(true)
      } catch {
        resolve(false)
      }
    })

    void PushNotifications.addListener('registrationError', () => resolve(false))
    void PushNotifications.register()
  })
}

async function registerWebPush(api: ApiClient, deviceId: string): Promise<boolean> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return false

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') return false

  const registration = await navigator.serviceWorker.ready

  const { publicKey } = await api.get<{ publicKey: string }>('/notifications/vapid-key')
  if (!publicKey) return false

  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: decodeVapidKey(publicKey),
  })

  const json = subscription.toJSON()

  await api.post('/notifications', {
    channel: 'WebPush' satisfies PushChannel,
    endpoint: subscription.endpoint,
    p256dh: json.keys?.p256dh ?? null,
    auth: json.keys?.auth ?? null,
    deviceId,
  })

  return true
}

/**
 * VAPID keys are base64url; PushManager wants raw bytes.
 *
 * Backed by an explicit ArrayBuffer because PushManager's BufferSource will not
 * accept a view that might sit on a SharedArrayBuffer.
 */
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
