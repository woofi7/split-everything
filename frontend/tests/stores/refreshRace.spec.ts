import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { resetDatabase } from '@/offline/db'

const user = {
  id: 'user-1',
  email: 'alice@example.com',
  displayName: 'Alice',
  avatarUrl: null,
  defaultCurrency: 'CAD',
  prefersLightTheme: false,
}

const SESSION_KEY = 'split-everything.session'

const tokensFor = (suffix: string, minutes = 15) => ({
  accessToken: `access-${suffix}`,
  accessTokenExpiresAt: new Date(Date.now() + minutes * 60_000).toISOString(),
  refreshToken: `refresh-${suffix}`,
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
})

function api(overrides: Record<string, unknown> = {}) {
  return {
    post: vi.fn(async (path: string, body?: unknown) => {
      if (path === '/auth/refresh') {
        if ((body as { refreshToken?: string })?.refreshToken !== 'refresh-live') {
          throw Object.assign(new Error('That session was already used. Sign in again.'), {
            status: 401,
          })
        }
        return tokensFor('rotated')
      }
      return null
    }),
    probe: vi.fn(async () => null),
    get: vi.fn(async () => user),
    patch: vi.fn(async () => user),
    delete: vi.fn(async () => null),
    ...overrides,
  }
}

function signedInWith(suffix: string) {
  const store = useAuthStore()
  const client = api()
  store.attachApi(client as never)
  store.user = user as never
  store.tokens = tokensFor(suffix) as never
  return { store, client }
}

describe('refreshing when another tab got there first', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('adopts the token the other tab stored instead of replaying a dead one', async () => {
    const { store, client } = signedInWith('stale')
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ user, tokens: tokensFor('live') }),
    )

    const token = await store.refresh()

    expect(token).toBe('access-live')
    expect(store.isSignedIn).toBe(true)
    expect(client.post).not.toHaveBeenCalled()
  })

  it('refreshes for real when storage holds the same token it has', async () => {
    const { store, client } = signedInWith('live')
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ user, tokens: tokensFor('live') }),
    )

    const token = await store.refresh()

    expect(token).toBe('access-rotated')
    expect(client.post).toHaveBeenCalledWith('/auth/refresh', { refreshToken: 'refresh-live' })
  })

  it('refreshes for real when nothing is stored', async () => {
    const { store, client } = signedInWith('live')

    expect(await store.refresh()).toBe('access-rotated')
    expect(client.post).toHaveBeenCalled()
  })

  it('still signs out when the stored token is the dead one too', async () => {
    const { store } = signedInWith('stale')
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ user, tokens: tokensFor('stale') }),
    )

    expect(await store.refresh()).toBeNull()
    expect(store.isSignedIn).toBe(false)
  })

  it('ignores stored state it cannot read', async () => {
    const { store, client } = signedInWith('live')
    localStorage.setItem(SESSION_KEY, 'not json')

    expect(await store.refresh()).toBe('access-rotated')
    expect(client.post).toHaveBeenCalled()
  })

  it('ignores a stored session for a different account', async () => {
    const { store, client } = signedInWith('live')
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ user: { ...user, id: 'user-2' }, tokens: tokensFor('other') }),
    )

    await store.refresh()
    expect(client.post).toHaveBeenCalledWith('/auth/refresh', { refreshToken: 'refresh-live' })
  })
})
