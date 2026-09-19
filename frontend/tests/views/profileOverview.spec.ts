import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ProfileView from '@/views/ProfileView.vue'
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

describe('the profile overview', () => {
  const OTHER = 'group-2'

  const roommates = testGroup({ ignoredNamePatterns: ['Loyer'] })

  const trip = testGroup({
    id: OTHER,
    name: 'World tour',
    members: roommates.members,
  })

  const closed = testGroup({ id: 'group-3', name: 'Old flat', isArchived: true })

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

  async function mountProfile() {
    const mounted = await mountView(ProfileView, {
      api: fakeApi({ '/groups': () => summaries(roommates, trip, closed) }),
      groups: [roommates, trip, closed],
      expenses: [dinner, rent, flights],
    })
    await settle()
    return mounted
  }

  it('lists every group you are in, with your balance in each', async () => {
    const { wrapper } = await mountProfile()

    const rows = wrapper.findAll('[data-testid="group-row"]')
    expect(rows.map((row) => row.attributes('data-group-id'))).toEqual([GROUP_ID, OTHER])

    expect(rows[0].text()).toContain('$800.00')
    expect(rows[1].text()).toContain('$20.00')
  })

  it('keeps archived groups apart, because a balance in one is still owed', async () => {
    const { wrapper } = await mountProfile()

    const archived = wrapper.findAll('[data-testid="archived-group-row"]')
    expect(archived).toHaveLength(1)
    expect(archived[0].text()).toContain('Old flat')
  })

  it('has the settings behind the gear rather than on the page', async () => {
    const { wrapper } = await mountProfile()

    expect(wrapper.find('[data-testid="profile-settings-link"]').exists()).toBe(true)
    expect(textOf(wrapper)).not.toContain('Delete my account')
  })
})
