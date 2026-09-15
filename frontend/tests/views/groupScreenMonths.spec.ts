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

  it('ignores anything in that parameter that is not a month', async () => {
    routeQuery = { month: 'august' }

    const { wrapper } = await mountGroup()
    await settle()

    expect(textOf(wrapper)).toContain('Groceries this month')
  })
})
