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
  testSettlement,
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

  it('shows what is still owed, as a pie', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      // Alice put 100 on her card for the two of them, so Bob owes her 50 and is
      // the only one owing anything.
      expenses: [
        testExpense({
          amount: 100,
          amountInBaseCurrency: 100,
          splits: [
            { memberId: ALICE, amount: 50, amountInBaseCurrency: 50, inputValue: null },
            { memberId: BOB, amount: 50, amountInBaseCurrency: 50, inputValue: null },
          ],
        }),
      ],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Still to settle')
    // The debt, not the till: 100 was spent and 50 of it has to move.
    expect(wrapper.find('[data-testid="centre-total"]').text()).toContain('$50.00')
    expect(wrapper.find('[data-testid="legend-row"]').text()).toContain('Bob')
  })

  it('leaves the pie empty once the group is square', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense()],
      // Bob's half of the 60, paid back. Nothing left to split.
      settlements: [
        testSettlement({ fromMemberId: BOB, toMemberId: ALICE, amount: 30, amountInBaseCurrency: 30 }),
      ],
    })
    await settle()

    // A chart of debts on a settled group is empty for a good reason, and says so
    // rather than reading as a group that has never spent anything.
    expect(wrapper.find('[data-testid="pie-empty"]').text()).toContain('settled up')
  })

  it('totals the expenses beside their heading', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [
        testExpense({ id: 'e1', amount: 60, amountInBaseCurrency: 60 }),
        testExpense({ id: 'e2', amount: 40, amountInBaseCurrency: 40 }),
      ],
    })
    await settle()

    // Every expense in the group, not only the page of them on screen.
    expect(wrapper.find('[data-testid="group-total"]').text()).toBe('$100.00')
  })

  it('says which of the balances is yours', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    // The card that stated your own balance is gone, so the list has to say which
    // of these numbers is yours.
    const rows = wrapper.findAll('li')
    const mine = rows.find((row) => row.find('[data-testid="your-balance"]').exists())
    expect(mine).toBeDefined()
    expect(mine!.text()).toContain('Alice')
    expect(mine!.text()).toContain('30.00')
  })

  it('marks exactly one balance as yours', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    expect(wrapper.findAll('[data-testid="your-balance"]')).toHaveLength(1)
  })

  it('changes group from the mark on the left', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    // The mark is the group's own icon, so it was already the thing on screen
    // that stood for which group. Pressing it is the shortest way to say so.
    const mark = wrapper.find('[data-testid="group-mark"]')
    expect(mark.exists()).toBe(true)
    expect(mark.element.tagName).toBe('BUTTON')
    expect(mark.attributes('aria-label')).toContain('Change group')
  })

  it('offers a way to change group even with only one', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    // With one group this is still how you reach creating the next.
    expect(wrapper.find('[data-testid="group-mark"]').exists()).toBe(true)
  })

  it('opens the picker when the mark is pressed', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    await wrapper.find('[data-testid="group-mark"]').trigger('click')
    await settle(1)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Ski trip')
  })

  it('sends the gear straight to the settings, with no menu in between', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    const gear = wrapper.find('[data-testid="group-settings-link"]')
    expect(gear.exists()).toBe(true)
    expect(wrapper.find('[data-testid="group-menu"]').exists()).toBe(false)
    expect(JSON.stringify(
      wrapper.findAllComponents(RouterLinkStub).map((link) => link.props().to),
    )).toContain('group-settings')
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

  /**
   * The order the screen is read in.
   *
   * The shape of the spending first, then what it means for you with the way to
   * act on it, then everyone else, then the detail. Asserted because the order is
   * the design: every one of these sections renders fine in isolation.
   */
  describe('the order of the dashboard', () => {
    it('runs pie, then balances, then expenses', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: [testExpense({ paidByMemberId: ALICE })],
      })
      await settle()

      const html = wrapper.html()
      const pie = html.indexOf('Still to settle')
      const balances = html.indexOf('>Balances<')
      const expensesHeading = html.indexOf('>Expenses<')

      expect(pie).toBeGreaterThan(-1)
      expect(balances).toBeGreaterThan(pie)
      expect(expensesHeading).toBeGreaterThan(balances)
    })

    it('has no card of its own for your balance', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
      })
      await settle()

      // One number and one button is not a card; the list below says the same
      // thing about everybody, including you.
      expect(wrapper.find('[data-testid="balance-line"]').exists()).toBe(false)
      expect(textOf(wrapper)).not.toContain('Your balance')
    })

    it('puts settling up in the balances card', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: [testExpense({ paidByMemberId: ALICE })],
      })
      await settle()

      const card = wrapper
        .findAll('section')
        .find((section) => section.text().includes('Balances'))

      expect(card!.find('[data-testid="settle-up"]').exists()).toBe(true)
    })

    it('keeps the simplify toggle beside the list it switches', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: [testExpense({ paidByMemberId: ALICE })],
      })
      await settle()

      const html = wrapper.html()
      // It switches the transfer list, not the balances above it.
      expect(html.indexOf('toggle-simplify')).toBeGreaterThan(html.indexOf('Settle up in 1 transfer'))
    })
  })

  /**
   * A long list of expenses.
   *
   * The replica already holds them all, so this is not about fetching: it is
   * about not building a thousand cards for a list nobody has scrolled through.
   * A group that has been running a year is the normal case.
   */
  describe('expenses by month', () => {
    /** A date in a given month, at noon so no timezone can move it. */
    const inMonth = (monthsBack: number, day = 15) => {
      const now = new Date()
      return new Date(now.getFullYear(), now.getMonth() - monthsBack, day, 12).toISOString()
    }

    const acrossMonths = () => [
      testExpense({ id: 'now-1', description: 'This month', spentAt: inMonth(0, 3) }),
      testExpense({ id: 'now-2', description: 'Also this month', spentAt: inMonth(0, 9) }),
      testExpense({ id: 'old-1', description: 'Last month', spentAt: inMonth(1) }),
      testExpense({ id: 'older-1', description: 'The month before', spentAt: inMonth(2) }),
    ]

    it('gives each month its own heading', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: acrossMonths(),
      })
      await settle()

      expect(wrapper.findAll('[data-testid="month-toggle"]')).toHaveLength(3)
    })

    it('opens the current month and leaves the rest closed', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: acrossMonths(),
      })
      await settle()

      const text = textOf(wrapper)
      expect(text).toContain('This month')
      expect(text).toContain('Also this month')
      // Present as a heading, absent as a card: a closed month builds nothing.
      expect(text).not.toContain('Last month')
      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(2)
    })

    it('opens a month when its heading is tapped', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: acrossMonths(),
      })
      await settle()

      await wrapper.findAll('[data-testid="month-toggle"]')[1].trigger('click')
      await settle()

      expect(textOf(wrapper)).toContain('Last month')
      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(3)
    })

    it('closes the month that was open when it is tapped again', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: acrossMonths(),
      })
      await settle()

      await wrapper.findAll('[data-testid="month-toggle"]')[0].trigger('click')
      await settle()

      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(0)
      expect(wrapper.findAll('[data-testid="month-toggle"]')[0].attributes('aria-expanded')).toBe(
        'false',
      )
    })

    it('totals each month on its heading, open or closed', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: [
          testExpense({ id: 'a', amount: 25, amountInBaseCurrency: 25, spentAt: inMonth(0, 2) }),
          testExpense({ id: 'b', amount: 75, amountInBaseCurrency: 75, spentAt: inMonth(0, 4) }),
          testExpense({ id: 'c', amount: 10, amountInBaseCurrency: 10, spentAt: inMonth(1) }),
        ],
      })
      await settle()

      const totals = wrapper.findAll('[data-testid="month-total"]').map((row) => row.text())
      expect(totals[0]).toBe('$100.00')
      expect(totals[1]).toBe('$10.00')
    })

    it('opens the most recent month when nothing was spent this one', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        // A group that went quiet: closing everything would leave a screen of
        // headings and no way to see it has ever been used.
        expenses: [testExpense({ description: 'Quiet since', spentAt: inMonth(2) })],
      })
      await settle()

      expect(textOf(wrapper)).toContain('Quiet since')
    })
  })

  describe('a long expense list', () => {
    /**
     * Distinct ids and times, so the order and the count are both meaningful, and
     * all inside one month: paging happens within the months that are open, so a
     * fixture spread over several would be measuring the sections instead.
     */
    function manyExpenses(count: number) {
      return Array.from({ length: count }, (_, index) =>
        testExpense({
          id: `expense-${String(index).padStart(3, '0')}`,
          paidByMemberId: ALICE,
          description: `Expense ${index}`,
          spentAt: new Date(Date.UTC(2026, 0, 15, 12, index)).toISOString(),
        }),
      )
    }

    /** A stand-in for the browser's observer, which jsdom does not have. */
    function withObserver() {
      const callbacks: Array<(entries: Array<{ isIntersecting: boolean }>) => void> = []
      class FakeObserver {
        constructor(callback: (entries: Array<{ isIntersecting: boolean }>) => void) {
          callbacks.push(callback)
        }
        observe() {}
        disconnect() {}
      }
      vi.stubGlobal('IntersectionObserver', FakeObserver)
      return { scrollToFoot: () => callbacks.at(-1)?.([{ isIntersecting: true }]) }
    }

    it('shows the first twenty and no more', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: manyExpenses(45),
      })
      await settle()

      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(20)
    })

    it('says how many are left', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: manyExpenses(45),
      })
      await settle()

      expect(wrapper.find('[data-testid="show-more-expenses"]').text()).toContain('25 left')
    })

    it('loads the next page when the foot of the list is reached', async () => {
      const observer = withObserver()
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: manyExpenses(45),
      })
      await settle()

      observer.scrollToFoot()
      await settle(1)

      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(40)
      vi.unstubAllGlobals()
    })

    it('keeps loading until there is nothing left', async () => {
      const observer = withObserver()
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: manyExpenses(45),
      })
      await settle()

      for (let page = 0; page < 5; page++) {
        observer.scrollToFoot()
        await settle(1)
      }

      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(45)
      // Nothing left to watch for, so the foot goes.
      expect(wrapper.find('[data-testid="expenses-sentinel"]').exists()).toBe(false)
      vi.unstubAllGlobals()
    })

    it('loads more on a tap, for anything that cannot watch', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: manyExpenses(45),
      })
      await settle()

      // jsdom has no IntersectionObserver, which is also an old browser: the list
      // must not become a dead end.
      await wrapper.find('[data-testid="show-more-expenses"]').trigger('click')
      await settle(1)

      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(40)
    })

    it('shows no foot when everything fits', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: manyExpenses(3),
      })
      await settle()

      expect(wrapper.findAll('[data-testid="expense-card"]')).toHaveLength(3)
      expect(wrapper.find('[data-testid="expenses-sentinel"]').exists()).toBe(false)
    })
  })
})
