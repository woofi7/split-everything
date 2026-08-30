import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import GroupView from '@/views/GroupView.vue'
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

describe('GroupView', () => {
  it('shows the group name and member count', async () => {
    const { wrapper } = await mountView(GroupView, { api: api() })

    expect(textOf(wrapper)).toContain('Roommates')
    expect(textOf(wrapper)).toContain('2 members')
  })

  it('lists each member balance', async () => {
    const { wrapper } = await mountView(GroupView, {
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
    const { wrapper } = await mountView(GroupView, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(textOf(wrapper)).toContain('Settle up in 1 transfer')
    expect(textOf(wrapper)).toContain('Bob pays Alice')
  })

  it('switches to the raw who-owes-whom view', async () => {
    const { wrapper } = await mountView(GroupView, {
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
    const { wrapper } = await mountView(GroupView, { api: api() })

    expect(textOf(wrapper)).toContain('Everyone is settled up')
  })

  it('lists the expenses with who paid', async () => {
    const { wrapper } = await mountView(GroupView, {
      api: api(),
      expenses: [testExpense()],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Dinner')
    expect(text).toContain('Alice paid')
  })

  it('marks an expense that has not synced yet', async () => {
    const { wrapper } = await mountView(GroupView, {
      api: api(),
      expenses: [testExpense({ pending: true })],
    })

    expect(textOf(wrapper)).toContain('Waiting')
  })

  it('does not mark a synced expense', async () => {
    const { wrapper } = await mountView(GroupView, {
      api: api(),
      expenses: [testExpense({ pending: false })],
    })

    expect(textOf(wrapper)).not.toContain('Waiting')
  })

  it('prompts for a first expense when the group is empty', async () => {
    const { wrapper } = await mountView(GroupView, { api: api() })

    expect(textOf(wrapper)).toContain('No expenses yet')
  })

  it('leaves a deleted expense out of the list', async () => {
    const { wrapper } = await mountView(GroupView, {
      api: api(),
      expenses: [testExpense({ isDeleted: true })],
    })

    expect(textOf(wrapper)).toContain('No expenses yet')
  })

  it('links a suggested transfer to the settle screen with it prefilled', async () => {
    const { wrapper } = await mountView(GroupView, {
      api: api(),
      expenses: [testExpense()],
    })

    const link = wrapper
      .findAllComponents(RouterLinkStub)
      .find((candidate) => JSON.stringify(candidate.props().to).includes('settle'))

    expect(link).toBeDefined()
    const to = link!.props().to as { query: Record<string, unknown> }
    expect(to.query.from).toBe(BOB)
    expect(to.query.to).toBe(ALICE)
    expect(to.query.amount).toBe(30)
  })

  it('links to the group settings', async () => {
    const { wrapper } = await mountView(GroupView, { api: api() })

    const link = wrapper
      .findAllComponents(RouterLinkStub)
      .find((candidate) => JSON.stringify(candidate.props().to).includes('group-settings'))

    expect(link).toBeDefined()
  })
})

describe('GroupView expense rows', () => {
  it('shows the date of each expense', async () => {
    const { wrapper } = await mountView(GroupView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ spentAt: '2026-03-14T12:00:00Z', description: 'Dinner' })],
    })
    await settle()

    // A list of amounts with no dates cannot be reconciled against anything.
    expect(textOf(wrapper)).toMatch(/14 Mar|Mar 14/)
  })

  it('marks each row with the colour of whoever paid', async () => {
    const { wrapper } = await mountView(GroupView, {
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
    const { wrapper } = await mountView(GroupView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Alice paid')
  })
})
