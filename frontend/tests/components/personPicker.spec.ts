import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PersonPicker from '@/components/groups/PersonPicker.vue'
import type { AddableUser } from '@/api/types'

/**
 * Finding someone who already has an account.
 *
 * The field used to demand a name typed exactly, and produced a placeholder
 * whatever you typed, so adding a real person was impossible from here. Fuzzy
 * matching over the people who actually exist is the difference between guessing
 * a spelling and picking someone.
 */

const people: AddableUser[] = [
  { id: 'u1', displayName: 'Alice Anderson', email: 'alice@example.com', avatarUrl: null },
  { id: 'u2', displayName: 'Bob Brown', email: 'bob@example.com', avatarUrl: null },
  { id: 'u3', displayName: 'Carol Clark', email: 'carol.clark@example.com', avatarUrl: null },
  { id: 'u4', displayName: 'Bob Jones', email: 'bjones@example.com', avatarUrl: null },
]

const mountPicker = (candidates = people) =>
  mount(PersonPicker, { props: { candidates } })

async function type(wrapper: ReturnType<typeof mountPicker>, text: string) {
  await wrapper.find('input[type="search"]').setValue(text)
}

describe('PersonPicker', () => {
  it('lists nobody until something is typed', () => {
    const wrapper = mountPicker()

    // A bare directory dump is noise, and on a phone it pushes the form away.
    expect(wrapper.findAll('[data-testid="candidate"]')).toHaveLength(0)
  })

  it('finds someone by the start of their name', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'ali')

    const names = wrapper.findAll('[data-testid="candidate"]').map((row) => row.text())
    expect(names[0]).toContain('Alice Anderson')
  })

  it('finds someone by letters spread through the name', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'crlclk')

    expect(wrapper.find('[data-testid="candidate"]').text()).toContain('Carol Clark')
  })

  it('finds someone by their email', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'bjones')

    expect(wrapper.find('[data-testid="candidate"]').text()).toContain('Bob Jones')
  })

  it('shows the email, so two people with one name can be told apart', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'bob')

    const rows = wrapper.findAll('[data-testid="candidate"]').map((row) => row.text())
    expect(rows.some((row) => row.includes('bob@example.com'))).toBe(true)
    expect(rows.some((row) => row.includes('bjones@example.com'))).toBe(true)
  })

  it('highlights what matched', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'ali')

    // Otherwise a fuzzy hit looks arbitrary.
    expect(wrapper.find('[data-testid="candidate"] mark').exists()).toBe(true)
  })

  it('picks someone when their row is clicked', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'ali')

    await wrapper.find('[data-testid="candidate"]').trigger('click')

    expect(wrapper.emitted('pick')).toEqual([[people[0]]])
  })

  it('picks the top match on Enter', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'ali')

    await wrapper.find('input[type="search"]').trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('pick')).toEqual([[people[0]]])
  })

  it('walks the list with the arrow keys', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'bob')

    const input = wrapper.find('input[type="search"]')
    await input.trigger('keydown', { key: 'ArrowDown' })
    await input.trigger('keydown', { key: 'Enter' })

    const picked = wrapper.emitted('pick')![0][0] as AddableUser
    expect(picked.displayName).toBe('Bob Jones')
  })

  it('clears the query after a pick, ready for the next person', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'ali')

    await wrapper.find('[data-testid="candidate"]').trigger('click')

    expect((wrapper.find('input[type="search"]').element as HTMLInputElement).value).toBe('')
  })

  it('says nobody matches, and points at the other way in', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'Dave')

    // Not an offer to add them anyway: a member with no account behind them could
    // never open the group, see what they owed, or be told about it.
    const answer = wrapper.find('[data-testid="no-match"]')
    expect(answer.exists()).toBe(true)
    expect(answer.text()).toContain('Dave')
    expect(answer.text()).toContain('invite link')
  })

  it('offers no way to add a name that matches nobody', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'Dave')

    expect(wrapper.find('[data-testid="add-placeholder"]').exists()).toBe(false)
    expect(wrapper.emitted('addPlaceholder')).toBeUndefined()
  })

  it('adds nobody when Enter is pressed on a name that matches nobody', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'Dave')

    await wrapper.find('input[type="search"]').trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('pick')).toBeUndefined()
  })

  it('says nothing while someone matches', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'ali')

    expect(wrapper.find('[data-testid="no-match"]').exists()).toBe(false)
  })

  it('says so when there is nobody else with an account', async () => {
    const wrapper = mountPicker([])

    await type(wrapper, 'any')

    expect(wrapper.find('[data-testid="no-match"]').exists()).toBe(true)
  })

  it('clears the query on Escape', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'ali')

    await wrapper.find('input[type="search"]').trigger('keydown', { key: 'Escape' })

    // Closes the list without picking anyone, which is what Escape means.
    expect((wrapper.find('input[type="search"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.findAll('[data-testid="candidate"]')).toHaveLength(0)
    expect(wrapper.emitted('pick')).toBeFalsy()
  })

  it('follows the pointer, so a click lands on the row under it', async () => {
    const wrapper = mountPicker()
    await type(wrapper, 'bob')

    const rows = wrapper.findAll('[data-testid="candidate"]')
    await rows[1].trigger('mousemove')

    expect(rows[1].attributes('aria-selected')).toBe('true')
    expect(rows[0].attributes('aria-selected')).toBe('false')
  })

  it('describes the field for a screen reader', () => {
    const wrapper = mountPicker()
    const input = wrapper.find('input[type="search"]')

    expect(input.attributes('role')).toBe('combobox')
    expect(input.attributes('aria-expanded')).toBe('false')
    expect(input.attributes('aria-label')).toBeTruthy()
  })

  it('announces the list once it has something in it', async () => {
    const wrapper = mountPicker()

    await type(wrapper, 'bob')

    expect(wrapper.find('input[type="search"]').attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('[role="listbox"]').exists()).toBe(true)
  })
})
