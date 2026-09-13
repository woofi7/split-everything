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

/**
 * The names a group asked to leave out, and the totals that state them.
 *
 * Rent is the whole reason the setting exists: a household pays fifteen hundred
 * before it has bought anything, and a month total carrying that barely moves, so
 * "did we spend more than usual?" cannot be read off it. Leaving it out of the
 * totals is only honest while every total that does so says how much it left out -
 * otherwise the screen is a figure that quietly disagrees with the list under it.
 */
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

    // The star is the whole promise: this number has more behind it. Without it a
    // total that quietly drops fifteen hundred is a total nobody can check.
    expect(wrapper.find('[data-testid="group-total"]').text()).toBe('$100.00*')
    expect(wrapper.find('[data-testid="group-left-out"]').exists()).toBe(false)
  })

  it('says what the group really came to when the total is asked', async () => {
    const { wrapper } = await mountGroup()

    await wrapper.find('[data-testid="group-total"]').trigger('click')
    await settle()

    // 100 of everyday spending and 1,500 of rent: the two halves add back up.
    expect(wrapper.find('[data-testid="group-left-out"]').text()).toContain('$1,600.00')
  })

  it('totals the month without them, and counts only what it totalled', async () => {
    const { wrapper } = await mountGroup()

    expect(wrapper.find('[data-testid="month-total"]').text()).toBe('$100.00*')

    // Two expenses behind the hundred, and the third still in the list under it.
    expect(wrapper.find('[data-testid="month-toggle"]').text()).toContain('2')
  })

  it('says what the month really came to on a tap, and on a pointer', async () => {
    const { wrapper } = await mountGroup()

    // The zone, not the figure: what it reveals appears below the figure, so a
    // hover that ended at the figure would be chased off by its own answer.
    await wrapper.find('[data-testid="month-total-zone"]').trigger('mouseenter')
    await settle()
    expect(wrapper.find('[data-testid="month-left-out"]').text()).toContain('$1,600.00')

    // Pointing at it is not tapping it: a phone has no pointer, so the tap has to
    // work on its own.
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

    // A phone: two taps, open and shut.
    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(true)

    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(false)

    // A mouse, where the pointer is still sitting on what was just closed: hover
    // used to open it straight back up, so the press did nothing at all.
    await zone().trigger('mouseenter')
    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(true)

    await wrapper.find('[data-testid="month-total"]').trigger('click')
    await settle()
    expect(showing()).toBe(false)

    // Leaving and coming back is a fresh question, and hover answers it again.
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

    // The total answers its own question now, so it cannot also be the control
    // that opens and closes the month it sits on.
    expect(wrapper.findAll('[data-expense-id]').length).toBe(before)
  })

  it('still lists the expense it left out', async () => {
    const { wrapper } = await mountGroup()

    // Left out of a total, not out of the group. Someone looking for the rent has
    // to find it where they put it.
    expect(wrapper.find('[data-expense-id="expense-rent"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Loyer aout')
  })

  it('leaves the balances alone, because the rent is still owed', async () => {
    const { wrapper } = await mountGroup()

    // Alice put up 1,600 of which 800 was Bob's. A display rule has no business
    // touching that, and the day it does is the day the app stops being trusted.
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
