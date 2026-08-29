import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import SignInView from '@/views/SignInView.vue'
import JoinView from '@/views/JoinView.vue'
import NotFoundView from '@/views/NotFoundView.vue'
import { fakeApi, mountView, settle, testGroup, textOf } from '../support/viewHarness'

const push = vi.fn()
const replace = vi.fn()
let query: Record<string, string> = {}

vi.mock('@/api/config', () => ({
  googleClientId: () => mockClientId,
  apiBaseUrl: () => '/api',
}))

let mockClientId = ''

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { token: 'invite-token' }, query, fullPath: '/join/invite-token' }),
  useRouter: () => ({ push, replace }),
  RouterLink: RouterLinkStub,
}))

describe('SignInView', () => {
  beforeEach(() => {
    push.mockClear()
    replace.mockClear()
    query = {}
    mockClientId = ''
    delete (window as unknown as { google?: unknown }).google
  })

  /** Puts a stand-in for Google Identity Services on the page. */
  function withGoogle(): { renderButton: ReturnType<typeof vi.fn>; fire: (credential?: string) => void } {
    mockClientId = 'test-client-id'
    let callback: ((response: { credential?: string }) => void) | undefined
    const renderButton = vi.fn()

    ;(window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: vi.fn((options: { callback: (r: { credential?: string }) => void }) => {
            callback = options.callback
          }),
          renderButton,
        },
      },
    }

    return { renderButton, fire: (credential) => callback?.({ credential }) }
  }

  it('explains what the app is', async () => {
    const { wrapper } = await mountView(SignInView, { signedIn: false })

    expect(textOf(wrapper)).toContain('Shared expenses, settled properly')
  })

  it('says so when the Google script did not load', async () => {
    const { wrapper } = await mountView(SignInView, { signedIn: false })

    // Blocked scripts and offline devices are normal; the page must say why
    // there is no button rather than showing nothing.
    expect(wrapper.find('[role="alert"]').text()).toContain('Google sign-in is unavailable')
  })

  it('renders the Google button when the script is present', async () => {
    const { renderButton } = withGoogle()

    const { wrapper } = await mountView(SignInView, { signedIn: false })

    expect(renderButton).toHaveBeenCalled()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('says so when the client id is not configured', async () => {
    withGoogle()
    mockClientId = ''

    const { wrapper } = await mountView(SignInView, { signedIn: false })

    // Without a client id there is nothing to render, and silence would look like
    // a broken page.
    expect(wrapper.find('[role="alert"]').text()).toContain('unavailable')
  })

  it('signs in with the credential Google hands back', async () => {
    const { fire } = withGoogle()

    const api = fakeApi({
      '/auth/google': () => ({
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
        isNewUser: false,
        autoJoinedGroupIds: [],
      }),
    })

    const { auth } = await mountView(SignInView, { api, signedIn: false })

    fire('google-credential')
    await settle()

    expect(auth.isSignedIn).toBe(true)
    expect(replace).toHaveBeenCalledWith('/groups')
  })

  it('returns to where the visitor was headed', async () => {
    query = { redirect: '/groups/group-1' }
    const { fire } = withGoogle()

    const api = fakeApi({
      '/auth/google': () => ({
        user: { id: 'user-1', email: 'a@b.c', displayName: 'A', avatarUrl: null, defaultCurrency: 'CAD', prefersLightTheme: false },
        tokens: {
          accessToken: 'a',
          accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
          refreshToken: 'r',
          refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
        },
        isNewUser: false,
        autoJoinedGroupIds: [],
      }),
    })

    await mountView(SignInView, { api, signedIn: false })
    fire('google-credential')
    await settle()

    expect(replace).toHaveBeenCalledWith('/groups/group-1')
  })

  it('ignores an off-site redirect target', async () => {
    query = { redirect: 'https://evil.example/steal' }
    const { fire } = withGoogle()

    const api = fakeApi({
      '/auth/google': () => ({
        user: { id: 'user-1', email: 'a@b.c', displayName: 'A', avatarUrl: null, defaultCurrency: 'CAD', prefersLightTheme: false },
        tokens: {
          accessToken: 'a',
          accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
          refreshToken: 'r',
          refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
        },
        isNewUser: false,
        autoJoinedGroupIds: [],
      }),
    })

    await mountView(SignInView, { api, signedIn: false })
    fire('google-credential')
    await settle()

    // An open redirect would let a crafted link bounce someone off-site with a
    // fresh session in hand.
    expect(replace).toHaveBeenCalledWith('/groups')
  })

  it('reports a refused sign-in', async () => {
    const { fire } = withGoogle()
    const api = fakeApi()
    api.post.mockRejectedValue(new Error('That Google sign-in could not be verified.'))

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })
    fire('bad-credential')
    await settle()

    expect(textOf(wrapper)).toContain('could not be verified')
  })

  it('reports a missing credential', async () => {
    const { fire } = withGoogle()

    const { wrapper } = await mountView(SignInView, { signedIn: false })
    fire(undefined)
    await settle()

    expect(textOf(wrapper)).toContain('did not return a credential')
  })

  it('offers the development sign-in when the server allows it', async () => {
    const api = fakeApi({ '/auth/capabilities': () => ({ googleConfigured: false, developmentSignIn: true } ) })

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })

    const text = textOf(wrapper)
    expect(text).toContain('Development sign-in')
    expect(text).toContain('Never enabled in production')
    expect(wrapper.find('input[type="email"]').exists()).toBe(true)
  })

  it('does not offer it when the server does not allow it', async () => {
    const api = fakeApi({ '/auth/capabilities': () => ({ googleConfigured: true, developmentSignIn: false } ) })

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })

    expect(wrapper.find('input[type="email"]').exists()).toBe(false)
  })

  it('does not complain about Google when there is another way in', async () => {
    const api = fakeApi({ '/auth/capabilities': () => ({ googleConfigured: false, developmentSignIn: true } ) })

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })

    // Showing "Google is unavailable" next to a working form would be confusing.
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('signs in with an address when the development path is offered', async () => {
    const api = fakeApi({
      '/auth/capabilities': () => ({ googleConfigured: false, developmentSignIn: true }),
      '/auth/dev': () => ({
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
        isNewUser: true,
        autoJoinedGroupIds: [],
      }),
    })

    const { wrapper, auth } = await mountView(SignInView, { api, signedIn: false })

    await wrapper.find('input[type="email"]').setValue('alice@example.com')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/auth/dev',
      expect.objectContaining({ email: 'alice@example.com' }),
    )
    expect(auth.isSignedIn).toBe(true)
    expect(replace).toHaveBeenCalledWith('/groups')
  })

  it('keeps the development submit disabled without an address', async () => {
    const api = fakeApi({ '/auth/capabilities': () => ({ googleConfigured: false, developmentSignIn: true } ) })

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('reports a refused development sign-in', async () => {
    const api = fakeApi({ '/auth/capabilities': () => ({ googleConfigured: false, developmentSignIn: true } ) })
    api.post.mockRejectedValue(new Error('Development sign-in is not enabled.'))

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })
    await wrapper.find('input[type="email"]').setValue('alice@example.com')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(textOf(wrapper)).toContain('not enabled')
  })

  it('still shows the Google failure when capabilities could not be read', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(SignInView, { api, signedIn: false })

    expect(wrapper.find('[role="alert"]').text()).toContain('unavailable')
  })

  it('sends an already signed-in visitor straight on', async () => {
    await mountView(SignInView, { signedIn: true })

    expect(replace).toHaveBeenCalledWith('/groups')
  })
})

