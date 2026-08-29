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

    expect(client.patch).toHaveBeenCalledWith(`/groups/${GROUP_ID}`, { name: 'Flatmates' })
    expect(textOf(wrapper)).toContain('Saved')
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

  it('adds a person by name', async () => {
    const client = api()
    const { wrapper } = await mountView(GroupSettingsView, { api: client })

    const inputs = wrapper.findAll('input[type="text"]')
    await inputs[inputs.length - 1].setValue('Carol')
    await wrapper.findAll('button').find((b) => b.text() === 'Add')!.trigger('click')
    await settle()

    expect(client.post).toHaveBeenCalledWith(`/groups/${GROUP_ID}/members`, {
      displayName: 'Carol',
    })
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

    expect(replace).toHaveBeenCalledWith({ name: 'groups' })
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
