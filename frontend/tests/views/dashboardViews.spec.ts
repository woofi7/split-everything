import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ActivityView from '@/views/ActivityView.vue'
import StatsView from '@/views/StatsView.vue'
import { GROUP_ID, fakeApi, mountView, settle, testGroup, textOf } from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

describe('ActivityView', () => {
  const feed = (items: unknown[]) =>
    fakeApi({ '/activity': () => ({ items }), '/groups': () => [testGroup()] })

  it('shows the group the rest of the app is on', async () => {
    const client = feed([])
    await mountView(ActivityView, { api: client })
    await settle()

    // All screens follow the one group, so a feed spanning every group would be
    // the odd one out.
    expect(client.get).toHaveBeenCalledWith(
      '/activity',
      expect.objectContaining({ groupId: GROUP_ID }),
    )
  })

  it('names that group, so a short feed is explained', async () => {
    const { wrapper } = await mountView(ActivityView, { api: feed([]) })
    await settle()

    expect(textOf(wrapper)).toContain('Roommates')
  })

  it('lists what happened, newest first as the server sent it', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: feed([
        {
          id: 2,
          groupId: 'group-1',
          groupName: 'Roommates',
          kind: 'ExpenseCreated',
          actorName: 'Alice',
          summary: 'Alice added Groceries',
          occurredAt: '2026-02-01T12:00:00Z',
        },
        {
          id: 1,
          groupId: 'group-1',
          groupName: 'Roommates',
          kind: 'GroupCreated',
          actorName: 'Alice',
          summary: 'Alice created Roommates',
          occurredAt: '2026-01-01T12:00:00Z',
        },
      ]),
    })

    const text = textOf(wrapper)
    expect(text).toContain('Alice added Groceries')
    expect(text.indexOf('added Groceries')).toBeLessThan(text.indexOf('created Roommates'))
  })

  it('names the group each entry belongs to', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: feed([
        {
          id: 1,
          groupId: 'group-1',
          groupName: 'Roommates',
          kind: 'ExpenseCreated',
          actorName: 'Alice',
          summary: 'Alice added Groceries',
          occurredAt: '2026-01-01T12:00:00Z',
        },
      ]),
    })

    expect(textOf(wrapper)).toContain('Roommates')
  })

  it('says the feed needs a connection when it could not load', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(ActivityView, { api })

    // Being explicit that groups and expenses still work matters: otherwise this
    // reads as the whole app being broken.
    const text = textOf(wrapper)
    expect(text).toContain('needs a connection')
    expect(text).toContain('still work offline')
  })

  it('says nothing has happened when the feed is empty', async () => {
    const { wrapper } = await mountView(ActivityView, { api: feed([]) })

    expect(textOf(wrapper)).toContain('Nothing has happened yet')
  })
})

