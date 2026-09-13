import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ToastStack from '@/components/ui/ToastStack.vue'
import { clearToasts, notify, toasts } from '@/ui/toasts'

/**
 * Where the app says things: the top of the screen, over whatever is there.
 */
describe('the toast stack', () => {
  beforeEach(clearToasts)
  afterEach(clearToasts)

  const mountStack = () => mount(ToastStack, { global: { stubs: { teleport: true } } })

  it('shows one card per thing said', () => {
    notify('Saved.', 'done')
    notify('Could not save.', 'error')

    const wrapper = mountStack()
    const cards = wrapper.findAll('[data-testid="toast"]')

    expect(cards.map((card) => card.text())).toEqual(
      expect.arrayContaining([expect.stringContaining('Saved.')]),
    )
    expect(cards).toHaveLength(2)
  })

  it('interrupts for an error and waits its turn for anything else', () => {
    notify('Could not save.', 'error')
    notify('Saved.', 'done')

    const wrapper = mountStack()
    const [problem, confirmation] = wrapper.findAll('[data-testid="toast"]')

    // A screen reader is told about a failure at once; a confirmation can wait for
    // a gap, because nothing depends on hearing it.
    expect(problem.attributes('role')).toBe('alert')
    expect(problem.attributes('aria-live')).toBe('assertive')
    expect(confirmation.attributes('role')).toBe('status')
    expect(confirmation.attributes('aria-live')).toBe('polite')
  })

  it('goes away when it is tapped', async () => {
    notify('Could not save.', 'error')
    const wrapper = mountStack()

    await wrapper.find('[data-testid="toast"]').trigger('click')

    expect(toasts.value).toEqual([])
    expect(wrapper.find('[data-testid="toast"]').exists()).toBe(false)
  })

  it('renders nothing at all when there is nothing to say', () => {
    const wrapper = mountStack()

    expect(wrapper.find('[data-testid="toast"]').exists()).toBe(false)
  })
})
