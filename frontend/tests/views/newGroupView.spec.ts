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

    const memberInput = wrapper.find('input[placeholder="Bob"]')
    await memberInput.setValue('Bob')
    const addButton = wrapper.findAll('button').find((button) => button.text() === 'Add')
    await addButton!.trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('Bob')
  })

  it('adds a person on the enter key', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    const memberInput = wrapper.find('input[placeholder="Bob"]')
    await memberInput.setValue('Carol')
    await memberInput.trigger('keydown.enter')
    await settle(1)

    expect(textOf(wrapper)).toContain('Carol')
  })

  it('ignores a blank name', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    const memberInput = wrapper.find('input[placeholder="Bob"]')
    await memberInput.setValue('   ')
    const addButton = wrapper.findAll('button').find((button) => button.text() === 'Add')
    await addButton!.trigger('click')
    await settle(1)

    expect(wrapper.findAll('[aria-label="People added so far"] li')).toHaveLength(0)
  })

  it('does not add the same person twice', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    const memberInput = wrapper.find('input[placeholder="Bob"]')
    const addButton = wrapper.findAll('button').find((button) => button.text() === 'Add')

    for (const name of ['Bob', 'bob']) {
      await memberInput.setValue(name)
      await addButton!.trigger('click')
      await settle(1)
    }

    expect(wrapper.findAll('[aria-label="People added so far"] li')).toHaveLength(1)
  })

  it('removes a person again', async () => {
    const { wrapper } = await mountView(NewGroupView, { groups: [] })

    const memberInput = wrapper.find('input[placeholder="Bob"]')
    await memberInput.setValue('Bob')
    const addButton = wrapper.findAll('button').find((button) => button.text() === 'Add')
    await addButton!.trigger('click')
    await settle(1)

    await wrapper.find('button[aria-label="Remove Bob"]').trigger('click')
    await settle(1)

    expect(wrapper.findAll('[aria-label="People added so far"] li')).toHaveLength(0)
  })

  it('sends the collected people with the group', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    const { wrapper } = await mountView(NewGroupView, { api, groups: [] })

    await wrapper.find('input[placeholder="Roommates"]').setValue('Roommates')
    const memberInput = wrapper.find('input[placeholder="Bob"]')
    await memberInput.setValue('Bob')
    const addButton = wrapper.findAll('button').find((button) => button.text() === 'Add')
    await addButton!.trigger('click')
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
