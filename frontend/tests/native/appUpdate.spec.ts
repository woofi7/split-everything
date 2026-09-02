import { afterEach, describe, expect, it, vi } from 'vitest'
import { checkForAppUpdate } from '@/native/appUpdate'

/**
 * Asking whether there is a newer build.
 *
 * A service worker looks for one on navigation and at most once a day, which on a
 * home-screen app is almost never: it is resumed rather than navigated. Pulling
 * down is somebody asking for the latest, so it asks for this too.
 */
describe('checking for a new version', () => {
  afterEach(() => {
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  function withRegistration(registration: unknown): void {
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: { getRegistration: vi.fn(async () => registration) },
    })
  }

  it('asks the registration to look', async () => {
    const update = vi.fn(async () => {})
    withRegistration({ update })

    await checkForAppUpdate()

    expect(update).toHaveBeenCalled()
  })

  it('says nothing when there is no worker registered yet', async () => {
    withRegistration(undefined)

    // A first visit, or a build with no service worker: not a failure.
    await expect(checkForAppUpdate()).resolves.toBeUndefined()
  })

  it('survives a browser with no service workers at all', async () => {
    await expect(checkForAppUpdate()).resolves.toBeUndefined()
  })

  it('swallows a failed check, because a refresh is still a refresh', async () => {
    withRegistration({
      update: vi.fn(async () => {
        throw new Error('offline')
      }),
    })

    // Offline is the ordinary case for this call, and the sync beside it is the
    // part somebody actually pulled down for.
    await expect(checkForAppUpdate()).resolves.toBeUndefined()
  })
})
