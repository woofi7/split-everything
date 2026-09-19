import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import GroupSettingsView from '@/views/GroupSettingsView.vue'
import {
  ALICE,
  BOB,
  GROUP_ID,
  USER_ID,
  fakeApi,
  mountView,
  saidOnScreen,
  settle,
  testExpense,
  testGroup,
  textOf,
} from '../support/viewHarness'

const replace = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { groupId: GROUP_ID }, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

const api = (overrides: Record<string, unknown> = {}) =>
  fakeApi({
    '/groups': () => testGroup(),
    ...overrides,
  })

describe('GroupSettingsView', () => {
  it('prefills the group name', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    expect((wrapper.find('input[type="text"]').element as HTMLInputElement).value).toBe('Roommates')
  })

  describe('saving the settings', () => {
    it('offers nothing while nothing has changed', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })

    it('appears as soon as something changes', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()

      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await settle(1)

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(true)
    })

    it('sits over the page rather than in it', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()
      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await settle(1)

      const bar = wrapper.find('[data-testid="save-bar"]')
      expect(bar.classes()).toContain('fixed')
      expect(bar.classes()).toContain('right-4')
    })

    it('saves every setting in one request', async () => {
      const client = api()
      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()

      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await wrapper.find('[data-testid="split-Shares"]').setValue(true)
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(client.patch).toHaveBeenCalledTimes(1)
      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}`,
        expect.objectContaining({ name: 'Flatmates', defaultSplitType: 'Shares' }),
      )
    })

    describe('the group colour', () => {
      it('saves the colour with the rest of the settings', async () => {
        const client = api()
        const { wrapper } = await mountView(GroupSettingsView, { api: client })
        await settle()

        await wrapper.find('[data-testid="accent-teal"]').trigger('click')
        await settle(1)
        await wrapper.find('[data-testid="save-settings"]').trigger('click')
        await settle()

        expect(client.patch).toHaveBeenCalledWith(
          `/groups/${GROUP_ID}`,
          expect.objectContaining({ themeName: 'teal' }),
        )
      })

      it('shows the one the group is already wearing', async () => {
        const { wrapper } = await mountView(GroupSettingsView, {
          api: api({ '/groups': () => testGroup({ themeName: 'amber' }) }),
          groups: [testGroup({ themeName: 'amber' })],
        })
        await settle()

        expect(wrapper.find('[data-testid="accent-amber"]').attributes('aria-pressed')).toBe('true')
      })

      it('gives the group back to whatever colour each person chose', async () => {
        const client = api({ '/groups': () => testGroup({ themeName: 'amber' }) })
        const { wrapper } = await mountView(GroupSettingsView, {
          api: client,
          groups: [testGroup({ themeName: 'amber' })],
        })
        await settle()

        await wrapper.find('[data-testid="clear-group-colour"]').trigger('click')
        await settle(1)
        await wrapper.find('[data-testid="save-settings"]').trigger('click')
        await settle()

        expect(client.patch).toHaveBeenCalledWith(
          `/groups/${GROUP_ID}`,
          expect.objectContaining({ themeName: '' }),
        )
      })

      it('offers nothing to clear when the group has no colour of its own', async () => {
        const { wrapper } = await mountView(GroupSettingsView, { api: api() })
        await settle()

        expect(wrapper.find('[data-testid="clear-group-colour"]').exists()).toBe(false)
      })
    })

    it('goes away once it is saved', async () => {
      const client = api()
      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()

      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(textOf(wrapper)).toContain('Saved')
    })

    it('puts every setting back when cancelled', async () => {
      const client = api()
      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()

      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await wrapper.find('[data-testid="split-Shares"]').setValue(true)
      await settle(1)
      await wrapper.find(`[data-testid="recolour-${ALICE}"]`).trigger('click')
      await settle(1)
      await wrapper.find('[data-testid="colour-14b8a6"]').trigger('click')
      await settle(1)

      await wrapper.find('[data-testid="cancel-changes"]').trigger('click')
      await settle(1)

      expect((wrapper.find('input[type="text"]').element as HTMLInputElement).value)
        .toBe('Roommates')
      expect(wrapper.find('[data-testid="split-Equal"]').attributes('checked')).toBeDefined()
      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
      expect(client.patch).not.toHaveBeenCalled()
    })

    it('is not offered to someone who cannot change anything', async () => {
      const group = testGroup()
      group.members = group.members.map((member) =>
        member.role === 'Owner' ? { ...member, role: 'Member' } : member,
      )

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => group }),
        groups: [group],
      })
      await settle()
      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await settle(1)

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })
  })

  it('renames the group', async () => {
    const client = api()
    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('input[type="text"]').setValue('Flatmates')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(client.patch).toHaveBeenCalledWith(
      `/groups/${GROUP_ID}`,
      expect.objectContaining({ name: 'Flatmates' }),
    )
    expect(textOf(wrapper)).toContain('Saved')
  })

  it('shows the group icon and opens the picker', async () => {
    const { wrapper } = await mountView(GroupSettingsView, {
      api: fakeApi({ '/groups': () => testGroup({ iconName: 'house' }) }),
      groups: [testGroup({ iconName: 'house' })],
    })

    expect(wrapper.find('button[data-icon="house"]').exists()).toBe(true)

    await wrapper.find('button[data-icon="house"]').trigger('click')
    await settle(1)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
  })

  it('holds a chosen icon until the settings are saved', async () => {
    const client = api()
    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('button[data-icon]').trigger('click')
    await settle(1)
    await wrapper.find('[data-icon="car"]').trigger('click')
    await settle()

    expect(client.patch).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="save-settings"]').exists()).toBe(true)

    await wrapper.find('[data-testid="save-settings"]').trigger('click')
    await settle()

    expect(client.patch).toHaveBeenCalledWith(
      `/groups/${GROUP_ID}`,
      expect.objectContaining({ iconName: 'car' }),
    )
  })

  it('describes the icon button for a screen reader', async () => {
    const { wrapper } = await mountView(GroupSettingsView, {
      api: fakeApi({ '/groups': () => testGroup({ iconName: 'house' }) }),
      groups: [testGroup({ iconName: 'house' })],
    })

    expect(wrapper.find('button[data-icon="house"]').attributes('aria-label')).toContain('House')
  })

  it('lists the people, marking who has not signed in', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    const text = textOf(wrapper)
    expect(text).toContain('Alice')
    expect(text).toContain('Bob')
    expect(text).toContain('not signed in yet')
  })

  it('marks a removed member', async () => {
    const removed = testGroup()
    removed.members[1] = { ...removed.members[1], status: 'Removed' }

    const { wrapper } = await mountView(GroupSettingsView, {
      api: fakeApi({ '/groups': () => removed }),
      groups: [removed],
    })

    expect(textOf(wrapper)).toContain('(removed)')
  })

  it('removes a person', async () => {
    const client = api()
    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.findAll('button').find((b) => b.text() === 'Remove')!.trigger('click')
    await settle()

    expect(client.delete).toHaveBeenCalledWith(`/groups/${GROUP_ID}/members/${BOB}`)
  })

  it('does not offer to remove the owner', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    expect(wrapper.findAll('button').filter((b) => b.text() === 'Remove')).toHaveLength(1)
  })

  it('creates an invite and shows its QR code', async () => {
    const client = api({
      '/groups/group-1/invites': () => ({
        id: 'invite-1',
        token: 'plain-token',
        url: 'https://split.test/join/plain-token',
        invitedEmail: null,
        expiresAt: '2026-02-01T00:00:00Z',
        maxUses: 1,
        useCount: 0,
      }),
    })
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:qr', revokeObjectURL: vi.fn() })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.findAll('button').find((b) => b.text() === 'Invite')!.trigger('click')
    await settle()

    expect(client.post).toHaveBeenCalledWith(
      `/groups/${GROUP_ID}/invites`,
      expect.objectContaining({ maxUses: 1, expiresInHours: 72 }),
    )
    expect(wrapper.find('img[alt="Invite QR code"]').exists()).toBe(true)
    vi.unstubAllGlobals()
  })

  it('pins an invite to an address when one is given', async () => {
    const client = api({
      '/groups/group-1/invites': () => ({
        id: 'invite-1',
        token: 't',
        url: 'u',
        invitedEmail: 'bob@example.com',
        expiresAt: '2026-02-01T00:00:00Z',
        maxUses: 1,
        useCount: 0,
      }),
    })
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:qr', revokeObjectURL: vi.fn() })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('input[type="email"]').setValue('bob@example.com')
    await wrapper.findAll('button').find((b) => b.text() === 'Invite')!.trigger('click')
    await settle()

    expect(client.post).toHaveBeenCalledWith(
      `/groups/${GROUP_ID}/invites`,
      expect.objectContaining({ email: 'bob@example.com' }),
    )
    vi.unstubAllGlobals()
  })

  it('adds someone who already has an account', async () => {
    const client = api({
      '/users/addable': () => [
        { id: 'user-bob', displayName: 'Bob Brown', email: 'bob@example.com', avatarUrl: null },
      ],
    })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('input[type="search"]').setValue('bob')
    await settle()
    await wrapper.find('[data-testid="candidate"]').trigger('click')
    await settle()

    expect(client.post).toHaveBeenCalledWith(
      `/groups/${GROUP_ID}/members/user`,
      { userId: 'user-bob' },
    )
  })

  it('marks which of these people is you', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    const rows = wrapper.findAll('li')
    const mine = rows.find((row) => row.text().includes('Alice'))
    const theirs = rows.find((row) => row.text().includes('Bob'))

    expect(mine!.find('[data-testid="you-tag"]').exists()).toBe(true)
    expect(theirs!.find('[data-testid="you-tag"]').exists()).toBe(false)
  })

  it('marks you by membership rather than by name', async () => {
    const twoAlices = {
      ...testGroup(),
      members: [
        { ...testGroup().members[0], id: 'member-other', userId: 'user-other' },
        { ...testGroup().members[0], id: 'member-mine', userId: USER_ID },
      ],
    }

    const { wrapper } = await mountView(GroupSettingsView, {
      api: fakeApi({ '/groups': () => twoAlices }),
      groups: [twoAlices],
    })

    const tagged = wrapper
      .findAll('li')
      .filter((row) => row.find('[data-testid="you-tag"]').exists())

    expect(tagged).toHaveLength(1)
  })

  it('says a removed member is removed, and leaves it at that', async () => {
    const removed = testGroup()
    removed.members[1] = { ...removed.members[1], status: 'Removed' }

    const { wrapper } = await mountView(GroupSettingsView, {
      api: fakeApi({ '/groups': () => removed }),
      groups: [removed],
    })

    const row = wrapper.findAll('li').find((li) => li.text().includes('Bob'))!
    expect(row.text()).toContain('(removed)')
    expect(row.text()).not.toContain('not signed in yet')
  })

  it('marks who owns the group', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    const rows = wrapper.findAll('li')
    const owner = rows.find((row) => row.find('[data-testid="owner-tag"]').exists())

    expect(owner).toBeDefined()
    expect(owner!.text()).toContain('Alice')
  })

  it('marks exactly one owner', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    expect(wrapper.findAll('[data-testid="owner-tag"]')).toHaveLength(1)
  })

  it('cannot add someone who has no account', async () => {
    const client = api({ '/users/addable': () => [] })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('input[type="search"]').setValue('Dave')
    await settle()

    expect(wrapper.find('[data-testid="add-placeholder"]').exists()).toBe(false)
    expect(client.post).not.toHaveBeenCalledWith(
      `/groups/${GROUP_ID}/members`,
      expect.anything(),
    )
  })

  it('does not offer people already in the group', async () => {
    const client = api({ '/users/addable': () => [] })

    await mountView(GroupSettingsView, { api: client })
    await settle()

    expect(client.get).toHaveBeenCalledWith('/users/addable', { groupId: GROUP_ID })
  })

  it('reports a failure to add someone', async () => {
    const client = api({
      '/users/addable': () => [
        { id: 'user-bob', displayName: 'Bob Brown', email: 'bob@example.com', avatarUrl: null },
      ],
    })
    client.post.mockRejectedValue(new Error('That person is already a member.'))

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('input[type="search"]').setValue('bob')
    await settle()
    await wrapper.find('[data-testid="candidate"]').trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('That person is already a member.')
  })

  it('shows the invite link so it can be read without the clipboard', async () => {
    const client = api({
      '/groups/group-1/invites': () => ({
        id: 'invite-1',
        token: 'plain-token',
        url: 'https://split.test/join/plain-token',
        invitedEmail: null,
        expiresAt: '2026-02-01T00:00:00Z',
        maxUses: 1,
        useCount: 0,
      }),
    })
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:qr', revokeObjectURL: vi.fn() })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.findAll('button').find((b) => b.text() === 'Invite')!.trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('https://split.test/join/plain-token')
    vi.unstubAllGlobals()
  })

  it('does not claim to have copied the link when it cannot', async () => {
    const client = api({
      '/groups/group-1/invites': () => ({
        id: 'invite-1',
        token: 'plain-token',
        url: 'https://split.test/join/plain-token',
        invitedEmail: null,
        expiresAt: '2026-02-01T00:00:00Z',
        maxUses: 1,
        useCount: 0,
      }),
    })
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:qr', revokeObjectURL: vi.fn() })
    const clipboard = navigator.clipboard
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })
    await wrapper.findAll('button').find((b) => b.text() === 'Invite')!.trigger('click')
    await settle()

    await wrapper.findAll('button').find((b) => b.text().includes('Copy'))!.trigger('click')
    await settle()

    expect(textOf(wrapper)).not.toContain('Invite link copied')
    expect(textOf(wrapper)).toContain('secure connection')

    Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true })
    vi.unstubAllGlobals()
  })

  it('explains that the link alone grants nothing', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    expect(textOf(wrapper)).toContain('sign in with Google to join')
  })

  it('copies the invite link', async () => {
    const writeText = vi.fn(async () => {})
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })

    const client = api({
      '/groups/group-1/invites': () => ({
        id: 'invite-1',
        token: 't',
        url: 'https://split.test/join/t',
        invitedEmail: null,
        expiresAt: '2026-02-01T00:00:00Z',
        maxUses: 1,
        useCount: 0,
      }),
    })
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:qr', revokeObjectURL: vi.fn() })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })
    await wrapper.findAll('button').find((b) => b.text() === 'Invite')!.trigger('click')
    await settle()
    await wrapper.findAll('button').find((b) => b.text().includes('Copy'))!.trigger('click')
    await settle()

    expect(writeText).toHaveBeenCalledWith('https://split.test/join/t')
    vi.unstubAllGlobals()
  })

  it('archives the group and leaves the list', async () => {
    const client = api({ '/groups/group-1/archive': () => testGroup({ isArchived: true }) })
    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.findAll('button').find((b) => b.text().includes('Archive this group'))!.trigger('click')
    await settle()

    expect(replace).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('offers to reopen an archived group instead', async () => {
    const archived = testGroup({ isArchived: true })
    const { wrapper } = await mountView(GroupSettingsView, {
      api: fakeApi({ '/groups': () => archived }),
      groups: [archived],
    })

    expect(textOf(wrapper)).toContain('Reopen this group')
    expect(textOf(wrapper)).not.toContain('Archive this group')
  })

  it('reopens an archived group', async () => {
    const archived = testGroup({ isArchived: true })
    const client = fakeApi({
      '/groups/group-1/unarchive': () => testGroup({ isArchived: false }),
      '/groups': () => archived,
    })

    const { wrapper } = await mountView(GroupSettingsView, { api: client, groups: [archived] })
    await wrapper.findAll('button').find((b) => b.text().includes('Reopen'))!.trigger('click')
    await settle()

    expect(client.post).toHaveBeenCalledWith(`/groups/${GROUP_ID}/unarchive`)
  })

  it('says archiving deletes nothing', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    expect(textOf(wrapper)).toContain('without deleting anything')
  })

  it('reports a refused invite', async () => {
    const client = api()
    client.post.mockRejectedValue(new Error('This group is archived and cannot be modified.'))

    const { wrapper } = await mountView(GroupSettingsView, { api: client })
    await wrapper.findAll('button').find((b) => b.text() === 'Invite')!.trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('archived')
  })

  describe('merging two people', () => {
    function twiceOver() {
      const group = testGroup()
      group.members = [
        group.members[0],
        { ...group.members[1], displayName: 'Emma', status: 'Removed' },
        { ...group.members[1], id: 'member-emma', displayName: 'Emma', userId: 'user-emma', isPlaceholder: false },
      ]
      return group
    }

    async function openMerge(group = twiceOver()) {
      const client = fakeApi({ '/groups': () => group })
      const mounted = await mountView(GroupSettingsView, { api: client, groups: [group] })

      await mounted.wrapper.find('[data-testid="merge-open"]').trigger('click')
      await settle(1)

      return { ...mounted, client }
    }

    it('is one action rather than a control on every row', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })

      expect(wrapper.find('[data-testid="merge-open"]').exists()).toBe(true)
      expect(wrapper.find(`[data-testid="merge-${BOB}"]`).exists()).toBe(false)
    })

    it('is an icon, so it says what it is without words', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })

      const button = wrapper.find('[data-testid="merge-open"]')
      expect(button.text()).toBe('')
      expect(button.attributes('aria-label')).toBe('Merge two people')
      expect(button.attributes('title')).toBe('Merge two people')
    })

    it('asks who goes and who stays', async () => {
      const { wrapper } = await openMerge()

      expect(wrapper.find('[data-testid="merge-source"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="merge-target"]').exists()).toBe(true)
    })

    it('asks nothing of the server until it is confirmed', async () => {
      const { wrapper, client } = await openMerge()

      expect(wrapper.find('[data-testid="merge-confirm"]').exists()).toBe(true)
      expect(client.post).not.toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/merge`,
        expect.anything(),
      )
    })

    it('says that it cannot be undone before anything is chosen', async () => {
      const { wrapper } = await openMerge()

      expect(wrapper.find('[data-testid="merge-confirm"]').text()).toContain('cannot be undone')
    })

    it('names both people once they are chosen', async () => {
      const { wrapper } = await openMerge()

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await wrapper.find('[data-testid="merge-target"]').setValue('member-emma')
      await settle(1)

      const text = wrapper.find('[data-testid="merge-confirm"]').text()
      expect(text).toContain('will be removed')
      expect(text).toContain('cannot be undone')
    })

    it('will not merge until both are chosen', async () => {
      const { wrapper } = await openMerge()

      expect(wrapper.find('[data-testid="merge-confirm-button"]').attributes('disabled'))
        .toBeDefined()

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await settle(1)

      expect(wrapper.find('[data-testid="merge-confirm-button"]').attributes('disabled'))
        .toBeDefined()
    })

    it('offers a removed member as the one to merge away', async () => {
      const { wrapper } = await openMerge()

      const options = wrapper
        .findAll('[data-testid="merge-source"] option')
        .map((option) => option.attributes('value'))

      expect(options).toContain(BOB)
    })

    it('says which of two people with the same name is the removed one', async () => {
      const { wrapper } = await openMerge()

      const labels = wrapper
        .findAll('[data-testid="merge-source"] option')
        .map((option) => option.text())

      expect(labels.some((label) => label.includes('Emma (removed)'))).toBe(true)
    })

    it('does not offer a removed member as the one to keep', async () => {
      const { wrapper } = await openMerge()

      const options = wrapper
        .findAll('[data-testid="merge-target"] option')
        .map((option) => option.attributes('value'))

      expect(options).not.toContain(BOB)
    })

    it('does not offer the owner as the one to merge away', async () => {
      const { wrapper } = await openMerge()
      const owner = testGroup().members.find((m) => m.role === 'Owner')!

      const options = wrapper
        .findAll('[data-testid="merge-source"] option')
        .map((option) => option.attributes('value'))

      expect(options).not.toContain(owner.id)
    })

    it('does not offer the same person on both sides', async () => {
      const { wrapper } = await openMerge()

      await wrapper.find('[data-testid="merge-source"]').setValue('member-emma')
      await settle(1)

      const options = wrapper
        .findAll('[data-testid="merge-target"] option')
        .map((option) => option.attributes('value'))

      expect(options).not.toContain('member-emma')
    })

    it('merges once confirmed', async () => {
      const { wrapper, client } = await openMerge()

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await wrapper.find('[data-testid="merge-target"]').setValue('member-emma')
      await wrapper.find('[data-testid="merge-confirm-button"]').trigger('click')
      await settle()

      expect(client.post).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/merge`,
        { sourceMemberId: BOB, targetMemberId: 'member-emma' },
      )
    })

    it('can be backed out of', async () => {
      const { wrapper, client } = await openMerge()

      await wrapper.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')
      await settle(1)

      expect(wrapper.find('[data-testid="merge-confirm"]').exists()).toBe(false)
      expect(client.post).not.toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/merge`,
        expect.anything(),
      )
    })

    it('reports a refusal over the top of the screen, and keeps the dialog open', async () => {
      const { wrapper, client } = await openMerge()
      client.post.mockRejectedValue(new Error('The group owner cannot be merged away.'))

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await wrapper.find('[data-testid="merge-target"]').setValue('member-emma')
      await wrapper.find('[data-testid="merge-confirm-button"]').trigger('click')
      await settle()

      expect(saidOnScreen().join(' ')).toContain('cannot be merged away')

      expect(wrapper.find('[data-testid="merge-confirm"]').exists()).toBe(true)
    })

    it('reports it once, not once per attempt', async () => {
      const { wrapper, client } = await openMerge()
      client.post.mockRejectedValue(new Error('The group owner cannot be merged away.'))

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await wrapper.find('[data-testid="merge-target"]').setValue('member-emma')
      await wrapper.find('[data-testid="merge-confirm-button"]').trigger('click')
      await settle()
      await wrapper.find('[data-testid="merge-confirm-button"]').trigger('click')
      await settle()

      expect(saidOnScreen().filter((said) => said.includes('cannot be merged away')))
        .toHaveLength(1)
    })

    it('offers no merge to someone who is only a member', async () => {
      const group = twiceOver()
      group.members = group.members.map((member) =>
        member.role === 'Owner' ? { ...member, role: 'Member' } : member,
      )

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => group }),
        groups: [group],
      })

      expect(wrapper.find('[data-testid="merge-open"]').exists()).toBe(false)
    })

    it('offers no merge in a group with nobody to merge', async () => {
      const alone = testGroup()
      alone.members = [alone.members[0]]

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => alone }),
        groups: [alone],
      })

      expect(wrapper.find('[data-testid="merge-open"]').exists()).toBe(false)
    })
  })

  describe('how a new expense is split', () => {
    it('shows the setting the group already has', async () => {
      const shared = testGroup({ defaultSplitType: 'Shares' })
      shared.defaultSplitValues = { [ALICE]: 2, [BOB]: 1 }

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => shared }),
        groups: [shared],
      })
      await settle()

      expect(wrapper.find('[data-testid="split-Shares"]').attributes('checked')).toBeDefined()
      const values = wrapper.findAll('input[type="number"]').map((input) => (input.element as HTMLInputElement).value)
      expect(values).toContain('2')
      expect(values).toContain('1')
    })

    it('asks for no numbers when it is equal', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()

      expect(wrapper.find('[data-testid="split-Equal"]').attributes('checked')).toBeDefined()
      expect(wrapper.findAll('input[type="number"]')).toHaveLength(0)
    })

    it('seeds a number for everyone when a type needs them', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()

      await wrapper.find('[data-testid="split-Percentage"]').setValue(true)
      await settle(1)

      const values = wrapper.findAll('input[type="number"]').map((input) => (input.element as HTMLInputElement).value)
      expect(values).toHaveLength(2)
      expect(values.every((value) => value === '50')).toBe(true)
    })

    it('refuses percentages that do not add up', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()

      await wrapper.find('[data-testid="split-Percentage"]').setValue(true)
      await settle(1)
      await wrapper.findAll('input[type="number"]')[0].setValue(10)
      await settle(1)

      expect(textOf(wrapper)).toContain('not 100')
      expect(wrapper.find('[data-testid="save-settings"]').attributes('disabled')).toBeDefined()
    })

    it('saves the split the group should use', async () => {
      const client = api()
      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()

      await wrapper.find('[data-testid="split-Shares"]').setValue(true)
      await settle(1)
      await wrapper.findAll('input[type="number"]')[0].setValue(2)
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}`,
        expect.objectContaining({
          defaultSplitType: 'Shares',
          defaultSplitValues: expect.objectContaining({ [ALICE]: 2 }),
        }),
      )
    })

    it('clears the values when going back to equal', async () => {
      const shared = testGroup({ defaultSplitType: 'Shares' })
      shared.defaultSplitValues = { [ALICE]: 2, [BOB]: 1 }
      const client = fakeApi({ '/groups': () => shared })
      const { wrapper } = await mountView(GroupSettingsView, {
        api: client,
        groups: [shared],
      })
      await settle()

      await wrapper.find('[data-testid="split-Equal"]').setValue(true)
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}`,
        expect.objectContaining({ defaultSplitType: 'Equal', defaultSplitValues: {} }),
      )
    })

    it('offers nothing to save when the screen matches the group', async () => {
      const { wrapper } = await mountView(GroupSettingsView, { api: api() })
      await settle()

      await wrapper.find('[data-testid="split-Shares"]').setValue(true)
      await settle(1)
      await wrapper.find('[data-testid="split-Equal"]').setValue(true)
      await settle(1)

      expect(wrapper.find('[data-testid="save-settings"]').exists()).toBe(false)
    })

    it('offers nothing to change to someone who is only a member', async () => {
      const group = testGroup()
      group.members = group.members.map((member) =>
        member.role === 'Owner' ? { ...member, role: 'Member' } : member,
      )

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => group }),
        groups: [group],
      })
      await settle()

      expect(wrapper.find('[data-testid="save-split"]').exists()).toBe(false)
      expect(textOf(wrapper)).toContain('Only an owner or an admin')
      expect(wrapper.find('[data-testid="split-Equal"]').attributes('disabled')).toBeDefined()
    })

    it('says so when the group is set to something it does not offer', async () => {
      const exact = testGroup({ defaultSplitType: 'ExactAmount' })

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => exact }),
        groups: [exact],
      })
      await settle()

      expect(textOf(wrapper)).toContain('Currently set to ExactAmount')
    })

    it('reports a refusal from the server', async () => {
      const client = api()
      client.patch.mockRejectedValue(new Error('Only an admin can change the default split.'))
      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()

      await wrapper.find('[data-testid="split-Shares"]').setValue(true)
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(textOf(wrapper)).toContain('Only an admin can change')
    })
  })

  it('offers a way to file the expenses that came before the categories', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })
    await settle()

    await wrapper.find('[data-testid="categories-toggle"]').trigger('click')
    await settle()

    const link = wrapper.findComponent<InstanceType<typeof RouterLinkStub>>(
      '[data-testid="file-expenses-link"]',
    )
    expect(link.exists()).toBe(true)
    expect(link.props('to')).toEqual({ name: 'file-expenses', params: { groupId: GROUP_ID } })
  })

  describe('names to leave out of the highlights', () => {
    async function openNames(wrapper: { find: (selector: string) => { trigger: (event: string) => Promise<void> } }) {
      await wrapper.find('[data-testid="ignored-names-toggle"]').trigger('click')
      await settle()
    }

    it('shows the patterns the group already has', async () => {
      const shared = testGroup()
      shared.ignoredNamePatterns = ['Loyer', '^Hydro']

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => shared }),
        groups: [shared],
      })
      await settle()
      await openNames(wrapper)

      const values = wrapper
        .findAll('[data-testid="pattern-input"]')
        .map((input) => (input.element as HTMLInputElement).value)

      expect(values).toEqual(['Loyer', '^Hydro'])
    })

    it('says how many expenses a pattern matches', async () => {
      const { wrapper } = await mountView(GroupSettingsView, {
        api: api(),
        expenses: [
          testExpense({ id: 'e1', description: 'Loyer aout' }),
          testExpense({ id: 'e2', description: 'Loyer juillet' }),
          testExpense({ id: 'e3', description: 'Groceries' }),
        ],
      })
      await settle()
      await openNames(wrapper)

      await wrapper.find('[data-testid="add-pattern"]').trigger('click')
      await settle()
      await wrapper.find('[data-testid="pattern-input"]').setValue('Loyer')
      await settle()

      expect(wrapper.find('[data-testid="pattern-matches"]').text()).toContain('2 expenses')
    })

    it('counts a star as anything, which is what people type', async () => {
      const { wrapper } = await mountView(GroupSettingsView, {
        api: api(),
        expenses: [
          testExpense({ id: 'e1', description: 'Loyer aout' }),
          testExpense({ id: 'e2', description: 'Paiement loyer' }),
          testExpense({ id: 'e3', description: 'Groceries' }),
        ],
      })
      await settle()
      await openNames(wrapper)

      await wrapper.find('[data-testid="add-pattern"]').trigger('click')
      await settle()
      await wrapper.find('[data-testid="pattern-input"]').setValue('Loyer*')
      await settle()

      expect(wrapper.find('[data-testid="pattern-matches"]').text()).toContain('1 expense')
    })

    it('saves the patterns on their own, because they are not an admin setting', async () => {
      const put = vi.fn(async () => testGroup())
      const client = api()
      client.put = put

      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()
      await openNames(wrapper)

      await wrapper.find('[data-testid="add-pattern"]').trigger('click')
      await settle()
      await wrapper.find('[data-testid="pattern-input"]').setValue('Loyer')
      await settle()

      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle(2)

      expect(put).toHaveBeenCalledWith(
        expect.stringContaining('/ignored-names'),
        { patterns: ['Loyer'] },
      )
    })

    it('lets an ordinary member set them, and save', async () => {
      const shared = testGroup()
      shared.members = shared.members.map((member) =>
        member.userId === USER_ID ? { ...member, role: 'Member' as const } : member,
      )

      const put = vi.fn(async () => shared)
      const client = fakeApi({ '/groups': () => shared })
      client.put = put

      const { wrapper } = await mountView(GroupSettingsView, { api: client, groups: [shared] })
      await settle()
      await openNames(wrapper)

      await wrapper.find('[data-testid="add-pattern"]').trigger('click')
      await settle()
      await wrapper.find('[data-testid="pattern-input"]').setValue('Loyer')
      await settle()

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(true)

      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle(2)

      expect(put).toHaveBeenCalledWith(
        expect.stringContaining('/ignored-names'),
        { patterns: ['Loyer'] },
      )
      expect(client.patch).not.toHaveBeenCalled()
    })

    it('does not save a row somebody left blank', async () => {
      const patch = vi.fn(async () => testGroup())
      const client = api()
      client.patch = patch

      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()
      await openNames(wrapper)

      await wrapper.find('[data-testid="add-pattern"]').trigger('click')
      await settle()

      expect(wrapper.find('[data-testid="save-settings"]').exists()).toBe(false)
      expect(patch).not.toHaveBeenCalled()
    })
  })

  describe('a member colour', () => {
    function coloured() {
      const group = testGroup()
      group.members = [
        { ...group.members[0], colorHex: '#6366f1' },
        { ...group.members[1], colorHex: '#f97316' },
      ]
      return group
    }

    async function open(memberId: string) {
      const group = coloured()
      const client = fakeApi({ '/groups': () => group })
      const mounted = await mountView(GroupSettingsView, { api: client, groups: [group] })
      await settle()

      await mounted.wrapper.find(`[data-testid="recolour-${memberId}"]`).trigger('click')
      await settle(1)
      return { ...mounted, client }
    }

    it('shows the colour the group stored, not one it worked out', async () => {
      const group = coloured()
      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => group }),
        groups: [group],
      })
      await settle()

      const swatch = wrapper.find(`[data-testid="recolour-${ALICE}"]`)
      expect(swatch.attributes('style')).toContain('rgb(99, 102, 241)')
    })

    it('offers the palette when the swatch is pressed', async () => {
      const { wrapper } = await open(ALICE)

      expect(wrapper.findAll('[data-testid^="colour-"]')).toHaveLength(12)
    })

    it('holds a picked colour until the settings are saved', async () => {
      const { wrapper, client } = await open(ALICE)

      await wrapper.find('[data-testid="colour-14b8a6"]').trigger('click')
      await settle()

      expect(client.patch).not.toHaveBeenCalled()
      expect(wrapper.find(`[data-testid="recolour-${ALICE}"]`).attributes('style'))
        .toContain('rgb(20, 184, 166)')
      expect(wrapper.find('[data-testid="save-settings"]').exists()).toBe(true)
    })

    it('saves the colour with the button', async () => {
      const { wrapper, client } = await open(ALICE)

      await wrapper.find('[data-testid="colour-14b8a6"]').trigger('click')
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/${ALICE}/color`,
        { colorHex: '#14b8a6' },
      )
    })

    it('stops being a change when the stored colour is picked again', async () => {
      const { wrapper } = await open(ALICE)

      await wrapper.find('[data-testid="colour-6366f1"]').trigger('click')
      await settle(1)

      expect(wrapper.find('[data-testid="save-bar"]').exists()).toBe(false)
    })

    it('shows the swap before it is saved', async () => {
      const { wrapper } = await open(ALICE)

      await wrapper.find('[data-testid="colour-f97316"]').trigger('click')
      await settle(1)

      expect(wrapper.find(`[data-testid="recolour-${ALICE}"]`).attributes('style'))
        .toContain('rgb(249, 115, 22)')
      expect(wrapper.find(`[data-testid="recolour-${BOB}"]`).attributes('style'))
        .toContain('rgb(99, 102, 241)')
    })

    it('saves both sides of a swap', async () => {
      const { wrapper, client } = await open(ALICE)

      await wrapper.find('[data-testid="colour-f97316"]').trigger('click')
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/${ALICE}/color`,
        { colorHex: '#f97316' },
      )
      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/${BOB}/color`,
        { colorHex: '#6366f1' },
      )
    })

    it('says what happens to a colour someone else has', async () => {
      const { wrapper } = await open(ALICE)

      expect(textOf(wrapper)).toContain('swaps the two')
    })

    it('reports a refusal', async () => {
      const { wrapper, client } = await open(ALICE)
      client.patch.mockRejectedValue(new Error('Only an owner or an admin can change'))

      await wrapper.find('[data-testid="colour-14b8a6"]').trigger('click')
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      expect(textOf(wrapper)).toContain('Only an owner or an admin')
    })

    it('lets a plain member change their own and nobody else', async () => {
      const group = coloured()
      group.members = [
        { ...group.members[0], role: 'Member' },
        { ...group.members[1], role: 'Member' },
      ]

      const { wrapper } = await mountView(GroupSettingsView, {
        api: fakeApi({ '/groups': () => group }),
        groups: [group],
      })
      await settle()

      expect(wrapper.find(`[data-testid="recolour-${ALICE}"]`).attributes('disabled'))
        .toBeUndefined()
      expect(wrapper.find(`[data-testid="recolour-${BOB}"]`).attributes('disabled'))
        .toBeDefined()
    })
  })
})
