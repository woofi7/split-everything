import { describe, expect, it } from 'vitest'
import { RouterLinkStub, mount } from '@vue/test-utils'
import AddKindSwitch from '@/components/expenses/AddKindSwitch.vue'

const mountSwitch = (current: 'expense' | 'payment' | 'settlement') =>
  mount(AddKindSwitch, {
    props: { current },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })

describe('choosing what to record', () => {
  it('offers all three, and marks the one being written', () => {
    const wrapper = mountSwitch('expense')

    expect(wrapper.find('[data-testid="record-expense"]').attributes('aria-current')).toBe('page')
    expect(wrapper.find('[data-testid="record-payment"]').attributes('aria-current')).toBeUndefined()
    expect(wrapper.find('[data-testid="record-settlement"]').attributes('aria-current')).toBeUndefined()
  })

  it('swaps the form without leaving a step behind to go back to', () => {
    const wrapper = mountSwitch('expense')

    for (const testid of ['record-expense', 'record-payment', 'record-settlement']) {
      expect(wrapper.find(`[data-testid="${testid}"]`).attributes('replace')).toBeDefined()
    }
  })
})
