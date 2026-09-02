import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import SignInView from '@/views/SignInView.vue'
import JoinView from '@/views/JoinView.vue'
import NotFoundView from '@/views/NotFoundView.vue'
import { fakeApi, mountView, settle, textOf, waitFor } from '../support/viewHarness'

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

    let options: Record<string, unknown> | undefined

    ;(window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: vi.fn((given: { callback: (r: { credential?: string }) => void }) => {
            options = given as unknown as Record<string, unknown>
            callback = given.callback
          }),
          renderButton,
        },
      },
    }

    return {
      renderButton,
      fire: (credential) => callback?.({ credential }),
      initializeOptions: () => options,
    }
  }

  it('signs the device back in as its own account, without asking', async () => {
    const { api } = await mountView(SignInView, {
      signedIn: false,
      api: fakeApi({
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
            accessToken: 'a',
            accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
            refreshToken: 'r',
            refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
          },
          isNewUser: false,
          autoJoinedGroupIds: [],
        }),
      }),
      rememberedAccount: { email: 'alice@example.com', displayName: 'Alice', avatarUrl: null },
    })
    await settle()

    // The device already knows whose it is. Asking is a question it has the
    // answer to, so it answers it and leaves.
    expect(api.post).toHaveBeenCalledWith(
      '/auth/dev',
      expect.objectContaining({ email: 'alice@example.com' }),
    )
    expect(replace).toHaveBeenCalledWith('/dashboard')
  })

  it('never puts a confirm-who-you-are step on screen', async () => {
    const { wrapper } = await mountView(SignInView, {
      signedIn: false,
      api: fakeApi({
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
            accessToken: 'a',
            accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
            refreshToken: 'r',
            refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
          },
          isNewUser: false,
          autoJoinedGroupIds: [],
        }),
      }),
      rememberedAccount: { email: 'alice@example.com', displayName: 'Alice', avatarUrl: null },
    })
    await settle()

    // Not "welcome back, continue as Alice": there is nothing to confirm.
    expect(textOf(wrapper)).not.toContain('Continue as')
    expect(wrapper.find('[data-testid="continue-as"]').exists()).toBe(false)
  })

  it('keeps the redirect when it reconnects, so an invite still lands', async () => {
    query = { redirect: '/join/invite-token' }

    await mountView(SignInView, {
      signedIn: false,
      api: fakeApi({
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
            accessToken: 'a',
            accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
            refreshToken: 'r',
            refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
          },
          isNewUser: false,
          autoJoinedGroupIds: [],
        }),
      }),
      rememberedAccount: { email: 'alice@example.com', displayName: 'Alice', avatarUrl: null },
    })
    await settle()

    expect(replace).toHaveBeenCalledWith('/join/invite-token')
  })

  it('asks who you are when the device belongs to nobody', async () => {
    const { wrapper, api } = await mountView(SignInView, {
      signedIn: false,
      api: fakeApi({
        '/auth/capabilities': () => ({ googleConfigured: false, developmentSignIn: true }),
      }),
    })
    await settle()

    // Nothing to reconnect to, so nothing is attempted and the form is the page.
    expect(api.post).not.toHaveBeenCalledWith('/auth/dev', expect.anything())
    expect(replace).not.toHaveBeenCalled()
    expect(wrapper.find('input[type="email"]').exists()).toBe(true)
  })

  it('asks who you are when only Google can answer', async () => {
    const { wrapper, api } = await mountView(SignInView, {
      signedIn: false,
      api: fakeApi({
        '/auth/capabilities': () => ({ googleConfigured: true, developmentSignIn: false }),
      }),
      rememberedAccount: { email: 'alice@example.com', displayName: 'Alice', avatarUrl: null },
    })
    await settle()

    // An address is not a credential. Where Google is the only way in, the
    // credential has to come from Google, so the page stays.
    expect(api.post).not.toHaveBeenCalledWith('/auth/dev', expect.anything())
    expect(replace).not.toHaveBeenCalled()
    expect(wrapper.find('input[type="email"]').exists()).toBe(false)
  })

  it('tells Google which account to offer', async () => {
    const google = withGoogle()
    await mountView(SignInView, {
      signedIn: false,
      rememberedAccount: { email: 'alice@example.com', displayName: 'Alice', avatarUrl: null },
    })

    // So the chooser opens on the right account rather than every account signed
    // into the browser.
    expect(google.initializeOptions()?.login_hint).toBe('alice@example.com')
  })

  it('explains what the app is', async () => {
    const { wrapper } = await mountView(SignInView, { signedIn: false })

    expect(textOf(wrapper)).toContain('Shared expenses, settled properly')
  })

  it('says so when the Google script did not load', async () => {
    const { wrapper } = await mountView(SignInView, { signedIn: false })
    // The screen fetches Google's library before it can know, and a script that
    // cannot be fetched settles a beat later.
    await waitFor(() => wrapper.find('[role="alert"]').exists())

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

  it('asks for a button too narrow for Google to personalize', async () => {
    const { renderButton } = withGoogle()

    await mountView(SignInView, { signedIn: false })

    // Not a stylistic width. At 200 and above Google swaps this HTML button for a
    // cross-origin iframe whose canvas paints opaque white on our coloured page,
    // and nothing on this side can recolour it. Widening this puts the white slab
    // back for everyone who has a Google session.
    const options = renderButton.mock.calls[0][1] as { width: number; theme: string }
    expect(options.width).toBeLessThan(200)
    expect(options.theme).toBe('filled_black')
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
    expect(replace).toHaveBeenCalledWith('/dashboard')
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
    expect(replace).toHaveBeenCalledWith('/dashboard')
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
    expect(replace).toHaveBeenCalledWith('/dashboard')
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
    await waitFor(() => wrapper.find('[role="alert"]').exists())

    expect(wrapper.find('[role="alert"]').text()).toContain('unavailable')
  })

  it('sends an already signed-in visitor straight on', async () => {
    await mountView(SignInView, { signedIn: true })

    expect(replace).toHaveBeenCalledWith('/dashboard')
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

    // The target carries the intent to join, so coming back finishes the job
    // rather than asking for the same tap a second time.
    expect(push).toHaveBeenCalledWith({
      name: 'sign-in',
      query: { redirect: '/join/invite-token?join=1' },
    })
  })

  it('joins on its own when the visitor comes back from signing in', async () => {
    // The spec's flow is one decision: open the link, sign in, you are in the
    // group. Landing back on the same page with the same button still to press
    // reads as though signing in did nothing.
    query = { join: '1' }
    const api = preview()
    api.post.mockResolvedValue({ groupId: 'group-1' })

    await mountView(JoinView, { api, signedIn: true })
    await settle()

    expect(api.post).toHaveBeenCalledWith('/invites/invite-token/redeem')
    expect(replace).toHaveBeenCalledWith({ name: 'group', params: { groupId: 'group-1' } })
  })

  it('does not join on its own when the visitor just opened the link', async () => {
    const api = preview()
    api.post.mockResolvedValue({ groupId: 'group-1' })

    // No intent recorded: they should see what they are joining and decide.
    const { wrapper } = await mountView(JoinView, { api, signedIn: true })
    await settle()

    expect(api.post).not.toHaveBeenCalled()
    expect(wrapper.find('button[type="button"]').exists()).toBe(true)
  })

  it('does not try to join on its own while signed out', async () => {
    query = { join: '1' }
    const api = preview()

    const { wrapper } = await mountView(JoinView, { api, signedIn: false })
    await settle()

    expect(api.post).not.toHaveBeenCalled()
    expect(textOf(wrapper)).toContain('Sign in with Google to join')
  })

  it('explains it when joining on its own fails', async () => {
    query = { join: '1' }
    const api = preview()
    api.post.mockRejectedValue(new Error('That invite has already been used.'))

    const { wrapper } = await mountView(JoinView, { api, signedIn: true })
    await settle()

    expect(textOf(wrapper)).toContain('That invite has already been used.')
    expect(replace).not.toHaveBeenCalled()
  })

  it('does not try to join an invite that cannot be redeemed', async () => {
    query = { join: '1' }
    const api = preview({ isRedeemable: false })

    await mountView(JoinView, { api, signedIn: true })
    await settle()

    expect(api.post).not.toHaveBeenCalled()
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