describe('StatsView', () => {
  const dashboard = (overrides: Record<string, unknown> = {}) => ({
    currency: 'CAD',
    totalSpend: 150,
    myShare: 75,
    myPaid: 100,
    expenseCount: 3,
    spendOverTime: [
      {
        bucket: '2026-01-01',
        amount: 100,
        expenseCount: 2,
        byMember: [
          { memberId: 'm1', memberName: 'Alice', amount: 60 },
          { memberId: 'm2', memberName: 'Bob', amount: 40 },
        ],
      },
      {
        bucket: '2026-02-01',
        amount: 50,
        expenseCount: 1,
        byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 50 }],
      },
    ],
    byCategory: [
      {
        categoryId: 'c1',
        categoryKey: 'groceries',
        categoryName: 'Groceries',
        colorHex: '#16a34a',
        amount: 100,
        expenseCount: 2,
        share: 0.667,
      },
      {
        categoryId: null,
        categoryKey: 'uncategorised',
        categoryName: 'Uncategorised',
        colorHex: '#94a3b8',
        amount: 50,
        expenseCount: 1,
        share: 0.333,
      },
    ],
    byMember: [
      { memberId: 'm1', memberName: 'Alice', paid: 100, owed: 75, net: 25 },
      { memberId: 'm2', memberName: 'Bob', paid: 50, owed: 75, net: -25 },
    ],
    ...overrides,
  })

  const api = (overrides: Record<string, unknown> = {}) =>
    fakeApi({ '/stats': () => dashboard(overrides), '/groups': () => [testGroup()] })

  it('shows the totals', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const text = textOf(wrapper)
    expect(text).toContain('Total')
    expect(text).toContain('150.00')
    expect(text).toContain('Your share')
    expect(text).toContain('75.00')
  })

  it('draws the spend-over-time bars scaled to the biggest bucket', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const bars = wrapper.findAll('[role="img"] li > span')
    expect(bars).toHaveLength(2)
    // The tallest bucket fills the chart; the other is proportional.
    expect(bars[0].attributes('style')).toContain('height: 100%')
    expect(bars[1].attributes('style')).toContain('height: 50%')
  })

  it('gives each bar a parent with a height to be a percentage of', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    // The bars carried a percentage height inside an auto-height list item, so it
    // resolved against nothing and the whole chart rendered flat. jsdom does no
    // layout, which is why the style assertion above passed while the chart was
    // empty on screen.
    const items = wrapper.findAll('[role="img"] li')
    expect(items.length).toBeGreaterThan(0)
    for (const item of items) expect(item.classes()).toContain('h-full')
  })

  it('gives a tiny bucket a visible floor rather than an invisible bar', async () => {
    const { wrapper } = await mountView(StatsView, {
      api: api({
        spendOverTime: [
          {
            bucket: '2026-01-01',
            amount: 1000,
            expenseCount: 1,
            byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 1000 }],
          },
          {
            bucket: '2026-02-01',
            amount: 1,
            expenseCount: 1,
            byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 1 }],
          },
        ],
      }),
    })

    const bars = wrapper.findAll('[role="img"] li > span')
    expect(bars[1].attributes('style')).toContain('height: 4%')
  })

  it('splits each bar by whoever paid, in their own colour', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    // January had two payers, February one.
    const bars = wrapper.findAll('[role="img"] li')
    expect(bars[0].findAll('[data-testid="bar-segment"]')).toHaveLength(2)
    expect(bars[1].findAll('[data-testid="bar-segment"]')).toHaveLength(1)

    const colours = bars[0]
      .findAll('[data-testid="bar-segment"]')
      .map((segment) => segment.attributes('style'))
    expect(colours[0]).not.toBe(colours[1])
  })

  it('sizes each segment by that person share of the bucket', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const segments = wrapper.findAll('[role="img"] li')[0].findAll('[data-testid="bar-segment"]')
    expect(segments[0].attributes('style')).toContain('height: 60%')
    expect(segments[1].attributes('style')).toContain('height: 40%')
  })

  it('names the people in a key under the chart', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    // A stack of coloured blocks says nothing without one.
    const text = textOf(wrapper)
    expect(text).toContain('Alice')
    expect(text).toContain('Bob')
  })

  it('describes the chart for a screen reader', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const label = wrapper.find('[role="img"]').attributes('aria-label')
    expect(label).toContain('by who paid')
    expect(label).toContain('Alice')
  })

  it('draws one whole bar when the server sent no breakdown', async () => {
    const { wrapper } = await mountView(StatsView, {
      api: api({
        spendOverTime: [{ bucket: '2026-01-01', amount: 100, expenseCount: 1 }],
      }),
    })

    // Rather than an empty bar: an older server, or a bucket with nothing in it.
    const segments = wrapper.findAll('[data-testid="bar-segment"]')
    expect(segments).toHaveLength(1)
    expect(segments[0].attributes('style')).toContain('height: 100%')
  })

  it('breaks the spend down by category', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const text = textOf(wrapper)
    expect(text).toContain('By category')
    expect(text).toContain('Groceries')
    expect(text).toContain('Uncategorised')
  })

  it('shows who is up and who is down', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const text = textOf(wrapper)
    expect(text).toContain('Who owes whom')
    expect(text).toContain('Alice')
    expect(text).toContain('Bob')
  })

  it('reloads when the granularity changes', async () => {
    const client = api()
    const { wrapper } = await mountView(StatsView, { api: client })
    client.get.mockClear()

    const selects = wrapper.findAll('select')
    await selects[1].setValue('week')
    await settle()

    expect(client.get).toHaveBeenCalledWith('/stats', expect.objectContaining({ granularity: 'week' }))
  })

  it('reloads when a single group is chosen', async () => {
    const client = api()
    const { wrapper } = await mountView(StatsView, { api: client })
    client.get.mockClear()

    await wrapper.findAll('select')[0].setValue(testGroup().id)
    await settle()

    expect(client.get).toHaveBeenCalledWith(
      '/stats',
      expect.objectContaining({ groupId: testGroup().id }),
    )
  })

  it('opens on the group the rest of the app is on', async () => {
    const client = api()
    await mountView(StatsView, { api: client })
    await settle()

    // Not a total across groups nobody asked for.
    expect(client.get).toHaveBeenCalledWith(
      '/stats',
      expect.objectContaining({ groupId: GROUP_ID, granularity: 'month' }),
    )
  })

  it('can still be asked for every group', async () => {
    const client = api()
    const { wrapper } = await mountView(StatsView, { api: client })
    await settle()

    await wrapper.find('select').setValue('')
    await settle()

    expect(client.get).toHaveBeenCalledWith(
      '/stats',
      expect.objectContaining({ groupId: undefined }),
    )
  })

  it('says stats need a connection when they could not load', async () => {
    const client = fakeApi()
    client.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(StatsView, { api: client })

    expect(textOf(wrapper)).toContain('Stats need a connection')
  })

  it('leaves the chart out when there is nothing to plot', async () => {
    const { wrapper } = await mountView(StatsView, { api: api({ spendOverTime: [] }) })

    expect(wrapper.find('[role="img"]').exists()).toBe(false)
  })
})

