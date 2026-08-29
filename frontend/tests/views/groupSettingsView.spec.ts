import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import GroupSettingsView from '@/views/GroupSettingsView.vue'
import {
  BOB,
  GROUP_ID,
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

  it('saves as soon as an icon is chosen', async () => {
    const client = api()
    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('button[data-icon]').trigger('click')
    await settle(1)
    await wrapper.find('[data-icon="car"]').trigger('click')
    await settle()

    // There is no other reason to be on this screen, so an extra Save press
    // would just be a step to forget.
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

  it('still adds someone with no account, by name', async () => {
    const client = api({ '/users/addable': () => [] })

    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    await wrapper.find('input[type="search"]').setValue('Dave')
    await settle()
    await wrapper.find('[data-testid="add-placeholder"]').trigger('click')
    await settle()

    expect(client.post).toHaveBeenCalledWith(
      `/groups/${GROUP_ID}/members`,
      { displayName: 'Dave' },
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
})
