import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import IconPicker from '@/components/ui/IconPicker.vue'
import { ICONS } from '@/domain/icons'

/**
 * The picker teleports to the body so it escapes any parent that clips or
 * establishes a stacking context. Stubbing the teleport renders it inline, which
 * is what lets the wrapper query it; attaching to the body keeps focus real.
 */
function mountPicker(props: Record<string, unknown> = {}) {
  return mount(IconPicker, {
    props: { open: true, modelValue: null, ...props },
    attachTo: document.body,
    global: { stubs: { teleport: true } },
  })
}

const optionsOf = (wrapper: ReturnType<typeof mountPicker>) =>
  wrapper.findAll('[role="option"]').map((option) => option.attributes('data-icon'))

const type = async (wrapper: ReturnType<typeof mountPicker>, value: string) => {
  await wrapper.find('input[type="search"]').setValue(value)
  await nextTick()
}

describe('IconPicker', () => {
  it('renders nothing while closed', () => {
    const wrapper = mountPicker({ open: false })

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('shows every icon when opened', () => {
    const wrapper = mountPicker()

    expect(optionsOf(wrapper)).toHaveLength(ICONS.length)
  })

  it('groups the icons into sections before anything is typed', () => {
    const wrapper = mountPicker()

    const headings = wrapper.findAll('h3').map((heading) => heading.text())
    expect(headings).toContain('Home')
    expect(headings).toContain('Transport')
  })

  it('drops the sections once there is a query, so relevance decides the order', () => {
    const wrapper = mountPicker()

    return type(wrapper, 'car').then(() => {
      // Sections would push a strong match below a weaker one in an earlier group.
      expect(wrapper.findAll('h3')).toHaveLength(0)
    })
  })

  it('filters as you type', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'house')

    const names = optionsOf(wrapper)
    expect(names[0]).toBe('house')
    expect(names.length).toBeLessThan(ICONS.length)
  })

  it('finds an icon by keyword rather than name', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'hydro')

    expect(optionsOf(wrapper)).toContain('bolt')
  })

  it('matches loosely, so an abbreviation works', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'grcr')

    expect(optionsOf(wrapper)).toContain('cart-shopping')
  })

  it('counts what is showing', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'house')

    const count = optionsOf(wrapper).length
    expect(wrapper.find('[aria-live="polite"]').text()).toContain(String(count))
  })

  it('uses the singular for a single result', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'passport')

    expect(wrapper.find('[aria-live="polite"]').text()).toBe('1 icon')
  })

  it('says so when nothing matches, and suggests what to do', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'zzzzqqq')

    expect(optionsOf(wrapper)).toHaveLength(0)
    expect(wrapper.text()).toContain('Try a plainer word')
  })

  it('highlights the characters that matched', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'hse')

    // Showing why a result matched is what stops fuzzy search feeling random.
    const marks = wrapper.findAll('mark').map((mark) => mark.text())
    expect(marks.length).toBeGreaterThan(0)
  })

  it('captions a keyword match with the keyword that matched', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'hydro')

    const bolt = wrapper.find('[data-icon="bolt"]')
    expect(bolt.text().toLowerCase()).toContain('hydro')
  })

  it('emits the chosen name and closes', async () => {
    const wrapper = mountPicker()

    await wrapper.find('[data-icon="house"]').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['house']])
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('marks the current selection', () => {
    const wrapper = mountPicker({ modelValue: 'house' })

    expect(wrapper.find('[data-icon="house"]').attributes('aria-selected')).toBe('true')
    expect(wrapper.find('[data-icon="car"]').attributes('aria-selected')).toBe('false')
  })

  it('shows the current selection in the footer', () => {
    const wrapper = mountPicker({ modelValue: 'house' })

    expect(wrapper.find('footer').text()).toContain('House')
  })

  it('says there is no icon when none is chosen', () => {
    const wrapper = mountPicker({ modelValue: null })

    expect(wrapper.find('footer').text()).toContain('No icon')
  })

  it('can remove the icon', async () => {
    const wrapper = mountPicker({ modelValue: 'house' })

    await wrapper.find('footer button').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[null]])
  })

  it('cannot remove an icon that is not set', () => {
    const wrapper = mountPicker({ modelValue: null })

    expect(wrapper.find('footer button').attributes('disabled')).toBeDefined()
  })

  it('closes on the close button', async () => {
    const wrapper = mountPicker()

    await wrapper.find('[aria-label="Close"]').trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('closes when the backdrop is tapped', async () => {
    const wrapper = mountPicker()

    await wrapper.find('[aria-hidden="true"]').trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('closes on escape', async () => {
    const wrapper = mountPicker()

    await wrapper.find('[role="dialog"]').trigger('keydown', { key: 'Escape' })

    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('does not emit a selection when it is dismissed', async () => {
    const wrapper = mountPicker()

    await wrapper.find('[aria-label="Close"]').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('announces itself as a modal dialog', () => {
    const wrapper = mountPicker()

    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.attributes('aria-modal')).toBe('true')
    expect(dialog.attributes('aria-labelledby')).toBe('icon-picker-title')
    expect(wrapper.find('#icon-picker-title').exists()).toBe(true)
  })

  it('takes a custom title', () => {
    const wrapper = mountPicker({ title: 'Category icon' })

    expect(wrapper.find('#icon-picker-title').text()).toBe('Category icon')
  })

  it('focuses the search box when it opens, since typing is the fast path', async () => {
    const wrapper = mountPicker({ open: false })

    await wrapper.setProps({ open: true })
    await nextTick()
    await nextTick()

    expect(document.activeElement).toBe(wrapper.find('input[type="search"]').element)
  })

  it('gives focus back to whatever opened it', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()

    const wrapper = mountPicker({ open: false })
    await wrapper.setProps({ open: true })
    await nextTick()

    await wrapper.setProps({ open: false })
    await nextTick()

    // Otherwise a keyboard user is dumped at the top of the page.
    expect(document.activeElement).toBe(trigger)
    trigger.remove()
  })

  it('clears the query each time it opens', async () => {
    const wrapper = mountPicker({ open: false })

    await wrapper.setProps({ open: true })
    await type(wrapper, 'house')
    await wrapper.setProps({ open: false })
    await wrapper.setProps({ open: true })
    await nextTick()

    expect((wrapper.find('input[type="search"]').element as HTMLInputElement).value).toBe('')
  })
})

describe('IconPicker keyboard navigation', () => {
  const press = async (wrapper: ReturnType<typeof mountPicker>, key: string) => {
    await wrapper.find('[role="dialog"]').trigger('keydown', { key })
    await nextTick()
  }

  const activeName = (wrapper: ReturnType<typeof mountPicker>) =>
    wrapper.find('[data-active="true"]').attributes('data-icon')

  it('starts on the current selection', () => {
    const wrapper = mountPicker({ modelValue: 'car' })

    expect(activeName(wrapper)).toBe('car')
  })

  it('starts on the first icon when nothing is selected', () => {
    const wrapper = mountPicker()

    expect(activeName(wrapper)).toBe(ICONS[0].name)
  })

  it('moves right and left', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'ArrowRight')
    expect(activeName(wrapper)).toBe(ICONS[1].name)

    await press(wrapper, 'ArrowLeft')
    expect(activeName(wrapper)).toBe(ICONS[0].name)
  })

  it('moves a whole row on down and up', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'ArrowDown')
    expect(activeName(wrapper)).toBe(ICONS[6].name)

    await press(wrapper, 'ArrowUp')
    expect(activeName(wrapper)).toBe(ICONS[0].name)
  })

  it('wraps rather than dead-ending', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'ArrowLeft')

    expect(activeName(wrapper)).toBe(ICONS[ICONS.length - 1].name)
  })

  it('chooses the highlighted icon on enter', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'ArrowRight')
    await press(wrapper, 'Enter')

    expect(wrapper.emitted('update:modelValue')).toEqual([[ICONS[1].name]])
  })

  it('lets you type then press enter for the top match', async () => {
    const wrapper = mountPicker()

    await wrapper.find('input[type="search"]').setValue('groceries')
    await nextTick()
    await press(wrapper, 'Enter')

    expect(wrapper.emitted('update:modelValue')).toEqual([['cart-shopping']])
  })

  it('resets the highlight to the top match when the query changes', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'ArrowDown')
    await wrapper.find('input[type="search"]').setValue('house')
    await nextTick()

    expect(activeName(wrapper)).toBe('house')
  })

  it('does nothing on enter when nothing matches', async () => {
    const wrapper = mountPicker()

    await wrapper.find('input[type="search"]').setValue('zzzzqqq')
    await nextTick()
    await press(wrapper, 'Enter')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('ignores arrow keys when nothing matches', async () => {
    const wrapper = mountPicker()

    await wrapper.find('input[type="search"]').setValue('zzzzqqq')
    await nextTick()
    await press(wrapper, 'ArrowRight')

    expect(wrapper.find('[data-active="true"]').exists()).toBe(false)
  })

  it('leaves other keys alone so typing still reaches the search box', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'a')

    expect(wrapper.emitted('close')).toBeUndefined()
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('points the search box at the highlighted option for screen readers', async () => {
    const wrapper = mountPicker()

    await press(wrapper, 'ArrowRight')

    expect(wrapper.find('input[type="search"]').attributes('aria-activedescendant')).toBe(
      `icon-option-${ICONS[1].name}`,
    )
  })

  it('keeps tab inside the dialog', async () => {
    const wrapper = mountPicker()

    const focusable = wrapper.findAll('button, input')
    const last = focusable[focusable.length - 1].element as HTMLElement
    last.focus()

    await wrapper.find('[role="dialog"]').trigger('keydown', { key: 'Tab' })
    await nextTick()

    // A modal that lets Tab escape lets a keyboard user operate what they cannot see.
    expect(document.activeElement).not.toBe(last)
  })

  it('wraps tab backwards from the first control', async () => {
    const wrapper = mountPicker()

    const first = wrapper.find('button, input').element as HTMLElement
    first.focus()

    await wrapper.find('[role="dialog"]').trigger('keydown', { key: 'Tab', shiftKey: true })
    await nextTick()

    expect(document.activeElement).not.toBe(first)
  })
})
