import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import CollapsibleSection from '@/components/ui/CollapsibleSection.vue'

describe('a collapsible section', () => {
  const mountSection = (props = {}) =>
    mount(CollapsibleSection, {
      props: { title: 'Categories', ...props },
      slots: { default: '<p data-testid="inside">The list</p>' },
    })

  it('starts closed, and says what is inside without opening', () => {
    const wrapper = mountSection({ count: 14 })

    expect(wrapper.find('[data-testid="inside"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Categories')
    expect(wrapper.find('[data-testid="section-count"]').text()).toBe('14')
  })

  it('opens and shuts on the heading', async () => {
    const wrapper = mountSection()
    const toggle = () => wrapper.find('[data-testid="section-toggle"]')

    await toggle().trigger('click')
    expect(wrapper.find('[data-testid="inside"]').exists()).toBe(true)
    expect(toggle().attributes('aria-expanded')).toBe('true')

    await toggle().trigger('click')
    expect(wrapper.find('[data-testid="inside"]').exists()).toBe(false)
    expect(toggle().attributes('aria-expanded')).toBe('false')
  })

  it('can be asked to start open', () => {
    expect(mountSection({ open: true }).find('[data-testid="inside"]').exists()).toBe(true)
  })

  it('says nothing where there is no count to say', () => {
    expect(mountSection().find('[data-testid="section-count"]').exists()).toBe(false)
  })

  it('names its own controls, so two sections on a screen are told apart', async () => {
    const wrapper = mountSection({ testid: 'categories' })

    expect(wrapper.find('[data-testid="categories-toggle"]').exists()).toBe(true)
    await wrapper.find('[data-testid="categories-toggle"]').trigger('click')
    expect(wrapper.find('[data-testid="categories"]').exists()).toBe(true)
  })
})
