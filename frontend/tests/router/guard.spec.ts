import { beforeEach, describe, expect, it } from 'vitest'
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
