import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import GroupSwitcher from '@/components/groups/GroupSwitcher.vue'
import { useGroupsStore } from '@/stores/groups'
import { resetDatabase } from '@/offline/db'

const group = (id: string, name: string, archived = false) => ({
  id,
  name,
  baseCurrency: 'CAD',
  colorHex: '#4f46e5',
  isArchived: archived,
  lineageId: `l-${id}`,
  members: [],
  myNetBalance: 0,
  totalSpend: 0,
  expenseCount: 0,
  updatedAt: '2026-01-01T00:00:00Z',
})

describe('GroupSwitcher', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  function withGroups(...list: ReturnType<typeof group>[]) {
    const store = useGroupsStore()
    store.groups = list as never
    store.attachApi({ get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } as never)
    if (list.length > 0) store.setMainGroup(list[0].id)
    return store
  }

  it('names the group the app is on', () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))

    const wrapper = mount(GroupSwitcher)

    expect(wrapper.text()).toContain('Roommates')
  })

  it('switches the whole app to another group', async () => {
    const store = withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))
    const wrapper = mount(GroupSwitcher)

    await wrapper.find('select').setValue('g2')

    expect(store.mainGroupId).toBe('g2')
  })

  it('leaves out archived groups', () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Old flat', true))

    const wrapper = mount(GroupSwitcher)

    expect(wrapper.text()).not.toContain('Old flat')
  })

  it('stays out of the way when there is only one group', () => {
    withGroups(group('g1', 'Roommates'))

    const wrapper = mount(GroupSwitcher)

    // A chooser with one option is noise.
    expect(wrapper.find('select').exists()).toBe(false)
  })

  it('renders nothing at all with no groups', () => {
    withGroups()

    const wrapper = mount(GroupSwitcher)

    expect(wrapper.text()).toBe('')
  })

  it('says what it is for', () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))

    const wrapper = mount(GroupSwitcher)

    expect(wrapper.find('select').attributes('aria-label')).toContain('group')
  })
})
