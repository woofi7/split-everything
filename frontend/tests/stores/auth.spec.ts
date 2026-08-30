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
    post: vi.fn(async (path: string) => {
      if (path === '/auth/google') return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
      if (path === '/auth/refresh') return { ...tokens, accessToken: 'access-2' }
      return null
    }),
    get: vi.fn(async () => user),
    patch: vi.fn(async () => ({ ...user, displayName: 'Alice A' })),
    delete: vi.fn(async () => null),
    ...overrides,
  }
}

describe('auth store', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('starts signed out', () => {
    const store = useAuthStore()

    expect(store.isSignedIn).toBe(false)
    expect(store.user).toBeNull()
  })

  it('signs in with a google credential', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)

    await store.signInWithGoogle('google-credential')

    expect(store.isSignedIn).toBe(true)
    expect(store.user?.email).toBe('alice@example.com')
    expect(store.accessToken).toBe('access-1')
  })

  it('keeps the session across a reload', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)
    await store.signInWithGoogle('google-credential')

    setActivePinia(createPinia())
    const revived = useAuthStore()
    revived.restore()

    // Without this the app would bounce the user to sign-in on every launch.
    expect(revived.isSignedIn).toBe(true)
    expect(revived.user?.displayName).toBe('Alice')
  })

  it('refreshes the access token', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)
    await store.signInWithGoogle('google-credential')

    const token = await store.refresh()

    expect(token).toBe('access-2')
    expect(store.accessToken).toBe('access-2')
  })

  /**
   * What ends a session, and what merely fails.
   *
   * Only the server can say a refresh token is spent. Treating every failure as
   * that meant a refresh attempted with no connection signed the app out, which
   * locked somebody out of data sitting on their own device, and it produced a
   * storm: cleared session, sign-in screen, another resume, another failure.
   */
  const refusing = (status: number) =>
    fakeApi({
      post: vi.fn(async (path: string) => {
        if (path === '/auth/google') return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
        throw Object.assign(new Error(`refused with ${status}`), { status })
      }),
    })

  const signedIn = async (api: ReturnType<typeof fakeApi>) => {
    const store = useAuthStore()
    store.attachApi(api as never)
    await store.signInWithGoogle('google-credential')
    return store
  }

  it('signs out when the server refuses the refresh token', async () => {
    const store = await signedIn(refusing(401))

    expect(await store.refresh()).toBeNull()
    expect(store.isSignedIn).toBe(false)
  })

  it('signs out when the server forbids it', async () => {
    const store = await signedIn(refusing(403))

    expect(await store.refresh()).toBeNull()
    expect(store.isSignedIn).toBe(false)
  })

  it('keeps the session when there is no connection', async () => {
    const api = fakeApi({
      post: vi.fn(async (path: string) => {
        if (path === '/auth/google') return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
        throw new TypeError('Failed to fetch')
      }),
    })
    const store = await signedIn(api)

    await expect(store.refresh()).rejects.toThrow()
    // Still signed in: the data on this device is the whole point of the replica.
    expect(store.isSignedIn).toBe(true)
  })

  it('keeps the session when the server is rate limiting it', async () => {
    const store = await signedIn(refusing(429))

    await expect(store.refresh()).rejects.toThrow()
    expect(store.isSignedIn).toBe(true)
  })

  it('keeps the session when the server itself is broken', async () => {
    const store = await signedIn(refusing(503))

    await expect(store.refresh()).rejects.toThrow()
    expect(store.isSignedIn).toBe(true)
  })

  it('clears everything on sign out', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)
    await store.signInWithGoogle('google-credential')

    await store.signOut()

    expect(store.isSignedIn).toBe(false)
    expect(store.accessToken).toBeNull()
    expect(localStorage.getItem('split-everything.session')).toBeNull()
  })

  it('reports a newly created account, so the app can show a welcome', async () => {
    const api = fakeApi({
      post: vi.fn(async () => ({ user, tokens, isNewUser: true, autoJoinedGroupIds: ['group-1'] })),
    })
    const store = useAuthStore()
    store.attachApi(api as never)

    const result = await store.signInWithGoogle('google-credential')

    expect(result.isNewUser).toBe(true)
    expect(result.autoJoinedGroupIds).toEqual(['group-1'])
  })

  it('updates the profile', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)
    await store.signInWithGoogle('google-credential')

    await store.updateProfile({ displayName: 'Alice A' })

    expect(store.user?.displayName).toBe('Alice A')
  })

  it('exposes the theme the user chose', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)
    await store.signInWithGoogle('google-credential')

    // Dark by default, per the spec.
    expect(store.theme).toBe('dark')

    await store.setTheme('light')
    expect(store.theme).toBe('light')
  })

  /**
   * The accent the whole application wears.
   *
   * On the account, so it follows the person onto any device they sign in on, and
   * applied the moment it is tapped: the app changes colour there and then, and a
   * spinner over a swatch while a server agrees would be absurd.
   */
  describe('the app accent', () => {
    it('wears the default until somebody says otherwise', async () => {
      const store = useAuthStore()
      store.attachApi(fakeApi() as never)
      await store.signInWithGoogle('google-credential')

      expect(store.accent.name).toBe('indigo')
    })

    it('wears what the account asked for', async () => {
      const api = fakeApi({ post: vi.fn(async () => ({
        user: { ...user, themeName: 'teal' },
        tokens,
        isNewUser: false,
        autoJoinedGroupIds: [],
      })) })
      const store = useAuthStore()
      store.attachApi(api as never)
      await store.signInWithGoogle('google-credential')

      expect(store.accent.name).toBe('teal')
      expect(store.accent.shades[2]).toBe('#0d9488')
    })

    it('applies a new one at once and tells the account', async () => {
      const api = fakeApi({ patch: vi.fn(async () => ({ ...user, themeName: 'rose' })) })
      const store = useAuthStore()
      store.attachApi(api as never)
      await store.signInWithGoogle('google-credential')

      await store.setAccent('rose')

      expect(store.accent.name).toBe('rose')
      expect(api.patch).toHaveBeenCalledWith('/auth/me', { themeName: 'rose' })
    })

    it('keeps it on when the account cannot be told', async () => {
      const api = fakeApi({ patch: vi.fn(async () => { throw new Error('offline') }) })
      const store = useAuthStore()
      store.attachApi(api as never)
      await store.signInWithGoogle('google-credential')

      await store.setAccent('amber')

      // A preference, not a transaction, and it is already on screen.
      expect(store.accent.name).toBe('amber')
    })

    it('survives a reload with the session', async () => {
      const api = fakeApi({ patch: vi.fn(async () => ({ ...user, themeName: 'sky' })) })
      const store = useAuthStore()
      store.attachApi(api as never)
      await store.signInWithGoogle('google-credential')
      await store.setAccent('sky')

      setActivePinia(createPinia())
      const revived = useAuthStore()
      revived.restore()

      // Right on the first paint, without waiting for the profile to come back.
      expect(revived.accent.name).toBe('sky')
    })

    it('ignores a colour it does not have', async () => {
      const api = fakeApi()
      const store = useAuthStore()
      store.attachApi(api as never)
      await store.signInWithGoogle('google-credential')

      await store.setAccent('chartreuse')

      expect(store.accent.name).toBe('indigo')
      expect(api.patch).not.toHaveBeenCalled()
    })
  })

  it('remembers the theme without being signed in', async () => {
    const store = useAuthStore()

    await store.setTheme('light')

    setActivePinia(createPinia())
    const revived = useAuthStore()
    revived.restore()
    expect(revived.theme).toBe('light')
  })

  it('surfaces a sign-in failure without leaving a half session', async () => {
    const api = fakeApi({
      post: vi.fn(async () => {
        throw new Error('google rejected')
      }),
    })
    const store = useAuthStore()
    store.attachApi(api as never)

    await expect(store.signInWithGoogle('bad-credential')).rejects.toThrow()
    expect(store.isSignedIn).toBe(false)
  })

  it('treats a stored session with an expired refresh token as signed out', async () => {
    localStorage.setItem(
      'split-everything.session',
      JSON.stringify({
        user,
        tokens: { ...tokens, refreshTokenExpiresAt: new Date(Date.now() - 1000).toISOString() },
      }),
    )
    const store = useAuthStore()

    store.restore()

    expect(store.isSignedIn).toBe(false)
  })

  it('ignores an unreadable stored session', () => {
    localStorage.setItem('split-everything.session', 'not json')
    const store = useAuthStore()

    store.restore()

    expect(store.isSignedIn).toBe(false)
  })

  it('deletes the account and signs out', async () => {
    const store = useAuthStore()
    store.attachApi(fakeApi() as never)
    await store.signInWithGoogle('google-credential')

    await store.deleteAccount()

    expect(store.isSignedIn).toBe(false)
  })
})
