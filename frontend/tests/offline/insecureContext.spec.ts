import { afterEach, describe, expect, it } from 'vitest'
import { getDeviceId, resetDatabase } from '@/offline/db'

/**
 * Startup outside a secure context.
 *
 * getDeviceId is the first thing bootstrap awaits, and it used to call
 * crypto.randomUUID, which is only defined in a secure context. Served over
 * plain HTTP on a LAN address it threw, bootstrap rejected, the app never
 * mounted, and the phone showed a blank screen with nothing on it to explain
 * why. This pins the one call that has to survive that.
 */

const original = crypto.randomUUID

afterEach(() => {
  Object.defineProperty(crypto, 'randomUUID', { value: original, configurable: true })
})

describe('startup with no crypto.randomUUID', () => {
  it('still resolves a device id', async () => {
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true })
    await resetDatabase()

    const id = await getDeviceId()

    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
  })

  it('keeps the same device id across calls', async () => {
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true })
    await resetDatabase()

    // It keys every vector clock, so it has to be stable for the life of the install.
    expect(await getDeviceId()).toBe(await getDeviceId())
  })
})
