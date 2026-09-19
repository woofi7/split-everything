import { beforeEach, describe, expect, it, vi } from 'vitest'
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

let routeQuery: Record<string, string> = {}

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { groupId: GROUP_ID }, query: routeQuery }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

describe('the month the group screen opens on', () => {
  const thisMonth = new Date()
  const iso = (when: Date) => when.toISOString()

  const recent = testExpense({
    id: 'expense-recent',
    description: 'Groceries this month',
    spentAt: iso(new Date(thisMonth.getFullYear(), thisMonth.getMonth(), 2, 12)),
  })

  const older = testExpense({
    id: 'expense-older',
    description: 'Corn dogs in August',
    amount: 16.68,
    spentAt: iso(new Date(thisMonth.getFullYear(), thisMonth.getMonth() - 5, 4, 12)),
    splits: [
      { memberId: ALICE, amount: 8.34, amountInBaseCurrency: 8.34, inputValue: null },
      { memberId: BOB, amount: 8.34, amountInBaseCurrency: 8.34, inputValue: null },
    ],
  })

  const olderMonth = () => {
    const when = new Date(thisMonth.getFullYear(), thisMonth.getMonth() - 5, 1)
    const month = String(when.getMonth() + 1).padStart(2, '0')
    return `${when.getFullYear()}-${month}-01`
  }

  beforeEach(() => {
    routeQuery = {}
  })

  async function mountGroup() {
    return mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      groups: [testGroup()],
      expenses: [recent, older],
    })
  }

  it('opens on this month, and leaves the rest closed', async () => {
    const { wrapper } = await mountGroup()
    await settle()

    expect(textOf(wrapper)).toContain('Groceries this month')
    expect(textOf(wrapper)).not.toContain('Corn dogs in August')
  })

  it('opens on the month the URL names, which is where the expense just added is', async () => {
    routeQuery = { month: olderMonth() }

    const { wrapper } = await mountGroup()
    await settle()

    expect(textOf(wrapper)).toContain('Corn dogs in August')
  })

  it('ignores a month nothing was spent in', async () => {
    routeQuery = { month: '1999-01-01' }

    const { wrapper } = await mountGroup()
    await settle()

    expect(textOf(wrapper)).toContain('Groceries this month')
  })

  it('shows a month when it is opened, however far down the list it starts', async () => {
    const olderMonthExpenses = Array.from({ length: 25 }, (_, index) =>
      testExpense({
        id: `older-${index}`,
        description: `Older ${index}`,
        spentAt: iso(new Date(thisMonth.getFullYear(), thisMonth.getMonth() - 5, 20 - index, 12)),
      }),
    )

    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      groups: [testGroup()],
      expenses: [recent, ...olderMonthExpenses],
    })
    await settle()

    const headings = wrapper.findAll('[data-testid="month-toggle"]')
    await headings[headings.length - 1].trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('Older 24')
  })

  it('brings the expense just added into view and marks it', async () => {
    routeQuery = { month: olderMonth(), added: 'expense-older' }

    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      groups: [testGroup()],
      expenses: [recent, older],
    })
    await settle()

    const card = wrapper.find('[data-expense-id="expense-older"]')
    expect(card.exists()).toBe(true)
    expect(card.classes()).toContain('ring-2')
  })

  it('ignores anything in that parameter that is not a month', async () => {
    routeQuery = { month: 'august' }

    const { wrapper } = await mountGroup()
    await settle()

    expect(textOf(wrapper)).toContain('Groceries this month')
  })
})
