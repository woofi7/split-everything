import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import CategoryPicker from '@/components/expenses/CategoryPicker.vue'
import { allInBody, inBody } from '../support/viewHarness'
import type { Category } from '@/domain/categories'

const category = (key: string, name: string, keywords: string[] = []): Category => ({
  key,
  name,
  iconName: 'tag',
  colorHex: '#64748b',
  sortOrder: 0,
  keywords,
})

const categories = [
  category('groceries', 'Groceries', ['metro', 'iga']),
  category('dining', 'Dining out', ['resto']),
  category('shopping', 'Shopping'),
]

describe('the category picker inside a label', () => {
  const inLabel = () =>
    mount(
      {
        render: () => h('label', {}, [h('span', 'Category'), h(CategoryPicker, { modelValue: null, categories })]),
      },
      { attachTo: document.body },
    )

  it('shuts when an option is chosen', async () => {
    const wrapper = inLabel()

    await wrapper.find('[data-testid="category"]').trigger('click')
    await inBody('[data-testid="category-option"]').trigger('click')

    expect(inBody('[data-testid="category-search"]').exists()).toBe(false)
  })

  it('shuts when Not filed is chosen', async () => {
    const wrapper = inLabel()

    await wrapper.find('[data-testid="category"]').trigger('click')
    await inBody('[data-testid="category-none"]').trigger('click')

    expect(inBody('[data-testid="category-search"]').exists()).toBe(false)
  })
})

describe('the category picker', () => {
  const mountPicker = (props: Partial<InstanceType<typeof CategoryPicker>['$props']> = {}) =>
    mount(CategoryPicker, { props: { modelValue: null, categories, ...props } })

  async function open(wrapper: ReturnType<typeof mountPicker>) {
    await wrapper.find('[data-testid="category"]').trigger('click')
    return wrapper
  }

  it('says what is chosen, and says when nothing is', async () => {
    expect(mountPicker().find('[data-testid="category"]').text()).toContain('Not filed')

    const filed = mountPicker({ modelValue: 'dining' })
    expect(filed.find('[data-testid="category"]').text()).toContain('Dining out')
  })

  it('shows the list before anything is typed', async () => {
    await open(mountPicker())

    expect(allInBody('[data-testid="category-option"]')).toHaveLength(3)
  })

  it('finds a category by a few letters of its name', async () => {
    await open(mountPicker())

    await inBody('[data-testid="category-search"]').setValue('dng')

    const found = allInBody('[data-testid="category-option"]')
    expect(found[0].attributes('data-value')).toBe('dining')
  })

  it('finds one by a word that files things there', async () => {
    await open(mountPicker())

    await inBody('[data-testid="category-search"]').setValue('metro')

    const found = allInBody('[data-testid="category-option"]')
    expect(found[0].attributes('data-value')).toBe('groceries')
    expect(found[0].text()).toContain('metro')
  })

  it('picks one, and says so', async () => {
    const wrapper = await open(mountPicker())

    await inBody('[data-value="dining"][data-testid="category-option"]').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['dining']])
    expect(inBody('[data-testid="category-search"]').exists()).toBe(false)
  })

  it('offers to make the one that is missing', async () => {
    const wrapper = await open(mountPicker())

    await inBody('[data-testid="category-search"]').setValue('Ski')

    const create = inBody('[data-testid="category-create"]')
    expect(create.text()).toContain('Ski')

    await create.trigger('click')
    expect(wrapper.emitted('create')).toEqual([['Ski']])
  })

  it('offers it even when something matched', async () => {
    await open(mountPicker())

    await inBody('[data-testid="category-search"]').setValue('Din')

    expect(allInBody('[data-testid="category-option"]').length).toBeGreaterThan(0)
    expect(inBody('[data-testid="category-create"]').exists()).toBe(true)
  })

  it('does not offer to make one that is already there', async () => {
    await open(mountPicker())

    await inBody('[data-testid="category-search"]').setValue('dining out')

    expect(inBody('[data-testid="category-create"]').exists()).toBe(false)
  })

  it('takes the highlighted row on Enter, and walks the list with the arrows', async () => {
    const wrapper = await open(mountPicker())
    const search = inBody('[data-testid="category-search"]')

    await search.trigger('keydown.down')
    await search.trigger('keydown.enter')

    expect(wrapper.emitted('update:modelValue')).toEqual([['dining']])
  })

  it('unfiles it from the bottom of the list', async () => {
    const wrapper = await open(mountPicker({ modelValue: 'dining' }))

    await inBody('[data-testid="category-none"]').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[null]])
  })

  it('shows a key nothing answers to rather than pretending it is unfiled', async () => {
    const wrapper = mountPicker({ modelValue: 'ski' })

    expect(wrapper.find('[data-testid="category"]').text()).toContain('ski')
  })

  it('stops at the top of the list rather than wrapping round', async () => {
    const wrapper = await open(mountPicker())
    const search = inBody('[data-testid="category-search"]')

    await search.setValue('Din')
    await search.trigger('keydown.up')
    await search.trigger('keydown.up')
    await search.trigger('keydown.enter')

    expect(wrapper.emitted('update:modelValue')).toEqual([['dining']])
  })

  it('stops at the bottom, on the row that makes a new one', async () => {
    const wrapper = await open(mountPicker())
    const search = inBody('[data-testid="category-search"]')

    await search.setValue('Din')
    for (let press = 0; press < 6; press++) await search.trigger('keydown.down')
    await search.trigger('keydown.enter')

    expect(wrapper.emitted('create')).toEqual([['Din']])
  })

  it('takes the create row on Enter when the highlight is on it', async () => {
    const wrapper = await open(mountPicker())
    const search = inBody('[data-testid="category-search"]')

    await search.setValue('Zzz')
    await search.trigger('keydown.enter')

    expect(wrapper.emitted('create')).toEqual([['Zzz']])
  })

  it('finds "not filed" by searching for it', async () => {
    const wrapper = await open(mountPicker({ modelValue: 'dining' }))
    const search = inBody('[data-testid="category-search"]')

    await search.setValue('not filed')
    await search.trigger('keydown.enter')

    expect(wrapper.emitted('update:modelValue')).toEqual([[null]])
  })

  it('highlights what the pointer is over', async () => {
    const wrapper = await open(mountPicker())

    await allInBody('[data-testid="category-option"]')[2].trigger('mousemove')
    await inBody('[data-testid="category-search"]').trigger('keydown.enter')

    expect(wrapper.emitted('update:modelValue')).toEqual([['shopping']])
  })

  it('shuts on a press anywhere else', async () => {
    await open(mountPicker())

    await inBody('.fixed.inset-0[aria-hidden="true"]').trigger('click')

    expect(inBody('[data-testid="category-search"]').exists()).toBe(false)
  })

  it('says it is adding, and cannot be opened while it does', async () => {
    const wrapper = mountPicker({ isCreating: true })

    expect(wrapper.find('[data-testid="category"]').text()).toContain('Adding')
    expect(wrapper.find('[data-testid="category"]').attributes('disabled')).toBeDefined()
  })

  it('shuts on escape', async () => {
    await open(mountPicker())

    await inBody('[data-testid="category-search"]').trigger('keydown.esc')

    expect(inBody('[data-testid="category-search"]').exists()).toBe(false)
  })
})
