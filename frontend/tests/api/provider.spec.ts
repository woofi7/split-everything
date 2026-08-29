import { afterEach, describe, expect, it } from 'vitest'
import { hasApiClient, setApiClient, useApi } from '@/api/provider'
import { apiBaseUrl, googleClientId } from '@/api/config'

afterEach(() => {
  setApiClient(null)
})

describe('api provider', () => {
  it('has no client before bootstrap', () => {
    setApiClient(null)

    expect(hasApiClient()).toBe(false)
  })

  it('hands back the client bootstrap provided', () => {
    const client = { get: () => null } as never
    setApiClient(client)

    expect(useApi()).toBe(client)
    expect(hasApiClient()).toBe(true)
  })

  it('fails loudly when a view asks before bootstrap', () => {
    setApiClient(null)

    // Silently returning a half-built client would surface later as a confusing
    // network error rather than a wiring mistake.
    expect(() => useApi()).toThrow(/setApiClient/)
  })

  it('can be replaced, which is how a test swaps in a fake', () => {
    const first = { get: () => 1 } as never
    const second = { get: () => 2 } as never

    setApiClient(first)
    setApiClient(second)

    expect(useApi()).toBe(second)
  })
})

describe('build configuration', () => {
  it('falls back to the same-origin api path', () => {
    // vitest.config.ts sets this, standing in for the value Vite inlines at build.
    expect(apiBaseUrl()).toBe('/api')
  })

  it('reports no google client id when none was configured', () => {
    // Sign-in has to be able to tell the difference between unconfigured and
    // broken, so this returns an empty string rather than undefined.
    expect(googleClientId()).toBe('')
  })
})
