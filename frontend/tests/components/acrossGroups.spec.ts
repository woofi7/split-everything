import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import AcrossGroups from '@/components/stats/AcrossGroups.vue'
import {
  ALICE,
  BOB,
  fakeApi,
  mountView,
  settle,
  testExpense,
  testGroup,
  testSettlement,
} from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

/**
 * One person's figures, across every group they are in.
 *
 * The stats screen above this is about a group; this is the other question, and
 * before it existed the answer was three tabs and the arithmetic in your head.
 */
describe('across your groups', () => {
  const OTHER = 'group-2'

  const roommates = testGroup({ ignoredNamePatterns: ['Loyer'] })
  const trip = testGroup({ id: OTHER, name: 'World tour', members: testGroup().members })
  const euros = testGroup({ id: 'group-3', name: 'Paris', baseCurrency: 'EUR', members: testGroup().members })

  /** Alice paid 100, half of it Bob's. */
  const dinner = testExpense({
    id: 'expense-dinner',
    description: 'Dinner',
    amount: 100,
    amountInBaseCurrency: 100,
    splits: [
      { memberId: ALICE, amount: 50, amountInBaseCurrency: 50, inputValue: null },
      { memberId: BOB, amount: 50, amountInBaseCurrency: 50, inputValue: null },
    ],
  })

  const rent = testExpense({
    id: 'expense-rent',
    description: 'Loyer aout',
    amount: 1500,
    amountInBaseCurrency: 1500,
    splits: [
      { memberId: ALICE, amount: 750, amountInBaseCurrency: 750, inputValue: null },
      { memberId: BOB, amount: 750, amountInBaseCurrency: 750, inputValue: null },
    ],
  })

  /** Bob paid this one, in the other group: 40, half of it Alice's. */
  const flights = testExpense({
    id: 'expense-flights',
    groupId: OTHER,
    paidByMemberId: BOB,
    description: 'Flights',
    amount: 40,
    amountInBaseCurrency: 40,
    splits: [
      { memberId: ALICE, amount: 20, amountInBaseCurrency: 20, inputValue: null },
      { memberId: BOB, amount: 20, amountInBaseCurrency: 20, inputValue: null },
    ],
  })

  const summaries = (...list: Array<ReturnType<typeof testGroup>>) =>
    list.map((group) => ({ ...group, memberCount: group.members.length, lastActivityAt: null }))

  /**
   * The component reads the stores that the screen around it loads, so the test
   * loads them too rather than reaching inside the component.
   */
  async function mountBlock(options: Parameters<typeof mountView>[1]) {
    const mounted = await mountView(AcrossGroups, options)
    await mounted.groupsStore.loadAll()
    await mounted.expensesStore.hydrate()
    await settle()
    return mounted
  }

  it('adds up what one person paid and owed, everywhere', async () => {
    const { wrapper } = await mountBlock({
      api: fakeApi({ '/groups': () => summaries(roommates, trip) }),
      groups: [roommates, trip],
      expenses: [dinner, rent, flights],
    })

    // The rent is a name that group leaves out, so it is out of these too: the
    // same arithmetic the group screen does, or the two screens disagree.
    expect(wrapper.find('[data-testid="combined-paid"]').text()).toBe('$100.00')
    expect(wrapper.find('[data-testid="combined-share"]').text()).toBe('$70.00')
  })

  it('states the balance across every group, settlements included', async () => {
    const { wrapper } = await mountBlock({
      api: fakeApi({ '/groups': () => summaries(roommates, trip) }),
      groups: [roommates, trip],
      expenses: [dinner, flights],
      // Bob hands 30 back, which is what paid-less-share cannot see.
      settlements: [testSettlement({ amount: 30, amountInBaseCurrency: 30 })],
    })

    // 50 owed by Bob, less the 30 he gave back, less the 20 Alice owes on flights.
    expect(wrapper.find('[data-testid="combined-net"]').text()).toContain('$0.00')
  })

  it('keeps currencies apart rather than inventing a total', async () => {
    const { wrapper } = await mountBlock({
      api: fakeApi({ '/groups': () => summaries(roommates, euros) }),
      groups: [roommates, euros],
      expenses: [dinner],
    })

    const blocks = wrapper.findAll('[data-testid="across-groups"]')
    expect(blocks).toHaveLength(2)
    // Named once there are two, because "across your groups" twice says nothing.
    expect(blocks[0].text()).toContain('CAD')
    expect(blocks[1].text()).toContain('EUR')
  })

  it('sets itself apart from the group\'s own figures', async () => {
    const { wrapper } = await mountBlock({
      api: fakeApi({ '/groups': () => summaries(roommates) }),
      groups: [roommates],
      expenses: [dinner],
    })

    // "You paid" means one thing above the rule and another below it, so the rule
    // and its name are what keep the two from reading as a mistake.
    expect(wrapper.text()).toContain('Just you')
  })

  it('says nothing at all when there are no groups', async () => {
    const { wrapper } = await mountBlock({
      api: fakeApi({ '/groups': () => [] }),
      groups: [],
    })

    expect(wrapper.find('[data-testid="across-groups"]').exists()).toBe(false)
  })

  it('counts the expenses behind the figures', async () => {
    const { wrapper } = await mountBlock({
      api: fakeApi({ '/groups': () => summaries(roommates, trip) }),
      groups: [roommates, trip],
      expenses: [dinner, rent, flights],
    })

    // Two expenses, not three: the rent is left out here as it is everywhere else.
    expect(wrapper.text()).toContain('2 expenses across 2 groups')
  })
})
