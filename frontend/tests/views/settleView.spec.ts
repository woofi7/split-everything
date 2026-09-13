import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import SettleView from '@/views/SettleView.vue'
import { db } from '@/offline/db'
import { ALICE, BOB, GROUP_ID, fakeApi, mountView, settle, testExpense, testGroup, textOf, waitFor, saidOnScreen } from '../support/viewHarness'

const replace = vi.fn()
let query: Record<string, string> = {}

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { groupId: GROUP_ID }, query }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

const api = () => fakeApi({ '/groups': () => testGroup() })

describe('SettleView', () => {
  it('prefills from the transfer that was tapped', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
    })

    const selects = wrapper.findAll('select')
    expect((selects[0].element as HTMLSelectElement).value).toBe(BOB)
    expect((selects[1].element as HTMLSelectElement).value).toBe(ALICE)
    expect((wrapper.find('input[inputmode="decimal"]').element as HTMLInputElement).value).toBe('30')
  })

  it('suggests the transfers that would settle the group', async () => {
    query = {}

    const { wrapper } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(textOf(wrapper)).toContain('Suggested transfers')
    expect(textOf(wrapper)).toContain('Bob pays Alice')
  })

  it('fills the form from a suggestion', async () => {
    query = {}

    const { wrapper } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
    })

    const use = wrapper.findAll('button').find((button) => button.text().includes('Use'))
    await use!.trigger('click')
    await settle(1)

    expect((wrapper.find('input[inputmode="decimal"]').element as HTMLInputElement).value).toBe('30')
  })

  it('records the settlement locally and returns to the group', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper, expensesStore } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('form').trigger('submit')
    // The redirect is the last thing to happen, so it is the only safe signal
    // that the whole action finished.
    await waitFor(() => replace.mock.calls.length > 0)

    expect(await db.settlements.count()).toBe(1)
    expect(expensesStore.balanceFor(GROUP_ID).every((b) => Math.abs(b.net) < 0.01)).toBe(true)
    expect(replace).toHaveBeenCalledWith({ name: 'group', params: { groupId: GROUP_ID } })
  })

  it('saves a note with the settlement', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
    })

    const inputs = wrapper.findAll('input[type="text"]')
    await inputs[inputs.length - 1].setValue('Etransfer')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect((await db.settlements.toArray())[0].note).toBe('Etransfer')
  })

  it('keeps the submit disabled until there is an amount', async () => {
    query = {}

    const { wrapper } = await mountView(SettleView, { api: api() })

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('reports a settlement between the same member instead of saving it', async () => {
    query = { from: ALICE, to: ALICE, amount: '10' }

    const { wrapper } = await mountView(SettleView, { api: api() })

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(saidOnScreen()).not.toHaveLength(0)
    expect(await db.settlements.count()).toBe(0)
  })

  it('offers no suggestions when the group is already settled', async () => {
    query = {}

    const { wrapper } = await mountView(SettleView, { api: api() })

    expect(textOf(wrapper)).not.toContain('Suggested transfers')
  })
})

/**
 * Two people who share more than one group can owe each other in both directions
 * at once, and paying both in full is two transfers where none is needed. The
 * settle screen is already about settling with somebody, so it is where the app
 * says so.
 */
describe('SettleView cancelling debts across groups', () => {
  /** Bob with an account of his own, since a placeholder exists in one group only. */
  const withAccounts = () => {
    const group = testGroup()
    group.members = group.members.map((member) =>
      member.id === BOB ? { ...member, userId: 'user-bob', isPlaceholder: false } : member,
    )
    return group
  }

  const facingBalance = {
    withUserId: 'user-bob',
    withName: 'Bob',
    groups: [
      { groupId: GROUP_ID, groupName: 'Roommates', currency: 'CAD', net: 1025, canSettle: true },
      { groupId: 'group-2', groupName: 'Ski trip', currency: 'CAD', net: -925, canSettle: true },
    ],
    offsets: [
      {
        owedGroupId: GROUP_ID,
        owedGroupName: 'Roommates',
        owingGroupId: 'group-2',
        owingGroupName: 'Ski trip',
        amount: 925,
        currency: 'CAD',
      },
    ],
    remaining: [{ currency: 'CAD', net: 100, groupId: GROUP_ID, groupName: 'Roommates' }],
  }

  const crossGroupApi = (balance: unknown = facingBalance) =>
    fakeApi({
      '/groups': () => withAccounts(),
      '/settlements/cross-group': () => balance,
      '/settlements/cross-group/offset': () => ({
        applied: facingBalance.offsets,
        remaining: facingBalance.remaining,
        settlementsRecorded: 2,
      }),
    })

  it('says what the two of them owe each other elsewhere', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper } = await mountView(SettleView, {
      api: crossGroupApi(),
      groups: [withAccounts()],
      expenses: [testExpense()],
    })
    await settle()

    const panel = wrapper.find('[data-testid="cross-group"]')
    expect(panel.exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Ski trip')
    expect(textOf(wrapper)).toContain('1,025.00')
    expect(textOf(wrapper)).toContain('925.00')
  })

  it('offers to cancel out only the part that faces both ways', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper } = await mountView(SettleView, {
      api: crossGroupApi(),
      groups: [withAccounts()],
      expenses: [testExpense()],
    })
    await settle()

    expect(wrapper.find('[data-testid="offset-across-groups"]').text()).toContain('925.00')
  })

  it('says where the difference ended up once it has', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper, api } = await mountView(SettleView, {
      api: crossGroupApi(),
      groups: [withAccounts()],
      expenses: [testExpense()],
    })
    await settle()

    await wrapper.find('[data-testid="offset-across-groups"]').trigger('click')
    await settle()

    expect(api.post).toHaveBeenCalledWith('/settlements/cross-group/offset', {
      withUserId: 'user-bob',
      note: null,
    })
    await waitFor(() => saidOnScreen().length > 0)
    expect(saidOnScreen().join(' ')).toContain('100.00')
    expect(saidOnScreen().join(' ')).toContain('Roommates')
  })

  it('says nothing at all when there is nothing facing the other way', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    const { wrapper } = await mountView(SettleView, {
      api: crossGroupApi({ ...facingBalance, offsets: [], remaining: [] }),
      groups: [withAccounts()],
      expenses: [testExpense()],
    })
    await settle()

    // One group between two people is the ordinary case and needs no explaining.
    expect(wrapper.find('[data-testid="cross-group"]').exists()).toBe(false)
  })

  it('says nothing for somebody who has no account to look up', async () => {
    query = { from: BOB, to: ALICE, amount: '30' }

    // Bob as a placeholder: he exists in this group and nowhere else, so there is
    // nothing of his to find in another.
    const { wrapper, api } = await mountView(SettleView, {
      api: fakeApi({ '/groups': () => testGroup(), '/settlements/cross-group': () => facingBalance }),
      groups: [testGroup()],
      expenses: [testExpense()],
    })
    await settle()

    expect(wrapper.find('[data-testid="cross-group"]').exists()).toBe(false)
    expect(api.get).not.toHaveBeenCalledWith('/settlements/cross-group', expect.anything())
  })
})

