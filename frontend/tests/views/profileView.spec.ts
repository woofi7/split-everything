import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ProfileView from '@/views/ProfileView.vue'
import { fakeApi, mountView, settle, testUser, textOf } from '../support/viewHarness'

const replace = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

describe('ProfileView', () => {
  it('shows the signed-in address', async () => {
    const { wrapper } = await mountView(ProfileView)

    expect(textOf(wrapper)).toContain('alice@example.com')
  })

  it('prefills the current name and currency', async () => {
    const { wrapper } = await mountView(ProfileView)

    expect((wrapper.find('input[type="text"]').element as HTMLInputElement).value).toBe('Alice')
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('CAD')
  })

  it('saves a profile change', async () => {
    const api = fakeApi({ '/auth/me': () => ({ ...testUser, displayName: 'Alice A' }) })
    const { wrapper } = await mountView(ProfileView, { api })

    await wrapper.find('input[type="text"]').setValue('Alice A')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.patch).toHaveBeenCalledWith(
      '/auth/me',
      expect.objectContaining({ displayName: 'Alice A' }),
    )
    expect(textOf(wrapper)).toContain('Saved')
  })

  it('reports a refused profile change', async () => {
    const api = fakeApi()
    api.patch.mockRejectedValue(new Error('Default currency must be a three-letter currency code.'))

    const { wrapper } = await mountView(ProfileView, { api })
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(textOf(wrapper)).toContain('three-letter currency code')
  })

  /**
   * The colour this person would like in the groups they join.
   *
   * A wish rather than a setting: a group where somebody already has it gives them
   * the next free one, because two the same in one group defeats the point.
   */
  describe('a preferred colour', () => {
    it('offers the palette the groups actually store', async () => {
      const { wrapper } = await mountView(ProfileView)

      // Twelve, matching the server's list: a colour a group cannot store is not
      // worth offering.
      expect(wrapper.findAll('[data-testid^="colour-"]')).toHaveLength(12)
    })

    it('saves the one that is tapped', async () => {
      const api = fakeApi({ '/auth/me': () => ({ ...testUser, preferredColorHex: '#f97316' }) })
      const { wrapper } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="colour-f97316"]').trigger('click')
      await settle()

      expect(api.patch).toHaveBeenCalledWith(
        '/auth/me',
        expect.objectContaining({ preferredColorHex: '#f97316' }),
      )
    })

    it('shows which one is chosen', async () => {
      const { wrapper, auth } = await mountView(ProfileView)
      // Set on the store the way a sign-in would have.
      auth.user = { ...testUser, preferredColorHex: '#14b8a6' } as never
      await settle(1)

      const pressed = wrapper
        .findAll('[data-testid^="colour-"]')
        .filter((swatch) => swatch.attributes('aria-pressed') === 'true')
        .map((swatch) => swatch.attributes('data-testid'))

      expect(pressed).toEqual(['colour-14b8a6'])
    })

    it('clears it when the chosen one is tapped again', async () => {
      const api = fakeApi({ '/auth/me': () => testUser })
      const { wrapper, auth } = await mountView(ProfileView, { api })
      auth.user = { ...testUser, preferredColorHex: '#f97316' } as never
      await settle(1)

      await wrapper.find('[data-testid="colour-f97316"]').trigger('click')
      await settle()

      // The only way back to having no preference.
      expect(api.patch).toHaveBeenCalledWith(
        '/auth/me',
        expect.objectContaining({ preferredColorHex: '' }),
      )
    })

    it('reports a colour the server will not take', async () => {
      const api = fakeApi()
      api.patch.mockRejectedValue(new Error('That is not one of the colours to choose from.'))
      const { wrapper } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="colour-f97316"]').trigger('click')
      await settle()

      expect(textOf(wrapper)).toContain('not one of the colours')
    })
  })

  it('starts in dark mode, as the spec asks', async () => {
    const { wrapper } = await mountView(ProfileView)

    const toggle = wrapper.find('[data-testid="theme-toggle"]')
    expect(toggle.attributes('aria-pressed')).toBe('false')
    expect(toggle.text()).toBe('Off')
  })

  it('switches to light mode', async () => {
    const { wrapper, auth } = await mountView(ProfileView)

    await wrapper.find('[data-testid="theme-toggle"]').trigger('click')
    await settle()

    expect(auth.theme).toBe('light')
    expect(wrapper.find('[data-testid="theme-toggle"]').attributes('aria-pressed')).toBe('true')
  })

  it('downloads the data export', async () => {
    const api = fakeApi()
    const click = vi.fn()
    const createElement = document.createElement.bind(document)
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const element = createElement(tag) as HTMLAnchorElement
      if (tag === 'a') element.click = click
      return element
    })
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:x', revokeObjectURL: vi.fn() })

    const { wrapper } = await mountView(ProfileView, { api })
    const button = wrapper.findAll('button').find((b) => b.text().includes('Download all my data'))
    await button!.trigger('click')
    await settle()

    expect(api.blob).toHaveBeenCalledWith('/auth/me/export')
    expect(click).toHaveBeenCalled()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('reports a failed export', async () => {
    const api = fakeApi()
    api.blob.mockRejectedValue(new Error('The server returned 500.'))

    const { wrapper } = await mountView(ProfileView, { api })
    const button = wrapper.findAll('button').find((b) => b.text().includes('Download all my data'))
    await button!.trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('returned 500')
  })

  it('disconnects the device and returns to sign-in', async () => {
    const { wrapper, auth } = await mountView(ProfileView)

    await wrapper.find('[data-testid="disconnect"]').trigger('click')
    await settle()

    expect(auth.isSignedIn).toBe(false)
    expect(replace).toHaveBeenCalledWith({ name: 'sign-in' })
  })

  it('stops the device reconnecting on its own', async () => {
    const { wrapper, auth } = await mountView(ProfileView, {
      rememberedAccount: { email: 'alice@example.com', displayName: 'Alice', avatarUrl: null },
    })

    await wrapper.find('[data-testid="disconnect"]').trigger('click')
    await settle()

    // Startup signs a remembered device back in without asking, so a disconnect
    // that left the account behind would put you straight back where you were
    // and the button would appear to do nothing.
    expect(auth.rememberedAccount).toBeNull()
  })

  it('shows its actions as buttons rather than lines of text', async () => {
    const { wrapper } = await mountView(ProfileView)

    // On a dark surface a bordered button with no fill was invisible, which is why
    // signing out looked like it did not exist.
    const disconnect = wrapper.find('[data-testid="disconnect"]')
    expect(disconnect.classes()).toContain('btn-secondary')
    expect(disconnect.classes()).toContain('btn-press')
  })

  it('asks for confirmation before deleting the account', async () => {
    const { wrapper, api } = await mountView(ProfileView)

    const button = wrapper.findAll('button').find((b) => b.text().includes('Delete my account'))
    await button!.trigger('click')
    await settle(1)

    // Irreversible, so it must not be one tap away.
    expect(api.delete).not.toHaveBeenCalled()
    expect(textOf(wrapper)).toContain('Your name stays on past expenses')
  })

  it('explains what account deletion does to other people balances', async () => {
    const { wrapper } = await mountView(ProfileView)

    const button = wrapper.findAll('button').find((b) => b.text().includes('Delete my account'))
    await button!.trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain("other people's balances remain correct")
  })

  it('can back out of deleting the account', async () => {
    const { wrapper, api } = await mountView(ProfileView)

    await wrapper.findAll('button').find((b) => b.text().includes('Delete my account'))!.trigger('click')
    await settle(1)
    await wrapper.findAll('button').find((b) => b.text() === 'Keep my account')!.trigger('click')
    await settle(1)

    expect(api.delete).not.toHaveBeenCalled()
    expect(textOf(wrapper)).toContain('Delete my account')
  })

  it('deletes the account once confirmed', async () => {
    const { wrapper, api, auth } = await mountView(ProfileView)

    await wrapper.findAll('button').find((b) => b.text().includes('Delete my account'))!.trigger('click')
    await settle(1)
    await wrapper.findAll('button').find((b) => b.text() === 'Delete it')!.trigger('click')
    await settle()

    expect(api.delete).toHaveBeenCalledWith('/auth/me')
    expect(auth.isSignedIn).toBe(false)
    expect(replace).toHaveBeenCalledWith({ name: 'sign-in' })
  })

  it('links to the importer and the conflict list', async () => {
    const { wrapper } = await mountView(ProfileView)

    const targets = wrapper
      .findAllComponents(RouterLinkStub)
      .map((link) => JSON.stringify(link.props().to))
      .join(' ')

    expect(targets).toContain('import')
    expect(targets).toContain('conflicts')
  })
})
