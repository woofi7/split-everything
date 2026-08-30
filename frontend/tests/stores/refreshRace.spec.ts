import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { resetDatabase } from '@/offline/db'

/**
 * Two tabs, one session.
 *
 * Scanning a QR code opens a new tab each time, so a phone ends up with several
 * on the same origin. They share stored state but not memory, and the server
 * treats a replayed refresh token as theft: it revokes every token for the
 * account. So the second tab to refresh signed both of them out, which reads as
 * "the authentication is not good".
 *
 * A tab about to refresh reads what is stored first. If another tab has already
 * done the work, it takes that result instead of replaying a token that is now
 * dead.
 */

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
        /*
         * The server's rule: a token already exchanged is treated as stolen, and
         * it answers 401. The status matters, because that is what separates the
         * session being over from the request merely failing: without it a refresh
         * attempted with no connection would sign the app out.
         */
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
    // This tab still holds the token it loaded with; the other tab has exchanged it.
    const { store, client } = signedInWith('stale')
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ user, tokens: tokensFor('live') }),
    )

    const token = await store.refresh()

    expect(token).toBe('access-live')
    expect(store.isSignedIn).toBe(true)
    // Nothing sent: replaying would have revoked the account's every token.
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

    // Someone else signed in here; adopting their token would be worse than
    // failing, so this tab asks with its own and finds out where it stands.
    await store.refresh()
    expect(client.post).toHaveBeenCalledWith('/auth/refresh', { refreshToken: 'refresh-live' })
  })
})
