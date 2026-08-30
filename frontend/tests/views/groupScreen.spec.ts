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

const api = () => fakeApi({ '/groups': () => testGroup() })

/**
 * The one group screen, reached either as the dashboard or by a group's own URL.
 * There used to be two of these; the group route now renders the dashboard, so
 * these behaviours are asserted where they actually live.
 */
describe('the group screen', () => {
  it('marks the corner with the group icon rather than the app icon', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup({ iconName: 'house', colorHex: '#123456' }) }),
      groups: [testGroup({ iconName: 'house', colorHex: '#123456' })],
    })
    await settle()

    // What tells two groups apart at a glance, and the same figure shown beside
    // the group's name everywhere else.
    const mark = wrapper.find('[data-testid="group-mark"]')
    expect(mark.exists()).toBe(true)
    // jsdom normalises the hex to rgb, so the colour is checked as it lands.
    expect(mark.attributes('style')).toContain('rgb(18, 52, 86)')
    expect(wrapper.find('[data-testid="app-icon"]').exists()).toBe(false)
  })

  it('falls back to the app icon when there is no group', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => [] }),
      groups: [],
    })
    await settle()

    // The corner is never empty, and no caller has to decide.
    expect(wrapper.find('[data-testid="group-mark"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="app-icon"]').exists()).toBe(true)
  })

  it('names the group first and the screen second', async () => {
    const { wrapper } = await mountView(DashboardView, { api: api() })

    // Which group you are in is the thing worth reading first, and it is the same
    // question on activity and stats. The member count it replaces is on the
    // balances below and in the group's own settings.
    expect(wrapper.find('h1').text()).toBe('Roommates')
    expect(textOf(wrapper)).toContain('Dashboard')
  })

  it('lists each member balance', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense()],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Balances')
    expect(text).toContain('Alice')
    expect(text).toContain('Bob')
    expect(text).toContain('30.00')
  })

  it('offers a simplified settle-up plan', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(textOf(wrapper)).toContain('Settle up in 1 transfer')
    expect(textOf(wrapper)).toContain('Bob pays Alice')
  })

  it('switches to the raw who-owes-whom view', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense()],
    })

    const toggle = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Show who owes whom'))
    await toggle!.trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('Who owes whom')
    expect(textOf(wrapper)).toContain('Simplify')
  })

  it('says everyone is settled when there is nothing owing', async () => {
    const { wrapper } = await mountView(DashboardView, { api: api() })

    expect(textOf(wrapper)).toContain('Everyone is settled up')
  })

  it('lists the expenses with who paid', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense()],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Dinner')
    expect(text).toContain('Alice paid')
  })

  it('marks an expense that has not synced yet', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense({ pending: true })],
    })

    expect(textOf(wrapper)).toContain('Waiting')
  })

  it('does not mark a synced expense', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense({ pending: false })],
    })

    expect(textOf(wrapper)).not.toContain('Waiting')
  })

  it('prompts for a first expense when the group is empty', async () => {
    const { wrapper } = await mountView(DashboardView, { api: api() })

    expect(textOf(wrapper)).toContain('No expenses yet')
  })

  it('leaves a deleted expense out of the list', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense({ isDeleted: true })],
    })

    expect(textOf(wrapper)).toContain('No expenses yet')
  })

  it('links a suggested transfer to the settle screen with it prefilled', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense()],
    })

    const link = wrapper
      .findAllComponents(RouterLinkStub)
      .find((candidate) => JSON.stringify(candidate.props().to).includes('settle'))

    // The first settle link is the section's own button; the transfer's carries the
    // parties and the amount so the form opens filled in.
    const prefilled = wrapper
      .findAllComponents(RouterLinkStub)
      .map((candidate) => candidate.props().to as { query?: Record<string, unknown> })
      .find((to) => to?.query?.from !== undefined)

    expect(prefilled).toBeDefined()
    expect(prefilled!.query!.from).toBe(BOB)
    expect(prefilled!.query!.to).toBe(ALICE)
    expect(prefilled!.query!.amount).toBe('30.00')
  })

  it('links to the group settings, from the menu in the corner', async () => {
    const { wrapper } = await mountView(DashboardView, { api: api() })

    // Behind the gear now: both items there are about the group rather than about
    // anything in it, and neither is done often enough to sit on the title row.
    await wrapper.find('[data-testid="group-menu"]').trigger('click')
    await settle(1)

    const link = wrapper
      .findAllComponents(RouterLinkStub)
      .find((candidate) => JSON.stringify(candidate.props().to).includes('group-settings'))

    expect(link).toBeDefined()
  })
})

describe('the group screen expense rows', () => {
  it('shows the date of each expense', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ spentAt: '2026-03-14T12:00:00Z', description: 'Dinner' })],
    })
    await settle()

    // A list of amounts with no dates cannot be reconciled against anything.
    expect(textOf(wrapper)).toMatch(/14 Mar|Mar 14/)
  })

  it('marks each row with the colour of whoever paid', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    // Scoped to the expense list: the balance rows carry swatches of their own.
    const swatches = wrapper
      .findAll('span[aria-hidden="true"]')
      .filter((span) => (span.attributes('style') ?? '').includes('background-color'))

    expect(swatches.length).toBeGreaterThan(0)
  })

  it('still names who paid', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Alice paid')
  })
})

describe('the group screen expense card colour', () => {
  it('tints each card with the colour of whoever paid', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [
        testExpense({ id: 'e1', paidByMemberId: ALICE }),
        testExpense({ id: 'e2', paidByMemberId: BOB }),
      ],
    })
    await settle()

    const cards = wrapper.findAll('[data-testid="expense-card"]')
    expect(cards).toHaveLength(2)

    const backgrounds = cards.map((card) => card.attributes('style'))
    expect(backgrounds[0]).toContain('background')
    // Two payers, two colours: the point is telling them apart at a glance.
    expect(backgrounds[0]).not.toBe(backgrounds[1])
  })

  it('mixes the colour with the surface rather than using it raw', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    const style = wrapper.find('[data-testid="expense-card"]').attributes('style') ?? ''

    // A full-strength colour behind the text would be unreadable in either theme,
    // and mixing with the surface token keeps it right in both.
    expect(style).toContain('color-mix')
    expect(style).toContain('--surface-raised')
  })

  it('keeps naming who paid, since a colour alone is not a name', async () => {
    const { wrapper } = await mountView(DashboardView, {
      api: api(),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Alice paid')
  })
})
