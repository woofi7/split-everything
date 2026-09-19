import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import CategoryEditor from '@/components/groups/CategoryEditor.vue'
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
]

/**
 * Editing the list of things an expense can be filed under.
 *
 * One editor for both lists there are - a group's own and the server's - because
 * they are the same list with a different owner, and two of them would be two
 * places to fix the next thing wrong with it.
 */
describe('the category editor', () => {
  const mountEditor = (props = {}) =>
    mount(CategoryEditor, {
      props: { categories, description: 'Whose list this is', ...props },
    })

  const rows = (wrapper: ReturnType<typeof mountEditor>) =>
    wrapper.findAll('[data-testid="category-row"]')

  const saved = (wrapper: ReturnType<typeof mountEditor>) =>
    wrapper.emitted('save')?.at(-1)?.[0] as Array<Record<string, unknown>> | undefined

  it('shows the list it was given, keywords and all', () => {
    const wrapper = mountEditor()

    expect(rows(wrapper)).toHaveLength(2)
    expect(
      (wrapper.find('[data-testid="category-keywords"]').element as HTMLInputElement).value,
    ).toBe('metro, iga')
  })

  it('says whose list it is, because that is the whole question', () => {
    expect(mountEditor().text()).toContain('Whose list this is')
  })

  it('offers nothing to save until something changes', async () => {
    const wrapper = mountEditor()
    expect(wrapper.find('[data-testid="save-categories"]').exists()).toBe(false)

    await wrapper.find('[data-testid="category-name"]').setValue('Epicerie')
    expect(wrapper.find('[data-testid="save-categories"]').exists()).toBe(true)
  })

  it('keeps the key when a category is renamed', async () => {
    const wrapper = mountEditor()

    await wrapper.find('[data-testid="category-name"]').setValue('Epicerie')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    // The key is what the expenses are filed under: renaming must not unfile a
    // year of them.
    expect(saved(wrapper)?.[0]).toMatchObject({ key: 'groceries', name: 'Epicerie' })
  })

  it('adds one with no key, so the server makes it from the name', async () => {
    const wrapper = mountEditor()

    await wrapper.find('[data-testid="add-category"]').trigger('click')
    await wrapper.findAll('[data-testid="category-name"]')[2].setValue('Ski')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    expect(saved(wrapper)?.[2]).toMatchObject({ key: undefined, name: 'Ski' })
  })

  it('drops a row nobody named rather than sending a blank', async () => {
    const wrapper = mountEditor()

    await wrapper.find('[data-testid="add-category"]').trigger('click')
    await wrapper.findAll('[data-testid="category-name"]')[0].setValue('Epicerie')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    expect(saved(wrapper)).toHaveLength(2)
  })

  it('removes one', async () => {
    const wrapper = mountEditor()

    await wrapper.findAll('[data-testid="category-remove"]')[0].trigger('click')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    expect(saved(wrapper)?.map((row) => row.key)).toEqual(['dining'])
  })

  it('reorders, because the order is the order they are shown in', async () => {
    const wrapper = mountEditor()

    await wrapper.findAll('[data-testid="category-down"]')[0].trigger('click')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    expect(saved(wrapper)?.map((row) => row.key)).toEqual(['dining', 'groceries'])
  })

  it('goes nowhere past either end of the list', async () => {
    const wrapper = mountEditor()

    await wrapper.findAll('[data-testid="category-up"]')[0].trigger('click')
    await wrapper.findAll('[data-testid="category-down"]')[1].trigger('click')

    // Nothing moved, so there is nothing to save.
    expect(wrapper.find('[data-testid="save-categories"]').exists()).toBe(false)
  })

  it('reads the keywords as a list, however they were typed', async () => {
    const wrapper = mountEditor()

    await wrapper.find('[data-testid="category-keywords"]').setValue(' metro ,, iga , epicerie ')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    expect(saved(wrapper)?.[0].keywords).toEqual(['metro', 'iga', 'epicerie'])
  })

  it('falls back to a plain tag for an icon nobody named', async () => {
    const wrapper = mountEditor()

    await wrapper.find('[data-testid="category-icon"]').setValue('')
    await wrapper.find('[data-testid="save-categories"]').trigger('click')

    expect(saved(wrapper)?.[0].iconName).toBe('tag')
  })

  it('puts everything back on cancel', async () => {
    const wrapper = mountEditor()

    await wrapper.find('[data-testid="category-name"]').setValue('Epicerie')
    await wrapper.find('[data-testid="cancel-categories"]').trigger('click')

    expect(
      (wrapper.find('[data-testid="category-name"]').element as HTMLInputElement).value,
    ).toBe('Groceries')
    expect(wrapper.emitted('revert')).toHaveLength(1)
    expect(wrapper.find('[data-testid="save-categories"]').exists()).toBe(false)
  })

  it('reads a list that arrives after it was mounted', async () => {
    const wrapper = mountEditor({ categories: [] })
    expect(rows(wrapper)).toHaveLength(0)

    await wrapper.setProps({ categories })

    // The group screen loads its categories a moment after it renders, and an
    // editor that missed them would show an empty list over a full one.
    expect(rows(wrapper)).toHaveLength(2)
  })

  it('says it is saving, and will not be pressed twice', async () => {
    const wrapper = mountEditor({ isSaving: true })

    await wrapper.find('[data-testid="category-name"]').setValue('Epicerie')

    expect(wrapper.find('[data-testid="save-categories"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="save-categories"]').text()).toContain('Saving')
  })
})
