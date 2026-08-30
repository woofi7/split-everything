import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ActivityView from '@/views/ActivityView.vue'
import StatsView from '@/views/StatsView.vue'
import { db } from '@/offline/db'
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

describe('ActivityView', () => {
  it('names the group first and the screen second', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({ '/activity': () => ({ items: [] }), '/groups': () => [testGroup()] }),
    })
    await settle()

    expect(wrapper.find('h1').text()).toBe('Roommates')
    expect(textOf(wrapper)).toContain('Activity')
  })

  it('offers the group controls, since the feed is about a group', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({ '/activity': () => ({ items: [] }), '/groups': () => [testGroup()] }),
    })
    await settle()

    // The mark changes group, the gear opens its settings: the same pair on every
    // screen scoped to a group.
    expect(wrapper.find('[data-testid="group-mark"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="group-settings-link"]').exists()).toBe(true)
  })

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

  it('shows the feed it has stored when the server cannot be reached', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(ActivityView, {
      api,
      // What a device that has been online before holds: the feed as it arrived.
      activity: [
        {
          id: 7,
          groupId: GROUP_ID,
          groupName: 'Roommates',
          kind: 'ExpenseCreated',
          actorMemberId: ALICE,
          actorName: 'Alice',
          subjectType: 'Expense',
          subjectId: 'expense-1',
          summary: 'Alice added Groceries',
          occurredAt: '2026-01-02T12:00:00Z',
        },
      ],
    })
    await settle()

    const text = textOf(wrapper)
    expect(text).toContain('Alice added Groceries')
    expect(text).toContain('Offline')
  })

  it('keeps the feed it is given, so the next time offline has something to show', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({
          items: [
            {
              id: 11,
              groupId: GROUP_ID,
              groupName: 'Roommates',
              kind: 'ExpenseCreated',
              actorMemberId: ALICE,
              actorName: 'Alice',
              subjectType: 'Expense',
              subjectId: 'expense-1',
              summary: 'Alice added Dinner',
              occurredAt: '2026-01-03T12:00:00Z',
            },
          ],
        }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    expect(textOf(wrapper)).toContain('Alice added Dinner')
    expect(await db.activity.get(11)).toMatchObject({ summary: 'Alice added Dinner' })
  })

  it('says nothing is stored yet when offline on a device that never pulled it', async () => {
    const api = fakeApi()
    api.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(ActivityView, { api })
    await settle()

    // The only case left where a connection is genuinely needed for this screen.
    expect(textOf(wrapper)).toContain('No activity stored on this device yet')
  })

})

