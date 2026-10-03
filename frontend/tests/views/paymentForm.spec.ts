import { describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { RouterLinkStub } from '@vue/test-utils'
import PaymentFormView from '@/views/PaymentFormView.vue'
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
import { today } from '@/domain/lastExpenseDate'

const push = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {}, fullPath: '/add/payment' }),
  useRouter: () => ({ push, replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

describe('recording a payment', () => {
  async function mountForm() {
    const mounted = await mountView(PaymentFormView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      groups: [testGroup()],
    })
    await settle()
    return mounted
  }

  it('starts with the other person paying you, which is why the screen is open', async () => {
    const { wrapper } = await mountForm()

    expect((wrapper.find('[data-testid="paid-from"]').element as HTMLSelectElement).value).toBe(BOB)
    expect((wrapper.find('[data-testid="paid-to"]').element as HTMLSelectElement).value).toBe(ALICE)
  })

  it('writes a settlement, and no expense at all', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('[data-testid="amount"]').setValue('50')
    await wrapper.find('form').trigger('submit')
    await settle()

    const [settlement] = expensesStore.settlementsForGroup(GROUP_ID)
    expect(settlement).toMatchObject({ fromMemberId: BOB, toMemberId: ALICE, amount: 50 })

    expect(expensesStore.forGroup(GROUP_ID)).toEqual([])
  })

  it('records it on the day it says', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('[data-testid="amount"]').setValue('50')
    await wrapper.find('[data-testid="paid-on"]').setValue('2026-08-06')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.settlementsForGroup(GROUP_ID)[0].settledAt).toContain('2026-08-06')
  })

  it('opens on today', async () => {
    const { wrapper } = await mountForm()

    expect((wrapper.find('[data-testid="paid-on"]').element as HTMLInputElement).value).toBe(today())
  })

  it('turns the payment round in a tap', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('[data-testid="swap-sides"]').trigger('click')
    await wrapper.find('[data-testid="amount"]').setValue('50')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.settlementsForGroup(GROUP_ID)[0]).toMatchObject({
      fromMemberId: ALICE,
      toMemberId: BOB,
    })
  })

  it('asks its questions where the expense form asks them', async () => {
    const { wrapper } = await mountForm()

    const order = wrapper
      .findAll('[data-testid]')
      .map((element) => element.attributes('data-testid'))
      .filter((id) => ['amount', 'paid-on', 'note', 'group', 'paid-from', 'paid-to'].includes(id!))

    expect(order).toEqual(['amount', 'paid-on', 'note', 'group', 'paid-from', 'paid-to'])
  })

  it('says which way the balance will move before it moves', async () => {
    const { wrapper } = await mountForm()

    await wrapper.find('[data-testid="amount"]').setValue('50')
    await settle()

    expect(wrapper.find('[data-testid="payment-effect"]').text()).toBe(
      'Bob owes Alice that much less.',
    )
  })

  it('refuses an amount nobody typed', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.settlementsForGroup(GROUP_ID)).toEqual([])
    expect(textOf(wrapper)).toContain('Enter an amount')
  })

  it('refuses a payment from somebody to themselves', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('[data-testid="amount"]').setValue('50')
    await wrapper.find('[data-testid="paid-to"]').setValue(BOB)
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.settlementsForGroup(GROUP_ID)).toEqual([])
    expect(textOf(wrapper)).toContain('two different people')
  })
})

