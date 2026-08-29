import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import NewGroupView from '@/views/NewGroupView.vue'
import { fakeApi, mountView, settle, testGroup, textOf } from '../support/viewHarness'

const replace = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

describe('NewGroupView', () => {
  /**
   * Adds someone with no account, through the field a person would use: type a
   * name nobody matches, then take the fallback.
   */
  async function addByName(wrapper: { find: (s: string) => any }, name: string) {
    await wrapper.find('input[type="search"]').setValue(name)
    await settle(1)
    const fallback = wrapper.find('[data-testid="add-placeholder"]')
    if (fallback.exists()) await fallback.trigger('click')
    await settle(1)
  }

  it('creates the group and opens it', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/groups',
      expect.objectContaining({ name: 'Roommates', baseCurrency: 'CAD' }),
    )
    expect(replace).toHaveBeenCalledWith({
      name: 'group',
      params: { groupId: testGroup().id },
    })
  })

  it('adds people who already have an account when the group is created', async () => {
    const api = fakeApi({
      '/groups': () => testGroup(),
      '/users/addable': () => [
        { id: 'user-bob', displayName: 'Bob Brown', email: 'bob@example.com', avatarUrl: null },
      ],
    })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await wrapper.find('input[type="search"]').setValue('bob')
    await settle()
    await wrapper.find('[data-testid="candidate"]').trigger('click')
    await settle()
    await wrapper.find('form').trigger('submit')
    await settle()

    // The group has to exist before anyone can be added to it, so this is a
    // second call rather than part of the create.
    expect(api.post).toHaveBeenCalledWith(
      `/groups/${testGroup().id}/members/user`,
      { userId: 'user-bob' },
    )
  })

  it('asks for everyone with an account, since there is no group yet', async () => {
    const api = fakeApi({ '/groups': () => testGroup(), '/users/addable': () => [] })

    await mountView(NewGroupView, { api, groups: [] })
    await settle()

    expect(api.get).toHaveBeenCalledWith('/users/addable', undefined)
  })

  it('still opens the group when adding someone to it fails', async () => {
    const api = fakeApi({
      '/groups': () => testGroup(),
      '/users/addable': () => [
        { id: 'user-bob', displayName: 'Bob Brown', email: 'bob@example.com', avatarUrl: null },
      ],
    })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await wrapper.find('input[type="search"]').setValue('bob')
    await settle()
    await wrapper.find('[data-testid="candidate"]').trigger('click')
    await settle()

    // The group is already created at this point. Stranding the person on this
    // screen would leave them unsure whether it exists.
    api.post.mockImplementation(async (path: string) =>
      path.endsWith('/members/user') ? Promise.reject(new Error('nope')) : testGroup(),
    )

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(replace).toHaveBeenCalledWith({
      name: 'group',
      params: { groupId: testGroup().id },
    })
  })

  it('refuses a group with no name', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('form').trigger('submit')
    await settle(1)

    expect(textOf(wrapper)).toContain('Give the group a name')
    expect(api.post).not.toHaveBeenCalled()
  })

  it('collects people by name before the group exists', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    await addByName(wrapper, 'Bob')

    expect(textOf(wrapper)).toContain('Bob')
  })

  it('adds a person on the enter key', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    const memberInput = wrapper.find('input[type="search"]')
    await memberInput.setValue('Carol')
    await settle(1)
    await memberInput.trigger('keydown', { key: 'Enter' })
    await settle(1)

    expect(textOf(wrapper)).toContain('Carol')
  })

  it('ignores a blank name', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    await addByName(wrapper, '   ')

    expect(wrapper.findAll('[aria-label="People added so far"] li')).toHaveLength(0)
  })

  it('does not add the same person twice', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    for (const name of ['Bob', 'bob']) await addByName(wrapper, name)

    expect(wrapper.findAll('[aria-label="People added so far"] li')).toHaveLength(1)
  })

  it('removes a person again', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    await addByName(wrapper, 'Bob')

    await wrapper.find('button[aria-label="Remove Bob"]').trigger('click')
    await settle(1)

    expect(wrapper.findAll('[aria-label="People added so far"] li')).toHaveLength(0)
  })

  it('sends the collected people with the group', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await addByName(wrapper, 'Bob')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/groups',
      expect.objectContaining({ placeholderMemberNames: ['Bob'] }),
    )
  })

  it('opens the icon picker rather than asking someone to type a name', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)

    await wrapper.findAll('button').find((button) => button.text().includes('Choose'))!.trigger('click')
    await settle(1)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
  })

  it('shows the chosen icon on the button', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    await wrapper.findAll('button').find((button) => button.text().includes('Choose'))!.trigger('click')
    await settle(1)
    await wrapper.find('[data-icon="house"]').trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('House')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('sends the chosen icon with the group', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await wrapper.findAll('button').find((button) => button.text().includes('Choose'))!.trigger('click')
    await settle(1)
    await wrapper.find('[data-icon="house"]').trigger('click')
    await settle(1)
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/groups',
      expect.objectContaining({ iconName: 'house' }),
    )
  })

  it('sends no icon when none was chosen', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.post).toHaveBeenCalledWith('/groups', expect.objectContaining({ iconName: null }))
  })

  it('offers the currencies a homelab is likely to need', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    const options = wrapper.find('select').findAll('option').map((option) => option.text())
    expect(options).toContain('CAD')
    expect(options).toContain('EUR')
    expect(options).toContain('JPY')
  })

  it('surfaces a server refusal', async () => {
    const api = fakeApi()
    api.post.mockRejectedValue(new Error('Group name must be at most 120 characters.'))

    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })
    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(textOf(wrapper)).toContain('at most 120 characters')
  })
})
