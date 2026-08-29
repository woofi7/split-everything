import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import SettleView from '@/views/SettleView.vue'
import { db } from '@/offline/db'
import { ALICE, BOB, GROUP_ID, fakeApi, mountView, settle, testExpense, testGroup, textOf, waitFor } from '../support/viewHarness'

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

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(await db.settlements.count()).toBe(0)
  })

  it('offers no suggestions when the group is already settled', async () => {
    query = {}

    const { wrapper } = await mountView(SettleView, { api: api() })

    expect(textOf(wrapper)).not.toContain('Suggested transfers')
  })
})
