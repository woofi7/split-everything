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
  useRoute: () => ({ params: { groupId: GROUP_ID }, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

describe('totals when the group leaves names out', () => {
  const thisMonth = new Date()
  const on = (day: number) =>
    new Date(thisMonth.getFullYear(), thisMonth.getMonth(), day, 12).toISOString()

  const rent = testExpense({
    id: 'expense-rent',
    description: 'Loyer aout',
    amount: 1500,
    amountInBaseCurrency: 1500,
    spentAt: on(1),
    splits: [
      { memberId: ALICE, amount: 750, amountInBaseCurrency: 750, inputValue: null },
      { memberId: BOB, amount: 750, amountInBaseCurrency: 750, inputValue: null },
    ],
  })

  const groceries = testExpense({
    id: 'expense-groceries',
    description: 'Groceries at Metro',
    amount: 60,
    amountInBaseCurrency: 60,
    spentAt: on(2),
  })

  const dinner = testExpense({
    id: 'expense-dinner',
    description: 'Dinner out',
    amount: 40,
    amountInBaseCurrency: 40,
    spentAt: on(3),
    splits: [
      { memberId: ALICE, amount: 20, amountInBaseCurrency: 20, inputValue: null },
      { memberId: BOB, amount: 20, amountInBaseCurrency: 20, inputValue: null },
    ],
  })

  const withRentLeftOut = testGroup({ ignoredNamePatterns: ['Loyer', 'Rent'] })

  async function mountGroup(group = withRentLeftOut) {
    const mounted = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => group }),
      groups: [group],
      expenses: [rent, groceries, dinner],
    })
    await settle()
    return mounted
  }

  it('totals the group without them, and stars the figure', async () => {
    const { wrapper } = await mountGroup()

    expect(wrapper.find('[data-testid="group-total"]').text()).toBe('$100.00*')
    expect(wrapper.find('[data-testid="group-left-out"]').exists()).toBe(false)
  })

  it('says what the group really came to when the total is asked', async () => {
    const { wrapper } = await mountGroup()

    await wrapper.find('[data-testid="group-total"]').trigger('click')
    await settle()

    expect(wrapper.find('[data-testid="group-left-out"]').text()).toContain('$1,600.00')
  })

  it('totals the month without them, and counts only what it totalled', async () => {
    const { wrapper } = await mountGroup()

    expect(wrapper.find('[data-testid="month-total"]').text()).toBe('$100.00*')

    expect(wrapper.find('[data-testid="month-toggle"]').text()).toContain('2')
  })

  it('says what the month really came to on a tap, and on a pointer', async () => {
    const { wrapper } = await mountGroup()

    await wrapper.find('[data-testid="month-total-zone"]').trigger('mouseenter')
    await settle()
    expect(wrapper.find('[data-testid="month-left-out"]').text()).toContain('$1,600.00')

    await wrapper.find('[data-testid="month-total-zone"]').trigger('mouseleave')
    await settle()
    expect(wrapper.find('[data-testid="month-left-out"]').exists()).toBe(false)

    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(wrapper.find('[data-testid="month-left-out"]').text()).toContain('$1,600.00')
  })

  it('shuts again on a second tap, pointer or no pointer', async () => {
    const { wrapper } = await mountGroup()
    const zone = () => wrapper.find('[data-testid="month-total-zone"]')
    const showing = () => wrapper.find('[data-testid="month-left-out"]').exists()

    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(true)

    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(false)

    await zone().trigger('mouseenter')
    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(true)

    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(false)

    await zone().trigger('mouseleave')
    await zone().trigger('mouseenter')
    await settle()
    expect(showing()).toBe(true)
  })

  it('leaves the month open and shut where it was when the total is asked', async () => {
    const { wrapper } = await mountGroup()

    const before = wrapper.findAll('[data-expense-id]').length
    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()

    expect(wrapper.findAll('[data-expense-id]').length).toBe(before)
  })

  it('still lists the expense it left out', async () => {
    const { wrapper } = await mountGroup()

    expect(wrapper.find('[data-expense-id="expense-rent"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Loyer aout')
  })

  it('leaves the balances alone, because the rent is still owed', async () => {
    const { wrapper } = await mountGroup()

    expect(textOf(wrapper)).toContain('$800.00')
  })

  it('counts everything, and stars nothing, when the group has asked for nothing', async () => {
    const { wrapper } = await mountGroup(testGroup())

    expect(wrapper.find('[data-testid="group-total"]').text()).toBe('$1,600.00')
    expect(wrapper.find('[data-testid="month-total"]').text()).toBe('$1,600.00')
    expect(wrapper.find('[data-testid="group-left-out"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="month-left-out"]').exists()).toBe(false)
  })

  it('says under the chart what the chart is not counting', async () => {
    const { wrapper } = await mountGroup()

    expect(wrapper.find('[data-testid="pie-left-out"]').text()).toContain('$1,500.00')
  })
})
