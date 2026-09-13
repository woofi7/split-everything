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

/** Mutable, because the month to open is what the form puts in the URL. */
let routeQuery: Record<string, string> = {}

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { groupId: GROUP_ID }, query: routeQuery }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

/**
 * Which month the group screen opens on.
 *
 * It groups by the month an expense was spent in and keeps every other heading
 * closed, which is right for reading and wrong for the moment just after adding:
 * an expense dated in August is filed correctly and, on a screen showing
 * September, invisibly. That is what somebody adding a stack of old receipts saw,
 * four times over, before concluding the app was dropping them.
 */
describe('the month the group screen opens on', () => {
  const thisMonth = new Date()
  const iso = (when: Date) => when.toISOString()

  const recent = testExpense({
    id: 'expense-recent',
    description: 'Groceries this month',
    spentAt: iso(new Date(thisMonth.getFullYear(), thisMonth.getMonth(), 2, 12)),
  })

  /** Five months back, which no default would ever open. */
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

    // Back to the ordinary default rather than a screen of closed headings.
    expect(textOf(wrapper)).toContain('Groceries this month')
  })

  /**
   * The list renders one window across every month that is open, so a month opened
   * underneath another one used to land entirely behind the "show more". Tapping a
   * heading and finding nothing under it looks exactly like the expenses not being
   * there, which is how an evening of entry looked lost.
   */
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

    // The last of twenty-five, which is well past a twenty-row window.
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
