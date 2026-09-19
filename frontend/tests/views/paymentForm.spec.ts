import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import PaymentFormView from '@/views/PaymentFormView.vue'
import {
  ALICE,
  BOB,
  GROUP_ID,
  fakeApi,
  mountView,
  settle,
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