describe('StatsView', () => {
  it('names the group first and the screen second', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })
    await settle()

    expect(wrapper.find('h1').text()).toBe('Roommates')
    expect(textOf(wrapper)).toContain('Stats')
  })

  it('offers the group controls, since the screen is about a group', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })
    await settle()

    expect(wrapper.find('[data-testid="group-mark"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="group-settings-link"]').exists()).toBe(true)
  })

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
        colorHex: '#16a34a',
        amount: 100,
        expenseCount: 2,
        share: 0.667,
      },
      {
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

    const bars = wrapper.findAll('[data-testid="bar-fill"]')
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
    // empty on screen. The column is a button now, so the chain runs through it.
    const items = wrapper.findAll('[data-testid="spend-chart"] li')
    expect(items.length).toBeGreaterThan(0)
    for (const item of items) expect(item.classes()).toContain('h-full')

    for (const bar of wrapper.findAll('[data-testid="bar"]')) {
      expect(bar.classes()).toContain('h-full')
    }
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

    const bars = wrapper.findAll('[data-testid="bar-fill"]')
    expect(bars[1].attributes('style')).toContain('height: 4%')
  })

  it('splits each bar by whoever paid, in their own colour', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    // January had two payers, February one.
    const bars = wrapper.findAll('[data-testid="spend-chart"] li')
    expect(bars[0].findAll('[data-testid="bar-segment"]')).toHaveLength(2)
    expect(bars[1].findAll('[data-testid="bar-segment"]')).toHaveLength(1)

    const colours = bars[0]
      .findAll('[data-testid="bar-segment"]')
      .map((segment) => segment.attributes('style'))
    expect(colours[0]).not.toBe(colours[1])
  })

  it('sizes each segment by that person share of the bucket', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    const segments = wrapper.findAll('[data-testid="spend-chart"] li')[0].findAll('[data-testid="bar-segment"]')
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

    const label = wrapper.find('[data-testid="spend-chart"]').attributes('aria-label')
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

  /**
   * Asking a bar how much, when, and who.
   *
   * A bar says how one stretch of time compares with the others and nothing else.
   * The same question the pie answers, and on a phone the only way to ask is a
   * tap, so the whole column is the target rather than the bar.
   */
  describe('asking about a bar', () => {
    it('says nothing until a bar is asked about', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      // A line repeating the total from the card above is furniture.
      expect(wrapper.find('[data-testid="bar-readout"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="key-amount"]').exists()).toBe(false)
    })

    it('answers with the date and the total when a bar is hovered', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      await wrapper.findAll('[data-testid="bar"]')[0].trigger('mouseenter')

      const readout = wrapper.find('[data-testid="bar-readout"]').text()
      expect(readout).toMatch(/Jan/)
      expect(readout).toContain('100.00')
    })

    it('says what each person paid in that bar', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      await wrapper.findAll('[data-testid="bar"]')[0].trigger('mouseenter')

      const key = wrapper.find('[data-testid="chart-key"]').text().replace(/\s+/g, ' ')
      expect(key).toContain('Alice')
      expect(key).toContain('60.00')
      expect(key).toContain('60%')
      expect(key).toContain('Bob')
      expect(key).toContain('40.00')
      expect(key).toContain('40%')
    })

    it('dims whoever paid nothing in that bar', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      // February was Alice alone.
      await wrapper.findAll('[data-testid="bar"]')[1].trigger('mouseenter')

      const rows = wrapper.find('[data-testid="chart-key"]').findAll('li')
      expect(rows[0].classes()).not.toContain('opacity-40')
      expect(rows[1].classes()).toContain('opacity-40')
    })

    it('dims the other bars, so the one asked about is obvious', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      await wrapper.findAll('[data-testid="bar"]')[0].trigger('mouseenter')

      const bars = wrapper.findAll('[data-testid="bar"]')
      expect(bars[0].classes()).not.toContain('opacity-40')
      expect(bars[1].classes()).toContain('opacity-40')
    })

    it('puts the heading back when the pointer leaves', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })
      const bar = wrapper.findAll('[data-testid="bar"]')[0]

      await bar.trigger('mouseenter')
      await bar.trigger('mouseleave')

      expect(wrapper.find('[data-testid="bar-readout"]').exists()).toBe(false)
    })

    it('answers a tap, which is all a phone has', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      await wrapper.findAll('[data-testid="bar"]')[1].trigger('click')

      expect(wrapper.find('[data-testid="bar-readout"]').text()).toContain('50.00')
    })

    it('keeps a tapped bar on show after the pointer leaves', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })
      const bar = wrapper.findAll('[data-testid="bar"]')[0]

      await bar.trigger('mouseenter')
      await bar.trigger('click')
      await bar.trigger('mouseleave')

      // A tap is a decision, not a passing glance.
      expect(wrapper.find('[data-testid="bar-readout"]').text()).toContain('100.00')
    })

    it('switches to another bar when that one is clicked', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })
      const bars = wrapper.findAll('[data-testid="bar"]')

      await bars[0].trigger('mouseenter')
      await bars[0].trigger('click')
      // What a pointer really does: it is over the second one before the click.
      await bars[0].trigger('mouseleave')
      await bars[1].trigger('mouseenter')
      await bars[1].trigger('click')

      // Treating hover and click as one state made this read as clicking the one
      // already chosen, so it cleared instead of switching.
      expect(wrapper.find('[data-testid="bar-readout"]').text()).toContain('50.00')
    })

    it('lets a second tap put the heading back', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })
      const bar = wrapper.findAll('[data-testid="bar"]')[0]

      await bar.trigger('mouseenter')
      await bar.trigger('click')
      await bar.trigger('click')

      expect(wrapper.find('[data-testid="bar-readout"]').exists()).toBe(false)
    })

    it('answers keyboard focus, which is the only way in without a pointer', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      await wrapper.findAll('[data-testid="bar"]')[0].trigger('focus')

      expect(wrapper.find('[data-testid="bar-readout"]').text()).toContain('100.00')
    })

    it('names each bar for a screen reader, which cannot hover one', async () => {
      const { wrapper } = await mountView(StatsView, { api: api() })

      const label = wrapper.findAll('[data-testid="bar"]')[0].attributes('aria-label')
      expect(label).toContain('Alice')
      expect(label).toContain('100.00')
    })
  })

  /**
   * The chart's axis is time, not a list of the days something happened on.
   *
   * Two bars side by side could otherwise be a day apart or a month, and a quiet
   * fortnight would look exactly like a busy one.
   */
  describe('the shape of the axis', () => {
    const daily = (overrides: Record<string, unknown> = {}) =>
      fakeApi({
        '/stats': () => dashboard({
          spendOverTime: [
            {
              bucket: '2026-01-01',
              amount: 100,
              expenseCount: 1,
              byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 100 }],
            },
            {
              bucket: '2026-01-04',
              amount: 50,
              expenseCount: 1,
              byMember: [{ memberId: 'm2', memberName: 'Bob', amount: 50 }],
            },
          ],
          ...overrides,
        }),
        '/groups': () => [testGroup()],
      })

    const atGranularity = async (value: string, api = daily()) => {
      const mounted = await mountView(StatsView, { api })
      await mounted.wrapper.findAll('select')[1].setValue(value)
      await settle()
      return mounted.wrapper
    }

    it('shows the days nothing was spent on', async () => {
      const wrapper = await atGranularity('day')

      // The second and the third of January had nothing, and they are still days.
      expect(wrapper.findAll('[data-testid="bar"]')).toHaveLength(4)
      expect(wrapper.findAll('[data-testid="bar-empty"]')).toHaveLength(2)
      expect(wrapper.findAll('[data-testid="bar-fill"]')).toHaveLength(2)
    })

    it('draws an empty day as a line on the floor, not a small bar', async () => {
      const wrapper = await atGranularity('day')

      // A floor height in somebody's colour would read as a small expense.
      const empty = wrapper.find('[data-testid="bar-empty"]')
      expect(empty.classes()).toContain('h-0.5')
      expect(empty.findAll('[data-testid="bar-segment"]')).toHaveLength(0)
    })

    it('answers an empty day with nothing spent', async () => {
      const wrapper = await atGranularity('day')

      await wrapper.findAll('[data-testid="bar"]')[1].trigger('mouseenter')

      const readout = wrapper.find('[data-testid="bar-readout"]').text()
      expect(readout).toMatch(/2 Jan|Jan 2/)
      expect(readout).toContain('0.00')
    })

    it('gives the bars less air the more of them there are', async () => {
      const roomy = await mountView(StatsView, { api: daily() })
      expect(roomy.wrapper.find('[data-testid="spend-chart"]').classes()).toContain('gap-1')

      // A daily chart of a quarter is a hundred bars, and 4px between each of them
      // is more gap than chart.
      const busy = await atGranularity('day', fakeApi({
        '/stats': () => dashboard({
          spendOverTime: [
            {
              bucket: '2026-01-01',
              amount: 100,
              expenseCount: 1,
              byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 100 }],
            },
            {
              bucket: '2026-03-01',
              amount: 50,
              expenseCount: 1,
              byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 50 }],
            },
          ],
        }),
        '/groups': () => [testGroup()],
      }))
      expect(busy.find('[data-testid="spend-chart"]').classes()).toContain('gap-px')
    })

    it('says what a week covers when one is asked about', async () => {
      const wrapper = await atGranularity('week', fakeApi({
        '/stats': () => dashboard({
          spendOverTime: [
            {
              bucket: '2026-05-11',
              amount: 100,
              expenseCount: 1,
              byMember: [{ memberId: 'm1', memberName: 'Alice', amount: 100 }],
            },
          ],
        }),
        '/groups': () => [testGroup()],
      }))

      await wrapper.findAll('[data-testid="bar"]')[0].trigger('mouseenter')

      // A bar labelled by its Monday says nothing about where it ends.
      const readout = wrapper.find('[data-testid="bar-readout"]').text()
      expect(readout).toMatch(/11/)
      expect(readout).toMatch(/17/)
      expect(readout).toContain(' - ')
    })

    it('names a month and nothing else', async () => {
      const wrapper = await atGranularity('month')

      // Not "Jan 26": the year is the same for every bar beside it.
      expect(wrapper.find('[data-testid="chart-dates"]').text()).toContain('January')
      expect(wrapper.find('[data-testid="chart-dates"]').text()).not.toContain('26')
    })
  })

  it('labels a bucket by its own calendar date, not by an instant', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    // Read as midnight UTC and rendered west of it, the first of January became
    // the last of December: every label on a monthly chart was a month out.
    const dates = wrapper.find('[data-testid="chart-dates"]').text()
    expect(dates).toContain('Jan')
    expect(dates).toContain('Feb')
    expect(dates).not.toContain('Dec')
  })

  it('puts the dates under the graph, above the names', async () => {
    const { wrapper } = await mountView(StatsView, { api: api() })

    // Against the bars they belong to, rather than adrift under the key.
    const html = wrapper.html()
    const chart = html.indexOf('data-testid="spend-chart"')
    const dates = html.indexOf('data-testid="chart-dates"')
    const key = html.indexOf('data-testid="chart-key"')

    expect(dates).toBeGreaterThan(chart)
    expect(key).toBeGreaterThan(dates)
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

  it('works out the stats on this device when the server cannot be reached', async () => {
    const client = fakeApi()
    client.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(StatsView, {
      api: client,
      groups: [testGroup()],
      expenses: [
        testExpense({ id: 'e1', paidByMemberId: ALICE, amount: 60, amountInBaseCurrency: 60 }),
        testExpense({ id: 'e2', paidByMemberId: BOB, amount: 40, amountInBaseCurrency: 40 }),
      ],
    })
    await settle()

    // Every number here is arithmetic over rows this device already holds, so
    // saying "stats need a connection" was refusing to add up what it had.
    const text = textOf(wrapper)
    expect(text).toContain('100.00')
    expect(text).toContain('Total')
    expect(wrapper.find('[data-testid="spend-chart"]').exists()).toBe(true)
    // And it says it is offline, because that is worth knowing.
    expect(text).toContain('Offline')
  })

  it('says there is nothing to add up when there are no groups at all', async () => {
    const client = fakeApi({ '/groups': () => [] })
    client.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(StatsView, { api: client, groups: [] })
    await settle()

    expect(textOf(wrapper)).toContain('Nothing to add up yet')
  })

  it('leaves the chart out when there is nothing to plot', async () => {
    const { wrapper } = await mountView(StatsView, { api: api({ spendOverTime: [] }) })

    expect(wrapper.find('[data-testid="spend-chart"]').exists()).toBe(false)
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

  it('shows a view control on an entry that opens something', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({ items: [entry({})] }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    const row = wrapper.find('[data-testid="activity-row"]')
    const view = row.find('[data-testid="activity-view"]')
    expect(view.exists()).toBe(true)
    expect(view.text()).toBe('View')
    // Not a button: the card is already the link, and a button inside a link is
    // neither valid nor predictable. It is hidden from a screen reader, which is
    // being handed the link itself.
    expect(view.element.tagName).toBe('SPAN')
    expect(view.attributes('aria-hidden')).toBe('true')
  })

  it('shows no view control where there is nothing to open', async () => {
    const { wrapper } = await mountView(ActivityView, {
      api: fakeApi({
        '/activity': () => ({ items: [entry({ groupId: null, subjectId: null })] }),
        '/groups': () => [testGroup()],
      }),
    })
    await settle()

    // A card that offers to show you something and then does nothing is worse
    // than one that offers nothing.
    expect(wrapper.find('[data-testid="activity-row"]').attributes('data-linked')).toBe('false')
    expect(wrapper.find('[data-testid="activity-view"]').exists()).toBe(false)
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
