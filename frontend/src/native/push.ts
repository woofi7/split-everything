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

/**
 * How turning notifications on ended.
 *
 * A boolean could not be honest here. A refused permission, a browser with no push
 * at all and a server with no keys behind the switch all came back false, so the
 * app told all three of them that notifications "were not allowed" - and only one
 * of the three has anything to do with permission. The one that says nothing about
 * this device is 'unconfigured': the deployment is unfinished, and no amount of
 * tapping will change it.
 */
export type PushOutcome = 'on' | 'denied' | 'unsupported' | 'unconfigured' | 'failed'

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

  // No key means nothing to subscribe to: the server was deployed without its
  // VAPID pair. Worth its own answer, because the phone in your hand did nothing
  // wrong and there is nothing it can do about it.
  const { publicKey } = await api.get<{ publicKey: string }>('/notifications/vapid-key')
  if (!publicKey) return 'unconfigured'

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

  return 'on'
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
