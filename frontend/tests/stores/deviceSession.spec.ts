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
    get: vi.fn(async (path: string) =>
      path === '/auth/capabilities' ? { googleConfigured: false, developmentSignIn: true } : user),
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

function knownDevice(api = fakeApi({ probe: vi.fn(async () => null) })) {
  localStorage.setItem(
    'split-everything.device-account',
    JSON.stringify({ email: 'alice@example.com', displayName: 'Alice', avatarUrl: null }),
  )
  const { store } = storeWith(api)
  store.restore()
  return { store, api }
}

describe('resuming a session on a known device', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('signs in from the cookie when no session is stored locally', async () => {
    const { store, api } = knownDevice(fakeApi())

    const resumed = await store.resumeSession()

    expect(api.probe).toHaveBeenCalledWith('/auth/refresh')
    expect(resumed).toBe(true)
    expect(store.isSignedIn).toBe(true)
    expect(store.user?.email).toBe('alice@example.com')
  })

  it('asks the server who the session belongs to', async () => {
    const { store, api } = knownDevice(fakeApi())

    await store.resumeSession()

    expect(api.get).toHaveBeenCalledWith('/auth/me')
    expect(store.accessToken).toBe('access-2')
  })

  it('asks the server nothing on a device that has never signed in here', async () => {
    const { store, api } = storeWith()

    const resumed = await store.resumeSession()

    expect(resumed).toBe(false)
    expect(api.probe).not.toHaveBeenCalled()
    expect(api.get).not.toHaveBeenCalled()
    expect(api.post).not.toHaveBeenCalled()
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

  it('stays signed out when the refresh works but the account cannot be read', async () => {
    const { store } = knownDevice(
      fakeApi({
        get: vi.fn(async () => {
          throw new Error('Unauthorized')
        }),
      }),
    )

    expect(await store.resumeSession()).toBe(false)
    expect(store.isSignedIn).toBe(false)
  })

  it('does not throw when the server cannot be reached at all', async () => {
    const { store } = knownDevice(
      fakeApi({
        probe: vi.fn(async () => {
          throw new Error('Failed to fetch')
        }),
        get: vi.fn(async () => {
          throw new Error('Failed to fetch')
        }),
      }),
    )

    await expect(store.resumeSession()).resolves.toBe(false)
  })
})

describe('reconnecting the account a device belongs to', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('signs back in as the remembered account when the cookie is gone', async () => {
    const { store, api } = knownDevice()

    const resumed = await store.resumeSession()

    expect(resumed).toBe(true)
    expect(store.isSignedIn).toBe(true)
    expect(api.post).toHaveBeenCalledWith('/auth/dev', expect.objectContaining({
      email: 'alice@example.com',
    }))
  })

  it('prefers the cookie, and does not sign in again when it works', async () => {
    const { store, api } = knownDevice(fakeApi())

    await store.resumeSession()

    expect(store.isSignedIn).toBe(true)
    expect(api.post).not.toHaveBeenCalledWith('/auth/dev', expect.anything())
  })

  it('does not reconnect a device that belongs to nobody', async () => {
    const { store, api } = storeWith(fakeApi({ probe: vi.fn(async () => null) }))
    store.restore()

    expect(await store.resumeSession()).toBe(false)
    expect(api.get).not.toHaveBeenCalledWith('/auth/capabilities')
  })

  it('does not reconnect from an address where Google is the only way in', async () => {
    const { store, api } = knownDevice(
      fakeApi({
        probe: vi.fn(async () => null),
        get: vi.fn(async () => ({ googleConfigured: true, developmentSignIn: false })),
      }),
    )

    expect(await store.resumeSession()).toBe(false)
    expect(api.post).not.toHaveBeenCalledWith('/auth/dev', expect.anything())
  })

  it('reports no session when the server cannot be reached', async () => {
    const { store } = knownDevice(
      fakeApi({
        probe: vi.fn(async () => null),
        get: vi.fn(async () => {
          throw new Error('Failed to fetch')
        }),
      }),
    )

    await expect(store.resumeSession()).resolves.toBe(false)
  })

  it('reports no session when the account no longer exists', async () => {
    const { store } = knownDevice(
      fakeApi({
        probe: vi.fn(async () => null),
        post: vi.fn(async () => {
          throw new Error('That account has been deleted.')
        }),
      }),
    )

    await expect(store.resumeSession()).resolves.toBe(false)
    expect(store.isSignedIn).toBe(false)
  })

  it('shares one attempt between concurrent callers', async () => {
    const { store, api } = knownDevice()

    const [first, second] = await Promise.all([store.resumeSession(), store.resumeSession()])

    expect(first).toBe(true)
    expect(second).toBe(true)
    expect(api.post).toHaveBeenCalledTimes(1)
  })

  it('can be asked again after an attempt has finished', async () => {
    const { store } = knownDevice(
      fakeApi({
        probe: vi.fn(async () => null),
        get: vi.fn(async () => {
          throw new Error('Failed to fetch')
        }),
      }),
    )

    expect(await store.resumeSession()).toBe(false)

    expect(await store.resumeSession()).toBe(false)
  })

  it('reconnects once per load, so a refused session cannot loop', async () => {
    const { store, api } = knownDevice()
    await store.resumeSession()

    store.sessionExpired()
    const resumed = await store.resumeSession()

    expect(resumed).toBe(false)
    expect(api.post).toHaveBeenCalledTimes(1)
  })

  it('does not reconnect after a deliberate sign-out', async () => {
    const { store } = knownDevice()
    await store.resumeSession()

    await store.signOut()
    const resumed = await store.resumeSession()

    expect(resumed).toBe(false)
    expect(store.isSignedIn).toBe(false)
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

  it('forgets the account when someone signs out on purpose', async () => {
    const { store } = storeWith()
    await store.signInAsDeveloper('alice@example.com')

    await store.signOut()

    expect(store.isSignedIn).toBe(false)
    expect(store.rememberedAccount).toBeNull()
    expect(localStorage.getItem('split-everything.device-account')).toBeNull()
  })

  it('keeps the account when the session merely expired', async () => {
    const { store } = storeWith()
    await store.signInAsDeveloper('alice@example.com')

    store.sessionExpired()

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
