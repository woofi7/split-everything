import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { resetDatabase } from '@/offline/db'

/**
 * Coming back to a device that already knows who you are.
 *
 * The browser holds the refresh token in an httpOnly cookie for thirty days,
 * which the app could not see and so never used: clearing local storage, or a
 * shell that never wrote it, dropped someone at a blank sign-in form while a
 * perfectly good session sat in the cookie jar.
 *
 * Two outcomes, and only two. A session the server will still honour signs in
 * with nothing asked. Anything else asks for the account this device belongs to
 * by name, rather than starting from nobody.
 */

const user = {
  id: 'user-1',
  email: 'alice@example.com',
  displayName: 'Alice',
  avatarUrl: null,
  defaultCurrency: 'CAD',
  prefersLightTheme: false,
}

const tokens = {
  accessToken: 'access-1',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-1',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
}

function fakeApi(overrides: Record<string, unknown> = {}) {
  return {
    probe: vi.fn(async (path: string) =>
      path === '/auth/refresh' ? { ...tokens, accessToken: 'access-2' } : null),
    post: vi.fn(async (path: string) => {
      if (path === '/auth/google') return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
      if (path === '/auth/dev') return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
      if (path === '/auth/refresh') return { ...tokens, accessToken: 'access-2' }
      return null
    }),
    get: vi.fn(async () => user),
    patch: vi.fn(async () => user),
    delete: vi.fn(async () => null),
    ...overrides,
  }
}

function storeWith(api = fakeApi()) {
  const store = useAuthStore()
  store.attachApi(api as never)
  return { store, api }
}

describe('resuming a session on a known device', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('signs in from the cookie when there is nothing stored locally', async () => {
    const { store, api } = storeWith()

    const resumed = await store.resumeSession()

    // No token to send: the server reads the cookie the browser kept.
    expect(api.probe).toHaveBeenCalledWith('/auth/refresh')
    expect(resumed).toBe(true)
    expect(store.isSignedIn).toBe(true)
    expect(store.user?.email).toBe('alice@example.com')
  })

  it('asks the server who the session belongs to', async () => {
    const { store, api } = storeWith()

    await store.resumeSession()

    expect(api.get).toHaveBeenCalledWith('/auth/me')
    expect(store.accessToken).toBe('access-2')
  })

  it('does nothing when it is already signed in', async () => {
    const { store, api } = storeWith()
    store.restore()
    await store.signInAsDeveloper('alice@example.com')
    api.post.mockClear()

    const resumed = await store.resumeSession()

    expect(resumed).toBe(true)
    expect(api.probe).not.toHaveBeenCalled()
  })

  it('reports no session when the cookie is gone', async () => {
    const { store } = storeWith(
      fakeApi({ probe: vi.fn(async () => null) }),
    )

    const resumed = await store.resumeSession()

    expect(resumed).toBe(false)
    expect(store.isSignedIn).toBe(false)
  })

  it('stays signed out when the refresh works but the account cannot be read', async () => {
    const { store } = storeWith(
      fakeApi({
        get: vi.fn(async () => {
          throw new Error('Unauthorized')
        }),
      }),
    )

    // Half a session is worse than none: the shell would render and then fail on
    // its first real request.
    expect(await store.resumeSession()).toBe(false)
    expect(store.isSignedIn).toBe(false)
  })

  it('does not throw when the server cannot be reached at all', async () => {
    const { store } = storeWith(
      fakeApi({ probe: vi.fn(async () => null) }),
    )

    await expect(store.resumeSession()).resolves.toBe(false)
  })
})

describe('the account a device belongs to', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('remembers who signed in here', async () => {
    const { store } = storeWith()

    await store.signInAsDeveloper('alice@example.com')

    expect(store.rememberedAccount).toEqual({
      email: 'alice@example.com',
      displayName: 'Alice',
      avatarUrl: null,
    })
  })

  it('still remembers after signing out', async () => {
    const { store } = storeWith()
    await store.signInAsDeveloper('alice@example.com')

    await store.signOut()

    // The point of remembering: the next visit asks for this account by name
    // rather than starting from nobody.
    expect(store.isSignedIn).toBe(false)
    expect(store.rememberedAccount?.email).toBe('alice@example.com')
  })

  it('survives a reload', async () => {
    const { store } = storeWith()
    await store.signInAsDeveloper('alice@example.com')

    setActivePinia(createPinia())
    const { store: reloaded } = storeWith()
    reloaded.restore()

    expect(reloaded.rememberedAccount?.displayName).toBe('Alice')
  })

  it('forgets when asked, so a shared device can be handed over', async () => {
    const { store } = storeWith()
    await store.signInAsDeveloper('alice@example.com')
    await store.signOut()

    store.forgetDevice()

    expect(store.rememberedAccount).toBeNull()

    setActivePinia(createPinia())
    const { store: reloaded } = storeWith()
    reloaded.restore()
    expect(reloaded.rememberedAccount).toBeNull()
  })

  it('replaces the account when someone else signs in here', async () => {
    const { store } = storeWith()
    await store.signInAsDeveloper('alice@example.com')

    const bob = { ...user, id: 'user-2', email: 'bob@example.com', displayName: 'Bob' }
    store.attachApi(
      fakeApi({
        post: vi.fn(async (path: string) =>
          path === '/auth/dev'
            ? { user: bob, tokens, isNewUser: false, autoJoinedGroupIds: [] }
            : { ...tokens },
        ),
      }) as never,
    )
    await store.signInAsDeveloper('bob@example.com')

    expect(store.rememberedAccount?.email).toBe('bob@example.com')
  })

  it('ignores a remembered account it cannot read', async () => {
    localStorage.setItem('split-everything.device-account', 'not json')

    const { store } = storeWith()
    store.restore()

    expect(store.rememberedAccount).toBeNull()
  })
})
