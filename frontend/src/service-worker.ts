/// <reference lib="webworker" />
import { precacheAndRoute, cleanupOutdatedCaches } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'
import { createHandlerBoundToURL } from 'workbox-precaching'

declare let self: ServiceWorkerGlobalScope

/**
 * The PWA shell and Web Push.
 *
 * The API is deliberately never cached: reads come from IndexedDB and writes go
 * through the outbox, so a stale cached response would only ever contradict the
 * local replica. This worker caches the app shell and handles notifications.
 */
cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

// Any navigation falls back to the shell, so a deep link works offline.
registerRoute(new NavigationRoute(createHandlerBoundToURL('index.html'), {
  denylist: [/^\/api\//, /^\/hubs\//],
}))

self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') void self.skipWaiting()
})

self.addEventListener('push', (event) => {
  if (!event.data) return

  let payload: { title?: string; body?: string; url?: string; tag?: string }
  try {
    payload = event.data.json()
  } catch {
    payload = { title: 'Split Everything', body: event.data.text() }
  }

  event.waitUntil(
    self.registration.showNotification(payload.title ?? 'Split Everything', {
      body: payload.body ?? '',
      tag: payload.tag,
      icon: '/icons/icon-192.png',
      badge: '/icons/icon-192.png',
      data: { url: payload.url ?? '/' },
      // Replaces rather than stacks, so five expenses do not mean five buzzes.
      // Cast because renotify ships in browsers but not in the DOM types yet.
      ...(payload.tag ? { renotify: true } : {}),
    } as NotificationOptions),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = (event.notification.data as { url?: string })?.url ?? '/'

  event.waitUntil(
    (async () => {
      const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })

      // Focus an open tab rather than opening a second copy of the app.
      for (const client of clients) {
        if ('focus' in client) {
          await client.focus()
          if ('navigate' in client) await client.navigate(url)
          return
        }
      }

      await self.clients.openWindow(url)
    })(),
  )
})