describe('JoinView', () => {
  beforeEach(() => {
    push.mockClear()
    replace.mockClear()
    query = {}
  })

  const preview = (overrides: Record<string, unknown> = {}) =>
    fakeApi({
      '/invites/invite-token': () => ({
        groupId: 'group-1',
        groupName: 'Roommates',
        iconName: null,
        invitedByName: 'Alice',
        memberCount: 2,
        isRedeemable: true,
        ...overrides,
      }),
    })

  it('names the group and who invited you', async () => {
    const { wrapper } = await mountView(JoinView, { api: preview(), signedIn: false })

    const text = textOf(wrapper)
    expect(text).toContain('Roommates')
    expect(text).toContain('Alice invited you')
    expect(text).toContain('2 people are in this group')
  })

  it('uses the singular for a group of one', async () => {
    const { wrapper } = await mountView(JoinView, {
      api: preview({ memberCount: 1 }),
      signedIn: false,
    })

    expect(textOf(wrapper)).toContain('1 person is in this group')
  })

  it('asks a visitor who is not signed in to sign in first', async () => {
    const { wrapper } = await mountView(JoinView, { api: preview(), signedIn: false })

    expect(textOf(wrapper)).toContain('Sign in with Google to join')
  })

  it('sends an unauthenticated visitor to sign-in, keeping the invite as the target', async () => {
    const { wrapper } = await mountView(JoinView, { api: preview(), signedIn: false })

    await wrapper.find('button[type="button"]').trigger('click')
    await settle()

    expect(push).toHaveBeenCalledWith({
      name: 'sign-in',
      query: { redirect: '/join/invite-token' },
    })
  })

  it('joins the group when already signed in', async () => {
    const api = preview()
    api.post.mockResolvedValue({ groupId: 'group-1' })

    const { wrapper } = await mountView(JoinView, { api, signedIn: true })

    await wrapper.find('button[type="button"]').trigger('click')
    await settle()

    expect(api.post).toHaveBeenCalledWith('/invites/invite-token/redeem')
    expect(replace).toHaveBeenCalledWith({ name: 'group', params: { groupId: 'group-1' } })
  })

  it('says an expired invite cannot be used', async () => {
    const { wrapper } = await mountView(JoinView, {
      api: preview({ isRedeemable: false }),
      signedIn: false,
    })

    expect(textOf(wrapper)).toContain('no longer valid')
    expect(wrapper.find('button[type="button"]').exists()).toBe(false)
  })

  it('reports an invite that does not exist', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('Invite was not found.'))

    const { wrapper } = await mountView(JoinView, { api, signedIn: false })

    expect(wrapper.find('[role="alert"]').text()).toContain('not found')
  })

  it('reports a refused redemption', async () => {
    const api = preview()
    api.post.mockRejectedValue(new Error('This invite was issued to a different email address.'))

    const { wrapper } = await mountView(JoinView, { api, signedIn: true })
    await wrapper.find('button[type="button"]').trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('different email address')
  })
})

describe('NotFoundView', () => {
  it('offers a way back', async () => {
    const { wrapper } = await mountView(NotFoundView)

    expect(textOf(wrapper)).toContain('does not exist')
    expect(textOf(wrapper)).toContain('Back to your groups')
  })
})
