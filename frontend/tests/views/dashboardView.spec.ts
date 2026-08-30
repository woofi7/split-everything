import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import DashboardView from '@/views/DashboardView.vue'
import {
  ALICE,
  BOB,
  GROUP_ID,
  fakeApi,
  mountView,
  settle,
  testExpense,
  testGroup,
  textOf,
} from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

const summaries = (...groups: ReturnType<typeof testGroup>[]) =>
  fakeApi({ '/groups': () => groups })

describe('DashboardView', () => {
  it('lists the groups it loaded', async () => {
    const { wrapper } = await mountView(DashboardView, { api: summaries(testGroup()) })

    expect(textOf(wrapper)).toContain('Roommates')
  })

  it('shows the total across groups', async () => {
    const { wrapper } = await mountView(DashboardView, {
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
    const { wrapper } = await mountView(DashboardView, {
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
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => [] }),
      groups: [],
    })

    expect(textOf(wrapper)).toContain('No groups yet')
    expect(textOf(wrapper)).toContain('Create a group')
  })

  it('says it is offline when the refresh failed but shows the cache', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(DashboardView, { api })

    // The cached group still renders: that is the whole point of the local replica.
    expect(textOf(wrapper)).toContain('Roommates')
    expect(textOf(wrapper)).toContain('Offline')
  })

  it('hides archived groups until asked', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: summaries(testGroup({ isArchived: true })),
      groups: [],
    })

    expect(textOf(wrapper)).toContain('Show archived groups')
    expect(textOf(wrapper)).not.toContain('Roommates')
  })

  it('reveals archived groups when the toggle is used', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: summaries(testGroup({ isArchived: true })),
      groups: [],
    })

    await wrapper.find('button[type="button"]').trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('Roommates')
    expect(textOf(wrapper)).toContain('Hide archived groups')
  })

  it('does not offer the archive toggle when nothing is archived', async () => {
    const { wrapper } = await mountView(DashboardView, { api: summaries(testGroup()) })

    expect(textOf(wrapper)).not.toContain('archived groups')
  })

  it('links each group to its detail screen', async () => {
    const { wrapper } = await mountView(DashboardView, { api: summaries(testGroup()) })

    const links = wrapper.findAllComponents(RouterLinkStub)
    expect(links.some((link) => JSON.stringify(link.props().to).includes(GROUP_ID))).toBe(true)
  })
})

describe('DashboardView spending pie', () => {
  it('shows who paid in the main group, not which group', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [
        testExpense({ id: 'e1', paidByMemberId: ALICE, amount: 60, amountInBaseCurrency: 60 }),
        testExpense({ id: 'e2', paidByMemberId: BOB, amount: 40, amountInBaseCurrency: 40 }),
      ],
    })
    await settle()

    // The question a shared account has is who has been paying.
    const text = textOf(wrapper)
    expect(text).toContain('Who paid in Roommates')
    expect(text).toContain('Alice')
    expect(text).toContain('60%')
    expect(text).toContain('40%')
  })

  it('says nothing has been spent rather than drawing an empty circle', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Nothing spent yet')
  })

  it('leaves the pie out entirely when there are no groups', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => [] }),
      groups: [],
    })

    expect(wrapper.find('svg[role="img"]').exists()).toBe(false)
  })

  it('offers a way to change which group the app is on', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({
        '/groups': () => [
          { ...testGroup(), id: 'group-1', name: 'Roommates' },
          { ...testGroup(), id: 'group-2', name: 'Ski trip' },
        ],
      }),
      groups: [],
    })
    await settle()

    const switcher = wrapper.find('select[aria-label="Which group the app is on"]')
    expect(switcher.exists()).toBe(true)
    expect(switcher.text()).toContain('Ski trip')
  })
})
