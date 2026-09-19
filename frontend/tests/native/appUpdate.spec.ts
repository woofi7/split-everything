import { afterEach, describe, expect, it, vi } from 'vitest'
import { checkForAppUpdate } from '@/native/appUpdate'

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

    await expect(checkForAppUpdate()).resolves.toBeUndefined()
  })
})