describe('ActivityView opening an expense', () => {
  const entry = (overrides: Record<string, unknown> = {}) => ({
    id: 1,
    groupId: GROUP_ID,
    groupName: 'Roommates',
    kind: 'ExpenseAdded',
    actorMemberId: 'm1',
    actorName: 'Alice',
    subjectType: 'Expense',
    subjectId: 'expense-1',
    summary: 'Alice added Dinner',
    occurredAt: '2026-01-05T12:00:00Z',
    ...overrides,
  })

  it('tints an entry with the colour of whoever acted', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({
          items: [
            entry({ id: 1, actorMemberId: 'm1', subjectId: 'expense-1' }),
            entry({ id: 2, actorMemberId: 'm2', subjectId: 'expense-2' }),
          ],
        }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    const rows = wrapper.findAll('[data-testid="activity-row"]')
    const styles = rows.map((row) => row.attributes('style'))

    expect(styles[0]).toContain('color-mix')
    // Two people, two colours, matching their expense cards.
    expect(styles[0]).not.toBe(styles[1])
  })

  it('leaves an entry with nobody behind it on the plain surface', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({ items: [entry({ actorMemberId: null })] }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    // A system event must not borrow somebody else's colour.
    const row = wrapper.find('[data-testid="activity-row"]')
    expect(row.attributes('style')).toBeFalsy()
    expect(row.classes()).toContain('surface-card')
  })

  it('links an expense entry to the expense', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({ '/activity': () => ({ items: [entry()] }), '/groups': () => [testGroup()] }),
    })
    await settle()

    const row = wrapper.find('[data-testid="activity-row"]')
    expect(row.attributes('data-linked')).toBe('true')

    const link = wrapper.findAllComponents(RouterLinkStub)
      .find((l) => JSON.stringify(l.props().to).includes('expense-1'))
    expect(link).toBeDefined()
  })

  it('opens the group for an entry with no expense of its own', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({ items: [entry({ subjectType: 'GroupMember', subjectId: 'm1' })] }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    // Someone being added has no screen of its own, but the roster does.
    expect(wrapper.find('[data-testid="activity-row"]').attributes('data-linked')).toBe('true')

    const link = wrapper.findAllComponents(RouterLinkStub)
      .find((l) => JSON.stringify(l.props().to).includes('"group"'))
    expect(link).toBeDefined()
  })

  it('leaves an entry with no group as plain text', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({ items: [entry({ groupId: null })] }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    expect(wrapper.find('[data-testid="activity-row"]').attributes('data-linked')).toBe('false')
  })
})