/**
 * A settlement could be recorded and never seen again: the balance moved, and
 * there was nothing on any screen saying why or letting anybody take it back. Two
 * people offset their debts across groups, the form kept the figure it had been
 * prefilled with, and the button underneath recorded the same debt a second time
 * six seconds later - with no way to undo it.
 */
describe('SettleView showing what is already settled', () => {
  const settled = (overrides: Record<string, unknown> = {}) => ({
    id: 'settlement-1',
    groupId: GROUP_ID,
    fromMemberId: BOB,
    toMemberId: ALICE,
    amount: 30,
    currency: 'CAD',
    amountInBaseCurrency: 30,
    settledAt: '2026-09-14T12:00:00Z',
    note: null,
    isDeleted: false,
    vectorClock: {},
    serverSeq: 1,
    pending: false,
    ...overrides,
  })

  it('lists what the group has already been told about', async () => {
    query = {}

    const { wrapper } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
      settlements: [settled({ note: 'Cancelled against Ski trip' })],
    })
    await settle()

    expect(wrapper.findAll('[data-testid="settlement-row"]')).toHaveLength(1)
    expect(textOf(wrapper)).toContain('Bob paid Alice')
    expect(textOf(wrapper)).toContain('Cancelled against Ski trip')
  })

  it('takes one back, and stops suggesting the transfer it paid', async () => {
    query = {}

    const { wrapper, expensesStore } = await mountView(SettleView, {
      api: api(),
      expenses: [testExpense()],
      settlements: [settled()],
    })
    await settle()

    await wrapper.find('[data-testid="unsettle-settlement-1"]').trigger('click')
    await settle()

    // A tombstone, so the other phone learns of it too rather than holding a
    // payment this one has forgotten.
    expect(expensesStore.settlementsForGroup(GROUP_ID)).toHaveLength(0)
    expect(await db.outbox.where('entityId').equals('settlement-1').count()).toBe(1)
  })

  it('empties the amount once an offset has cleared what it was for', async () => {
    query = { from: BOB, to: ALICE, amount: '925' }

    const group = testGroup()
    group.members = group.members.map((member) =>
      member.id === BOB ? { ...member, userId: 'user-bob', isPlaceholder: false } : member,
    )

    const { wrapper } = await mountView(SettleView, {
      api: fakeApi({
        '/groups': () => group,
        '/settlements/cross-group': () => ({
          withUserId: 'user-bob',
          withName: 'Bob',
          groups: [
            { groupId: GROUP_ID, groupName: 'Roommates', currency: 'CAD', net: -925, canSettle: true },
            { groupId: 'group-2', groupName: 'Ski trip', currency: 'CAD', net: 925, canSettle: true },
          ],
          offsets: [
            {
              owedGroupId: 'group-2',
              owedGroupName: 'Ski trip',
              owingGroupId: GROUP_ID,
              owingGroupName: 'Roommates',
              amount: 925,
              currency: 'CAD',
            },
          ],
          remaining: [{ currency: 'CAD', net: 0, groupId: null, groupName: null }],
        }),
        '/settlements/cross-group/offset': () => ({
          applied: [],
          remaining: [{ currency: 'CAD', net: 0, groupId: null, groupName: null }],
          settlementsRecorded: 2,
        }),
      }),
      groups: [group],
      expenses: [],
    })
    await settle()

    await wrapper.find('[data-testid="offset-across-groups"]').trigger('click')
    await settle()

    // The figure that was on screen has been cancelled, so the button below it
    // must not still be armed with it.
    expect((wrapper.find('input[inputmode="decimal"]').element as HTMLInputElement).value).toBe('')
  })
})
