import { afterEach, describe, expect, it } from 'vitest'
import { getDeviceId, resetDatabase } from '@/offline/db'

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

    expect(await getDeviceId()).toBe(await getDeviceId())
  })
})
