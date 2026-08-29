import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import GroupsView from '@/views/GroupsView.vue'
import { GROUP_ID, fakeApi, mountView, settle, testGroup, textOf } from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

const summaries = (...groups: ReturnType<typeof testGroup>[]) =>
  fakeApi({ '/groups': () => groups })

describe('GroupsView', () => {
  it('lists the groups it loaded', async () => {
    const { wrapper } = await mountView(GroupsView, { api: summaries(testGroup()) })

    expect(textOf(wrapper)).toContain('Roommates')
  })

  it('shows the total across groups', async () => {
    const { wrapper } = await mountView(GroupsView, {
      api: summaries(
        testGroup({ id: 'a', name: 'A', myNetBalance: 50 }),
        testGroup({ id: 'b', name: 'B', myNetBalance: -20 }),
      ),
      groups: [],
    })

    expect(textOf(wrapper)).toContain('Across all groups')
    expect(textOf(wrapper)).toContain('30.00')
  })

  it('puts a group you owe in above a settled one', async () => {
    const { wrapper } = await mountView(GroupsView, {
      api: summaries(
        testGroup({ id: 'settled', name: 'Settled', myNetBalance: 0 }),
        testGroup({ id: 'owing', name: 'Owing', myNetBalance: -40 }),
      ),
      groups: [],
    })

    const text = textOf(wrapper)
    expect(text.indexOf('Owing')).toBeLessThan(text.indexOf('Settled'))
  })

  it('offers a way to create the first group when there are none', async () => {
    const { wrapper } = await mountView(GroupsView, {
      api: fakeApi({ '/groups': () => [] }),
      groups: [],
    })

    expect(textOf(wrapper)).toContain('No groups yet')
    expect(textOf(wrapper)).toContain('Create a group')
  })

  it('says it is offline when the refresh failed but shows the cache', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(GroupsView, { api })

    // The cached group still renders: that is the whole point of the local replica.
    expect(textOf(wrapper)).toContain('Roommates')
    expect(textOf(wrapper)).toContain('Offline')
  })

  it('hides archived groups until asked', async () => {
    const { wrapper } = await mountView(GroupsView, {
      api: summaries(testGroup({ isArchived: true })),
      groups: [],
    })

    expect(textOf(wrapper)).toContain('Show archived groups')
    expect(textOf(wrapper)).not.toContain('Roommates')
  })

  it('reveals archived groups when the toggle is used', async () => {
    const { wrapper } = await mountView(GroupsView, {
      api: summaries(testGroup({ isArchived: true })),
      groups: [],
    })

    await wrapper.find('button[type="button"]').trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('Roommates')
    expect(textOf(wrapper)).toContain('Hide archived groups')
  })

  it('does not offer the archive toggle when nothing is archived', async () => {
    const { wrapper } = await mountView(GroupsView, { api: summaries(testGroup()) })

    expect(textOf(wrapper)).not.toContain('archived groups')
  })

  it('links each group to its detail screen', async () => {
    const { wrapper } = await mountView(GroupsView, { api: summaries(testGroup()) })

    const links = wrapper.findAllComponents(RouterLinkStub)
    expect(links.some((link) => JSON.stringify(link.props().to).includes(GROUP_ID))).toBe(true)
  })
})
