import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { router } from '@/router'
import { useAuthStore } from '@/stores/auth'

const session = {
  user: {
    id: 'user-1',
    email: 'alice@example.com',
    displayName: 'Alice',
    avatarUrl: null,
    defaultCurrency: 'CAD',
    prefersLightTheme: false,
  },
  tokens: {
    accessToken: 'access-1',
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  },
}

/** A server that will sign a known device back in from its address alone. */
function reconnectableApi() {
  return {
    probe: vi.fn(async () => null),
    get: vi.fn(async () => ({ googleConfigured: false, developmentSignIn: true })),
    post: vi.fn(async () => ({
      user: session.user,
      tokens: session.tokens,
      isNewUser: false,
      autoJoinedGroupIds: [],
    })),
  }
}

describe('route guard', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await router.replace('/sign-in')
    await router.isReady()
  })

  it('sends a signed-out visitor to sign-in', async () => {
    await router.push('/groups')

    expect(router.currentRoute.value.name).toBe('sign-in')
  })

  it('remembers where they were going', async () => {
    await router.push('/groups/group-1')

    // Otherwise a deep link or an invite would dump them on the group list after
    // signing in, having lost what they clicked.
    expect(router.currentRoute.value.query.redirect).toBe('/groups/group-1')
  })

  it('lets a signed-in visitor through', async () => {
    localStorage.setItem('split-everything.session', JSON.stringify(session))
    useAuthStore().restore()

    await router.push('/groups')

    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('lets a device that already belongs to someone straight through', async () => {
    localStorage.setItem(
      'split-everything.device-account',
      JSON.stringify({ email: 'alice@example.com', displayName: 'Alice', avatarUrl: null }),
    )
    const auth = useAuthStore()
    auth.attachApi(reconnectableApi() as never)
    auth.restore()

    await router.push('/groups')

    // No detour through sign-in: the device knows whose it is, so it gets itself
    // back in rather than asking a question it has the answer to.
    expect(router.currentRoute.value.name).toBe('dashboard')
    expect(auth.isSignedIn).toBe(true)
  })

  it('still asks when the device belongs to nobody', async () => {
    const auth = useAuthStore()
    auth.attachApi(reconnectableApi() as never)
    auth.restore()

    await router.push('/groups')

    expect(router.currentRoute.value.name).toBe('sign-in')
  })

  it('leaves the invite page public', async () => {
    await router.push('/join/some-token')

    // A person who has never opened the app has to be able to see the invite.
    expect(router.currentRoute.value.name).toBe('join')
  })

  it('leaves sign-in itself public', async () => {
    await router.push('/sign-in')

    expect(router.currentRoute.value.name).toBe('sign-in')
  })

  it('redirects the root to the dashboard', async () => {
    localStorage.setItem('split-everything.session', JSON.stringify(session))
    useAuthStore().restore()

    await router.push('/')

    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('has a catch-all for an unknown path', async () => {
    localStorage.setItem('split-everything.session', JSON.stringify(session))
    useAuthStore().restore()

    await router.push('/nowhere-at-all')

    expect(router.currentRoute.value.name).toBe('not-found')
  })

  it('routes every named screen the nav and views link to', () => {
    const names = router.getRoutes().map((route) => route.name)

    for (const name of [
      'dashboard',
      'group',
      'group-settings',
      'new-group',
      'add-expense',
      'expense',
      'settle',
      'activity',
      'stats',
      'import',
      'conflicts',
      'profile',
      'join',
      'sign-in',
    ]) {
      expect(names).toContain(name)
    }
  })
})
