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
  settle,
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

  /**
   * One save for the whole screen.
   *
   * The name, the icon and the default split are settings: edited and then kept,
   * so they are saved together, once, in one request. Adding a person, changing a
   * colour and creating an invite are actions, and stay immediate.
   */
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

      // The settings it covers run down a long page, and a button that has
      // scrolled away cannot answer "I changed something".
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

      // One PATCH: the group's fields and how it splits are the same endpoint, so
      // there is no reason for two round trips or for one to succeed alone.
      expect(client.patch).toHaveBeenCalledTimes(1)
      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}`,
        expect.objectContaining({ name: 'Flatmates', defaultSplitType: 'Shares' }),
      )
    })

    it('goes away once it is saved', async () => {
      const client = api()
      const { wrapper } = await mountView(GroupSettingsView, { api: client })
      await settle()

      await wrapper.find('input[type="text"]').setValue('Flatmates')
      await settle(1)
      await wrapper.find('[data-testid="save-settings"]').trigger('click')
      await settle()

      // The fake echoes the group back unchanged, so this also pins that the bar
      // follows the group rather than a flag of its own.
      expect(textOf(wrapper)).toContain('Saved')
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

    // One save for the whole screen, so nothing on it commits on its own.
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

    // Removing the last owner would leave the group unmanageable.
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

    // A real membership, not a placeholder that has to be claimed later.
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
    // Names repeat: a group can hold two people called Nicolas, and only one of
    // them is the one reading the list.
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

    // Exactly one: matching on the name would have tagged both.
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
    // Both notes at once read as a puzzle. Removed is the fact that matters.
    expect(row.text()).toContain('(removed)')
    expect(row.text()).not.toContain('not signed in yet')
  })

  it('marks who owns the group', async () => {
    const { wrapper } = await mountView(GroupSettingsView, { api: api() })

    const rows = wrapper.findAll('li')
    const owner = rows.find((row) => row.find('[data-testid="owner-tag"]').exists())

    // The one person no merge or removal can take out of the group, and the only
    // one who can do either.
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

    // An invite link is the only other way in, and the person accepts it
    // themselves, so the group never holds someone who cannot open it.
    expect(wrapper.find('[data-testid="add-placeholder"]').exists()).toBe(false)
    expect(client.post).not.toHaveBeenCalledWith(
      `/groups/${GROUP_ID}/members`,
      expect.anything(),
    )
  })

  it('does not offer people already in the group', async () => {
    // The server filters them out; this pins that the view asks per group rather
    // than for a global directory.
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

    // The clipboard API needs a secure context, so the link has to be readable
    // on its own or a plain HTTP session cannot share an invite at all.
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

  /**
   * Folding two people into one.
   *
   * The same person can be in a group twice: a name a CSV import invented, and
   * the account they later signed up with. Both halves carry expenses, so neither
   * can just be deleted.
   *
   * One action asking for a pair, rather than something done to a row: who goes
   * and who inherits reads as one sentence, which is what makes it hard to get
   * backwards. Everything still works afterwards, which is exactly why a mistake
   * is invisible, so the warning is part of the feature.
   */
  describe('merging two people', () => {
    /** A group holding one person twice, which is what a merge is for. */
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
      // Icon only, so the name has to be carried for anyone who cannot see it.
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

      // One of the two is not a merge.
      expect(wrapper.find('[data-testid="merge-confirm-button"]').attributes('disabled'))
        .toBeDefined()
    })

    it('offers a removed member as the one to merge away', async () => {
      const { wrapper } = await openMerge()

      // Removing a member deactivates it rather than deleting it, because it
      // still holds expenses. That leftover is the most likely thing to merge.
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

      // Both are called Emma. Without this the list is two identical rows.
      expect(labels.some((label) => label.includes('Emma (removed)'))).toBe(true)
    })

    it('does not offer a removed member as the one to keep', async () => {
      const { wrapper } = await openMerge()

      const options = wrapper
        .findAll('[data-testid="merge-target"] option')
        .map((option) => option.attributes('value'))

      // Everything ends up on the target, so a member nobody can see would put
      // the history out of sight.
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

    it('reports a refusal in the dialog, where the eye already is', async () => {
      const { wrapper, client } = await openMerge()
      client.post.mockRejectedValue(new Error('The group owner cannot be merged away.'))

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await wrapper.find('[data-testid="merge-target"]').setValue('member-emma')
      await wrapper.find('[data-testid="merge-confirm-button"]').trigger('click')
      await settle()

      // At the foot of the section it read as nothing having happened at all.
      expect(wrapper.find('[data-testid="merge-confirm"]').text())
        .toContain('cannot be merged away')
    })

    it('reports it once, not in two places', async () => {
      const { wrapper, client } = await openMerge()
      client.post.mockRejectedValue(new Error('The group owner cannot be merged away.'))

      await wrapper.find('[data-testid="merge-source"]').setValue(BOB)
      await wrapper.find('[data-testid="merge-target"]').setValue('member-emma')
      await wrapper.find('[data-testid="merge-confirm-button"]').trigger('click')
      await settle()

      const alerts = wrapper
        .findAll('[role="alert"]')
        .filter((node) => node.text().includes('cannot be merged away'))

      expect(alerts).toHaveLength(1)
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

      // It rewrites everyone's balances, so the server allows only an owner or an
      // admin. Offering the button anyway would end in a refusal.
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

  /**
   * How the group splits an expense by default.
   *
   * A fact about the household rather than about one expense, so it belongs on the
   * group's own screen. It was only settable as a side effect of adding an
   * expense, which meant you could not see what it was.
   */
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

      // Blank boxes make the person do arithmetic the app already knows.
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
      // The one save refuses while any setting on the screen is wrong.
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
      // A group that already splits by shares, so choosing equal is a change.
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

      // Back where it started, so there is nothing to save and no button offering.
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

      // The server allows only an owner or an admin, so the control would end in a
      // refusal. The setting is still shown, because knowing it is not a privilege.
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

      // An exact amount is a fact about one expense, so it is not offered as a
      // standing rule, but a group already on it is not quietly rewritten.
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

  /**
   * A member's colour, which belongs to the group.
   *
   * It is what the expense cards, the balances and the charts all draw with, so
   * seeing it and changing it belongs where the group is described.
   */
  describe('a member colour', () => {
    /** A group whose colours the server has stored. */
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

    it('saves the colour that is picked', async () => {
      const { wrapper, client } = await open(ALICE)

      await wrapper.find('[data-testid="colour-14b8a6"]').trigger('click')
      await settle()

      expect(client.patch).toHaveBeenCalledWith(
        `/groups/${GROUP_ID}/members/${ALICE}/color`,
        { colorHex: '#14b8a6' },
      )
    })

    it('says what happens to a colour someone else has', async () => {
      const { wrapper } = await open(ALICE)

      // Swapped rather than refused, and worth saying before the tap.
      expect(textOf(wrapper)).toContain('swaps the two')
    })

    it('reports a refusal', async () => {
      const { wrapper, client } = await open(ALICE)
      client.patch.mockRejectedValue(new Error('Only an owner or an admin can change'))

      await wrapper.find('[data-testid="colour-14b8a6"]').trigger('click')
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

      // ALICE is the signed-in member in the harness; BOB is somebody else.
      expect(wrapper.find(`[data-testid="recolour-${ALICE}"]`).attributes('disabled'))
        .toBeUndefined()
      expect(wrapper.find(`[data-testid="recolour-${BOB}"]`).attributes('disabled'))
        .toBeDefined()
    })
  })
})
