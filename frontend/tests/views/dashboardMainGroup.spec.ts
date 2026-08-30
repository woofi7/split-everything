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

/**
 * The dashboard is one group, not a list of them.
 *
 * The app is used on one group at a time. Opening on a list of every group meant
 * a tap before anything useful, and it duplicated the group screen underneath.
 * The others are reachable through the picker in the header.
 */

const twoGroups = () =>
  fakeApi({
    '/groups': () => [
      { ...testGroup(), id: GROUP_ID, name: 'Roommates' },
      { ...testGroup(), id: 'group-2', name: 'Ski trip' },
    ],
  })

describe('DashboardView on the main group', () => {
  it('names the group it is showing', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    expect(wrapper.find('h1').text()).toContain('Roommates')
  })

  it('does not list the other groups', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    // The whole point: one group at a time.
    expect(textOf(wrapper)).not.toContain('Ski trip')
  })

  it('shows the expenses of that group', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ description: 'Groceries at Metro' })],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Groceries at Metro')
  })

  it('shows the dates of those expenses', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ spentAt: '2026-03-14T12:00:00Z' })],
    })
    await settle()

    expect(textOf(wrapper)).toMatch(/14 Mar|Mar 14/)
  })

  it('shows who paid, as a pie', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [
        testExpense({ id: 'e1', paidByMemberId: ALICE, amount: 60, amountInBaseCurrency: 60 }),
        testExpense({ id: 'e2', paidByMemberId: BOB, amount: 40, amountInBaseCurrency: 40 }),
      ],
    })
    await settle()

    const text = textOf(wrapper)
    expect(text).toContain('60%')
    expect(text).toContain('40%')
  })

  it('shows the balance for that group', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => ({ ...testGroup(), myNetBalance: 42.5 }) }),
    })
    await settle()

    expect(textOf(wrapper)).toContain('42.50')
  })

  it('offers a way to change group even with only one', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    // With one group this is still how you reach creating the next.
    expect(wrapper.find('[data-testid="change-group"]').exists()).toBe(true)
  })

  it('opens the picker when asked', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    await wrapper.find('[data-testid="change-group"]').trigger('click')
    await settle(1)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Ski trip')
  })

  it('follows the group that was picked', async () => {
    const { wrapper, groupsStore } = await mountView(DashboardView, {
      api: twoGroups(),
      groups: [],
    })
    await settle()

    groupsStore.setMainGroup('group-2')
    await settle()

    expect(wrapper.find('h1').text()).toContain('Ski trip')
  })

  it('asks you to make a group when there are none', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => [] }),
      groups: [],
    })
    await settle()

    expect(textOf(wrapper)).toContain('No groups yet')
  })

  it('links to the group settings of the group it is showing', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    const targets = wrapper.findAllComponents(RouterLinkStub)
      .map((link) => JSON.stringify(link.props().to)).join(' ')
    expect(targets).toContain('group-settings')
  })

  it('links to settling up for that group', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    const targets = wrapper.findAllComponents(RouterLinkStub)
      .map((link) => JSON.stringify(link.props().to)).join(' ')
    expect(targets).toContain('settle')
  })

  it('says it is offline when the refresh failed but shows the cache', async () => {
    const api = fakeApi({ '/groups': () => testGroup() })
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(DashboardView, { api })
    await settle()

    // The cached group still renders: that is the whole point of the local replica.
    expect(textOf(wrapper)).toContain('Roommates')
    expect(wrapper.text()).toContain('Offline')
  })
})