describe('recording a settlement', () => {
  const BOB_USER = 'user-bob'
  const TRIP_ID = 'group-trip'

  const SettlementForm = defineComponent({
    render: () => h(PaymentFormView, { kind: 'settlement' }),
  })

  const withBob = (overrides = {}) => {
    const base = testGroup(overrides)
    return {
      ...base,
      members: base.members!.map((member) =>
        member.id === BOB ? { ...member, userId: BOB_USER, isPlaceholder: false } : member,
      ),
    }
  }

  const trip = () => ({
    ...withBob({ id: TRIP_ID, name: 'World tour' }),
    members: withBob().members.map((member) => ({ ...member, id: `${member.id}-trip` })),
  })

  async function mountForm(expenses = [testExpense({ paidByMemberId: BOB })]) {
    const mounted = await mountView(SettlementForm, {
      api: fakeApi({
        [`/groups/${TRIP_ID}`]: () => trip(),
        [`/groups/${GROUP_ID}`]: () => withBob(),
        '/groups': () => withBob(),
        '/settlements/move': () => ({}),
      }),
      groups: [withBob(), trip()],
      expenses,
    })
    await settle()
    return mounted
  }

  it('starts with whoever owes paying off the whole balance', async () => {
    const { wrapper } = await mountForm()

    expect(wrapper.find('[data-testid="record-settlement"]').attributes('aria-current')).toBe('page')
    expect((wrapper.find('[data-testid="paid-from"]').element as HTMLSelectElement).value).toBe(ALICE)
    expect((wrapper.find('[data-testid="paid-to"]').element as HTMLSelectElement).value).toBe(BOB)
    expect((wrapper.find('[data-testid="amount"]').element as HTMLInputElement).value).toBe('30.00')
  })

  it('fills in what the simplified balance says, not who paid for whom', async () => {
    const CAROL = 'member-carol'
    const group = {
      ...withBob(),
      members: [
        ...withBob().members,
        { ...withBob().members[1], id: CAROL, userId: 'user-carol', displayName: 'Carol' },
      ],
    }
    const only = (memberId: string) => [
      { memberId, amount: 60, amountInBaseCurrency: 60, inputValue: null },
    ]

    const { wrapper } = await mountView(SettlementForm, {
      api: fakeApi({ [`/groups/${GROUP_ID}`]: () => group, '/groups': () => group }),
      groups: [group],
      expenses: [
        testExpense({ id: 'carol-for-bob', paidByMemberId: CAROL, splits: only(BOB) }),
        testExpense({ id: 'bob-for-alice', paidByMemberId: BOB, splits: only(ALICE) }),
      ],
    })
    await settle()

    expect((wrapper.find('[data-testid="paid-from"]').element as HTMLSelectElement).value).toBe(ALICE)
    expect((wrapper.find('[data-testid="paid-to"]').element as HTMLSelectElement).value).toBe(CAROL)
    expect((wrapper.find('[data-testid="amount"]').element as HTMLInputElement).value).toBe('60.00')
  })

  it('writes an ordinary settlement when it stays in the group', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.settlementsForGroup(GROUP_ID)[0]).toMatchObject({
      fromMemberId: ALICE,
      toMemberId: BOB,
      amount: 30,
    })
  })

  it('keeps an amount somebody typed', async () => {
    const { wrapper } = await mountForm()

    await wrapper.find('[data-testid="amount"]').setValue('12')
    await wrapper.find('[data-testid="swap-sides"]').trigger('click')
    await wrapper.find('[data-testid="swap-sides"]').trigger('click')
    await settle()

    expect((wrapper.find('[data-testid="amount"]').element as HTMLInputElement).value).toBe('12')
  })

  it('says so when nothing is owed that way round', async () => {
    const { wrapper } = await mountForm()

    await wrapper.find('[data-testid="swap-sides"]').trigger('click')
    await settle()

    expect(wrapper.find('[data-testid="nothing-owed"]').text()).toBe(
      'Nothing owed between them in Roommates.',
    )
  })

  it('moves it through the server as one step, writing nothing locally', async () => {
    const { wrapper, api, expensesStore } = await mountForm()

    await wrapper.find('[data-testid="move-elsewhere"]').setValue(true)
    await settle()

    expect(wrapper.find('[data-testid="payment-effect"]').text()).toBe(
      'Alice owes Bob that much less in Roommates and that much more in World tour. No money changes hands.',
    )

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/settlements/move',
      expect.objectContaining({
        groupId: GROUP_ID,
        fromMemberId: ALICE,
        toMemberId: BOB,
        amount: 30,
        targetGroupId: TRIP_ID,
      }),
    )
    expect(expensesStore.settlementsForGroup(GROUP_ID)).toEqual([])
    expect(push).toHaveBeenCalledWith({ name: 'group', params: { groupId: TRIP_ID } })
  })

  it('asks for the group first', async () => {
    const { wrapper } = await mountForm()

    const order = wrapper
      .findAll('[data-testid]')
      .map((element) => element.attributes('data-testid'))
      .filter((id) => ['group', 'move-elsewhere', 'paid-from', 'amount', 'note'].includes(id!))

    expect(order).toEqual(['group', 'move-elsewhere', 'paid-from', 'amount', 'note'])
  })

  it('offers the move before anybody has opened the other group', async () => {
    const { wrapper } = await mountView(SettlementForm, {
      api: fakeApi({
        [`/groups/${TRIP_ID}`]: () => trip(),
        [`/groups/${GROUP_ID}`]: () => withBob(),
        '/groups': () => withBob(),
      }),
      groups: [withBob(), { ...trip(), members: undefined }],
      expenses: [testExpense({ paidByMemberId: BOB })],
    })
    await settle()

    const box = wrapper.find('[data-testid="move-elsewhere"]')
    expect((box.element as HTMLInputElement).disabled).toBe(false)
  })

  it('shows the move but explains it when no other group has both people', async () => {
    const mounted = await mountView(SettlementForm, {
      api: fakeApi({ '/groups': () => testGroup() }),
      groups: [testGroup(), testGroup({ id: TRIP_ID, name: 'World tour' })],
    })
    await settle()

    const box = mounted.wrapper.find('[data-testid="move-elsewhere"]')
    expect((box.element as HTMLInputElement).disabled).toBe(true)
    expect(mounted.wrapper.find('[data-testid="no-target-group"]').text()).toBe(
      'No other group in CAD has both of them.',
    )
  })
})

describe('recording a plain payment', () => {
  it('never offers to move anything to another group', async () => {
    const mounted = await mountView(PaymentFormView, {
      api: fakeApi({ '/groups': () => testGroup() }),
      groups: [testGroup(), testGroup({ id: 'group-trip', name: 'World tour' })],
    })
    await settle()

    expect(mounted.wrapper.find('[data-testid="move-elsewhere"]').exists()).toBe(false)
    expect((mounted.wrapper.find('[data-testid="amount"]').element as HTMLInputElement).value).toBe('')
  })
})
