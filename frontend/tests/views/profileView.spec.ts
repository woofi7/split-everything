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

  /**
   * One save, one way back.
   *
   * The same shape as the group's settings, because these are settings too: edited
   * and then kept. The theme is not among them, since it applies as it is switched
   * and there is nothing to preview.
   */
  describe('saving the profile', () => {
    it('offers nothing while nothing has changed', async () => {
      const { wrapper } = await mountView(ProfileView)

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })

    it('appears on the first change', async () => {
      const { wrapper } = await mountView(ProfileView)

      await wrapper.find('input[type="text"]').setValue('Alice A')
      await settle(1)

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="cancel-changes"]').exists()).toBe(true)
    })

    it('saves the name and the currency together', async () => {
      const api = fakeApi({ '/auth/me': () => testUser })
      const { wrapper } = await mountView(ProfileView, { api })

      await wrapper.find('input[type="text"]').setValue('Alice A')
      await wrapper.find('select').setValue('EUR')
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(api.patch).toHaveBeenCalledTimes(1)
      expect(api.patch).toHaveBeenCalledWith('/auth/me', expect.objectContaining({
        displayName: 'Alice A',
        defaultCurrency: 'EUR',
      }))
    })

    it('puts everything back when cancelled', async () => {
      const api = fakeApi()
      const { wrapper } = await mountView(ProfileView, { api })

      await wrapper.find('input[type="text"]').setValue('Alice A')
      await wrapper.find('select').setValue('EUR')
      await settle(1)
      await wrapper.find('[data-testid="cancel-changes"]').trigger('click')
      await settle(1)

      expect((wrapper.find('input[type="text"]').element as HTMLInputElement).value).toBe('Alice')
      expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('CAD')
      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
      expect(api.patch).not.toHaveBeenCalled()
    })

    it('leaves the theme alone, which is applied as it is switched', async () => {
      const { wrapper, auth } = await mountView(ProfileView)

      await wrapper.find('[data-testid="theme-toggle"]').trigger('click')
      await settle()

      expect(auth.theme).toBe('light')
      // Nothing to save: there is no preview of a theme.
      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })
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
   * The colour the whole application wears.
   *
   * On the account rather than on the device, because somebody who picks a colour
   * means it wherever they sign in. There is no personal colour on this screen any
   * more: a member's colour belongs to the group, and that is where it is edited.
   */
  describe('the app colour', () => {
    it('offers the eight themes', async () => {
      const { wrapper } = await mountView(ProfileView)

      expect(wrapper.findAll('[data-testid^="accent-"]')).toHaveLength(8)
    })

    it('starts on the default when nobody has said', async () => {
      const { wrapper } = await mountView(ProfileView)

      const pressed = wrapper
        .findAll('[data-testid^="accent-"]')
        .filter((swatch) => swatch.attributes('aria-pressed') === 'true')
        .map((swatch) => swatch.attributes('data-testid'))

      expect(pressed).toEqual(['accent-indigo'])
    })

    it('wears the one that is tapped straight away', async () => {
      const api = fakeApi({ '/auth/me': () => ({ ...testUser, themeName: 'teal' }) })
      const { wrapper, auth } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="accent-teal"]').trigger('click')
      await settle()

      // The whole application changes colour on the tap: a Save button over a
      // change you are already looking at would have nothing to do.
      expect(auth.accent.name).toBe('teal')
      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })

    it('tells the account, so it follows onto another device', async () => {
      const api = fakeApi({ '/auth/me': () => ({ ...testUser, themeName: 'rose' }) })
      const { wrapper } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="accent-rose"]').trigger('click')
      await settle()

      expect(api.patch).toHaveBeenCalledWith(
        '/auth/me',
        expect.objectContaining({ themeName: 'rose' }),
      )
    })

    it('keeps the colour on when the server cannot be told', async () => {
      const api = fakeApi()
      api.patch.mockRejectedValue(new Error('offline'))
      const { wrapper, auth } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="accent-amber"]').trigger('click')
      await settle()

      // The same bargain the light switch makes: it is a preference, not a
      // transaction, and it is already on screen.
      expect(auth.accent.name).toBe('amber')
      expect(textOf(wrapper)).not.toContain('offline')
    })

    it('shows the one the account already wears', async () => {
      const { wrapper, auth } = await mountView(ProfileView)
      auth.user = { ...testUser, themeName: 'violet' } as never
      await settle(1)

      expect(wrapper.find('[data-testid="accent-violet"]').attributes('aria-pressed')).toBe('true')
      expect(wrapper.find('[data-testid="accent-indigo"]').attributes('aria-pressed')).toBe('false')
    })

    it('has no colour of your own on it any more', async () => {
      const { wrapper } = await mountView(ProfileView)

      // A member's colour belongs to the group, and is edited in its settings.
      expect(wrapper.findAll('[data-testid^="colour-"]')).toHaveLength(0)
      expect(textOf(wrapper)).not.toContain('Your colour')
    })
  })

  /**
   * The language the app is read in.
   *
   * On the account like the colour, applied on the tap for the same reason: the
   * screen is the confirmation, and there is nothing left to preview.
   */
  describe('the language', () => {
    it('offers English and French, each named in itself', async () => {
      const { wrapper } = await mountView(ProfileView)

      expect(wrapper.find('[data-testid="language-en"]').text()).toBe('English')
      expect(wrapper.find('[data-testid="language-fr"]').text()).toBe('Francais')
    })

    it('starts on English', async () => {
      const { wrapper } = await mountView(ProfileView)

      expect(wrapper.find('[data-testid="language-en"]').attributes('aria-pressed')).toBe('true')
      expect(wrapper.find('[data-testid="language-fr"]').attributes('aria-pressed')).toBe('false')
    })

    it('reads in French from the tap, and tells the account', async () => {
      const api = fakeApi({ '/auth/me': () => ({ ...testUser, locale: 'fr' }) })
      const { wrapper, auth } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="language-fr"]').trigger('click')
      await settle()

      expect(auth.language).toBe('fr')
      expect(api.patch).toHaveBeenCalledWith(
        '/auth/me',
        expect.objectContaining({ locale: 'fr' }),
      )
      // Nothing to save: the whole screen has already changed language.
      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })

    it('keeps the language when the account cannot be told', async () => {
      const api = fakeApi()
      api.patch.mockRejectedValue(new Error('offline'))
      const { wrapper, auth } = await mountView(ProfileView, { api })

      await wrapper.find('[data-testid="language-fr"]').trigger('click')
      await settle()

      expect(auth.language).toBe('fr')
    })

    it('shows the language the account already reads in', async () => {
      const { wrapper, auth } = await mountView(ProfileView)
      auth.user = { ...testUser, locale: 'fr' } as never
      await settle(1)

      expect(wrapper.find('[data-testid="language-fr"]').attributes('aria-pressed')).toBe('true')
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
