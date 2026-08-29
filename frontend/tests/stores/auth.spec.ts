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

  it('signs out when the refresh fails', async () => {
    const api = fakeApi({
      post: vi.fn(async (path: string) => {
        if (path === '/auth/google') return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
        throw new Error('refresh rejected')
      }),
    })
    const store = useAuthStore()
    store.attachApi(api as never)
    await store.signInWithGoogle('google-credential')

    expect(await store.refresh()).toBeNull()
    expect(store.isSignedIn).toBe(false)
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
