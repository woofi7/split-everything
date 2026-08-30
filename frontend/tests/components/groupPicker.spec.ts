import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { mount, RouterLinkStub } from '@vue/test-utils'
import GroupPicker from '@/components/groups/GroupPicker.vue'
import { useGroupsStore } from '@/stores/groups'
import { resetDatabase } from '@/offline/db'

/**
 * Choosing the group the whole app is on.
 *
 * The app shows one group at a time, so this is the only place the others are
 * reachable. It has to be findable with one group as well as ten: with one, it is
 * still how you get to creating the next.
 */

const group = (id: string, name: string, balance = 0, archived = false) => ({
  id,
  name,
  baseCurrency: 'CAD',
  colorHex: '#4f46e5',
  iconName: null,
  isArchived: archived,
  lineageId: `l-${id}`,
  members: [],
  memberCount: 2,
  myNetBalance: balance,
  totalSpend: 0,
  expenseCount: 0,
  updatedAt: '2026-01-01T00:00:00Z',
})

function withGroups(...list: ReturnType<typeof group>[]) {
  const store = useGroupsStore()
  store.groups = list as never
  store.attachApi({ get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } as never)
  if (list.length > 0) store.setMainGroup(list[0].id)
  return store
}

const mountPicker = (open = true) =>
  mount(GroupPicker, {
    props: { open },
    global: { stubs: { RouterLink: RouterLinkStub, teleport: true } },
  })

describe('GroupPicker', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('lists every group', () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))

    const wrapper = mountPicker()

    const names = wrapper.findAll('[data-testid="group-option"]').map((row) => row.text())
    expect(names.join(' ')).toContain('Roommates')
    expect(names.join(' ')).toContain('Ski trip')
  })

  it('opens the group it switches to at the top', async () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
    const wrapper = mountPicker()

    try {
      await wrapper.findAll('[data-testid="group-option"]')[1].trigger('click')
      await nextTick()

      // Left alone the screen opens wherever the last group was being read, which
      // is nowhere in this one.
      expect(scrollTo).toHaveBeenCalledWith(0, 0)
    } finally {
      scrollTo.mockRestore()
    }
  })

  it('marks the one the app is on', () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))

    const wrapper = mountPicker()

    const rows = wrapper.findAll('[data-testid="group-option"]')
    expect(rows[0].attributes('aria-current')).toBe('true')
    expect(rows[1].attributes('aria-current')).toBe('false')
  })

  it('switches the app to the group that was picked', async () => {
    const store = withGroups(group('g1', 'Roommates'), group('g2', 'Ski trip'))
    const wrapper = mountPicker()

    await wrapper.findAll('[data-testid="group-option"]')[1].trigger('click')

    expect(store.mainGroupId).toBe('g2')
    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('shows what each group owes, so the choice is informed', () => {
    withGroups(group('g1', 'Roommates', 42.5), group('g2', 'Ski trip', -10))

    const wrapper = mountPicker()

    const text = wrapper.text()
    expect(text).toContain('42.50')
    expect(text).toContain('10.00')
  })

  it('offers to create a group', () => {
    withGroups(group('g1', 'Roommates'))

    const wrapper = mountPicker()

    const targets = wrapper.findAllComponents(RouterLinkStub)
      .map((link) => JSON.stringify(link.props().to)).join(' ')
    expect(targets).toContain('new-group')
  })

  it('is useful with a single group, since that is how you get to the next one', () => {
    withGroups(group('g1', 'Roommates'))

    const wrapper = mountPicker()

    expect(wrapper.findAll('[data-testid="group-option"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('Roommates')
  })

  it('separates archived groups rather than hiding them', () => {
    withGroups(group('g1', 'Roommates'), group('g2', 'Old flat', 0, true))

    const wrapper = mountPicker()

    // Frozen, not gone: you still need to read the history.
    expect(wrapper.text()).toContain('Old flat')
    expect(wrapper.text()).toContain('Archived')
  })

  it('puts a group you owe in above a settled one', () => {
    withGroups(group('g1', 'Settled', 0), group('g2', 'Owing', -25))

    const wrapper = mountPicker()

    // This list is where attention gets directed, so the group needing it leads.
    const names = wrapper.findAll('[data-testid="group-option"]').map((row) => row.text())
    expect(names[0]).toContain('Owing')
  })

  it('says nothing about archived groups when there are none', () => {
    withGroups(group('g1', 'Roommates'))

    const wrapper = mountPicker()

    expect(wrapper.text()).not.toContain('Archived')
  })

  it('renders nothing when closed', () => {
    withGroups(group('g1', 'Roommates'))

    const wrapper = mountPicker(false)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('closes on Escape', async () => {
    withGroups(group('g1', 'Roommates'))
    const wrapper = mountPicker()

    await wrapper.find('[role="dialog"]').trigger('keydown', { key: 'Escape' })

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('closes on the backdrop', async () => {
    withGroups(group('g1', 'Roommates'))
    const wrapper = mountPicker()

    // Tapping outside a sheet is how a sheet is dismissed on a phone.
    await wrapper.find('.fixed.inset-0').trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('closes on the Close button', async () => {
    withGroups(group('g1', 'Roommates'))
    const wrapper = mountPicker()

    await wrapper.findAll('button').find((b) => b.text() === 'Close')!.trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('closes when going to make a new group', async () => {
    withGroups(group('g1', 'Roommates'))
    const wrapper = mountPicker()

    const link = wrapper.findAllComponents(RouterLinkStub)
      .find((l) => JSON.stringify(l.props().to).includes('new-group'))
    await link!.trigger('click')

    // Otherwise it sits over the screen it navigated to.
    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('switches to an archived group when one is picked', async () => {
    const store = withGroups(group('g1', 'Roommates'), group('g2', 'Old flat', 0, true))
    const wrapper = mountPicker()

    const archivedRow = wrapper.findAll('[data-testid="group-option"]')
      .find((row) => row.text().includes('Old flat'))
    await archivedRow!.trigger('click')

    // Frozen, not gone: reading the history is the reason to go there.
    expect(store.mainGroupId).toBe('g2')
  })

  it('describes itself', () => {
    withGroups(group('g1', 'Roommates'))

    const wrapper = mountPicker()

    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.attributes('aria-modal')).toBe('true')
    expect(dialog.attributes('aria-label')).toContain('group')
  })
})
