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

  it('keeps what is about the group behind one icon', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()

    // Nothing on show until asked: both items were competing with the page title
    // for the corner of every visit.
    expect(wrapper.find('[data-testid="group-menu"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="group-menu-items"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="change-group"]').exists()).toBe(false)
  })

  it('offers a way to change group even with only one', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()
    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)

    // With one group this is still how you reach creating the next.
    expect(wrapper.find('[data-testid="change-group"]').exists()).toBe(true)
  })

  it('opens the picker when asked', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)
    await wrapper.find('[data-testid="change-group"]').trigger('click')
    await settle(1)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Ski trip')
  })

  it('closes the menu on the way to the picker', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)
    await wrapper.find('[data-testid="change-group"]').trigger('click')
    await settle(1)

    // Otherwise it sits open behind the dialog and is there again on the way back.
    expect(wrapper.find('[data-testid="group-menu-items"]').exists()).toBe(false)
  })

  it('closes the menu on Escape', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
    })
    await settle()
    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await settle(1)

    // A menu that only closes by choosing something is a trap on a phone.
    expect(wrapper.find('[data-testid="group-menu-items"]').exists()).toBe(false)
  })

  it('closes the menu when something else is clicked', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      attachTo: document.body,
    })
    await settle()
    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)

    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await settle(1)

    expect(wrapper.find('[data-testid="group-menu-items"]').exists()).toBe(false)
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
    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)

    const target = wrapper.find('[data-testid="menu-group-settings"]')
    expect(target.exists()).toBe(true)
    expect(JSON.stringify(
      wrapper.findAllComponents(RouterLinkStub)
        .map((link) => link.props().to),
    )).toContain('group-settings')
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
    it('puts the pie above the balance, and the balance above the expenses', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
        expenses: [testExpense({ paidByMemberId: ALICE })],
      })
      await settle()

      const html = wrapper.html()
      const pie = html.indexOf('Who paid')
      const balance = html.indexOf('balance-line')
      const balances = html.indexOf('>Balances<')
      const expensesHeading = html.indexOf('>Expenses<')

      expect(pie).toBeGreaterThan(-1)
      expect(balance).toBeGreaterThan(pie)
      expect(balances).toBeGreaterThan(balance)
      expect(expensesHeading).toBeGreaterThan(balances)
    })

    it('stacks the label over the amount', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
      })
      await settle()

      // Two lines, small over large, in a column rather than side by side.
      const stack = wrapper.find('[data-testid="balance-line"] > span')
      expect(stack.classes()).toContain('flex-col')
      expect(stack.classes()).toContain('justify-center')
      // At least as tall as the button beside it, so the row reads as one block.
      expect(stack.classes()).toContain('min-h-11')
    })

    it('puts settling up on the same line as the balance', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
      })
      await settle()

      // The number and the button that answers it belong next to each other.
      const line = wrapper.find('[data-testid="balance-line"]')
      expect(line.text()).toContain('Your balance')
      expect(line.find('[data-testid="settle-up"]').exists()).toBe(true)
    })

    it('does not repeat group settings outside the menu', async () => {
      const { wrapper } = await mountView(DashboardView, {
        api: fakeApi({ '/groups': () => testGroup() }),
      })
      await settle()

      expect(wrapper.find('[data-testid="balance-line"]').text()).not.toContain('Group settings')
    })
  })

  /**
   * A long list of expenses.
   *
   * The replica already holds them all, so this is not about fetching: it is
   * about not building a thousand cards for a list nobody has scrolled through.
   * A group that has been running a year is the normal case.
   */
  describe('a long expense list', () => {
    /** Distinct ids and dates, so the order and the count are both meaningful. */
    function manyExpenses(count: number) {
      return Array.from({ length: count }, (_, index) =>
        testExpense({
          id: `expense-${String(index).padStart(3, '0')}`,
          paidByMemberId: ALICE,
          description: `Expense ${index}`,
          spentAt: new Date(Date.UTC(2026, 0, 1 + index)).toISOString(),
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
