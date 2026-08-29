import { describe, expect, it } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import GroupCard from '@/components/groups/GroupCard.vue'
import type { LocalGroup } from '@/offline/db'

const group = (overrides: Partial<LocalGroup> = {}): LocalGroup => ({
  id: 'group-1',
  name: 'Roommates',
  description: null,
  baseCurrency: 'CAD',
  iconName: null,
  colorHex: '#4f46e5',
  isArchived: false,
  lineageId: 'lineage-1',
  members: [
    {
      id: 'm1',
      userId: 'user-1',
      displayName: 'Alice',
      avatarUrl: null,
      role: 'Owner',
      status: 'Active',
      isPlaceholder: false,
      netBalance: 0,
    },
    {
      id: 'm2',
      userId: null,
      displayName: 'Bob',
      avatarUrl: null,
      role: 'Member',
      status: 'Active',
      isPlaceholder: true,
      netBalance: 0,
    },
  ],
  myNetBalance: 0,
  totalSpend: 0,
  expenseCount: 0,
  updatedAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

const mountCard = (value: LocalGroup) =>
  mount(GroupCard, {
    props: { group: value },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })

describe('GroupCard', () => {
  it('shows the group name and members', () => {
    const wrapper = mountCard(group())

    expect(wrapper.text()).toContain('Roommates')
    expect(wrapper.text()).toContain('Alice, Bob')
  })

  it('says you are owed when the balance is positive', () => {
    const wrapper = mountCard(group({ myNetBalance: 42.5 }))

    expect(wrapper.text()).toContain('You are owed')
    expect(wrapper.text()).toContain('42.50')
  })

  it('says you owe when the balance is negative', () => {
    const wrapper = mountCard(group({ myNetBalance: -42.5 }))

    expect(wrapper.text()).toContain('You owe')
  })

  it('says settled up at zero', () => {
    const wrapper = mountCard(group({ myNetBalance: 0 }))

    expect(wrapper.text()).toContain('Settled up')
  })

  it('marks an archived group', () => {
    const wrapper = mountCard(group({ isArchived: true }))

    expect(wrapper.attributes('data-archived')).toBe('true')
    expect(wrapper.text()).toContain('Archived')
  })

  it('summarises a large roster instead of listing everyone', () => {
    const many = group({
      members: ['Alice', 'Bob', 'Carol', 'Dan', 'Erin'].map((name, index) => ({
        id: `m${index}`,
        userId: null,
        displayName: name,
        avatarUrl: null,
        role: 'Member',
        status: 'Active',
        isPlaceholder: true,
        netBalance: 0,
      })),
    })

    const wrapper = mountCard(many)

    expect(wrapper.text()).toContain('and 3 more')
  })

  it('ignores removed members in the summary', () => {
    const withRemoved = group({
      members: [
        { ...group().members[0] },
        { ...group().members[1], status: 'Removed' },
      ],
    })

    const wrapper = mountCard(withRemoved)

    expect(wrapper.text()).not.toContain('Bob')
  })

  it('falls back to a default icon when the group has none', () => {
    const wrapper = mountCard(group({ iconName: null }))

    // A hole where the icon goes reads as a broken row, so there is always one.
    expect(wrapper.find('[data-icon]').exists()).toBe(true)
  })

  it('renders the icon the group was given', () => {
    const wrapper = mountCard(group({ iconName: 'house' }))

    expect(wrapper.find('[data-icon="house"]').exists()).toBe(true)
  })

  it('falls back for an icon name it does not know', () => {
    // A name written by a newer version of the app must not break the list.
    const wrapper = mountCard(group({ iconName: 'invented-later' }))

    const rendered = wrapper.find('[data-icon]').attributes('data-icon')
    expect(rendered).toBeTruthy()
    expect(rendered).not.toBe('invented-later')
  })

  it('links to the group', () => {
    const wrapper = mountCard(group())

    expect(wrapper.findComponent(RouterLinkStub).props().to).toEqual({
      name: 'group',
      params: { groupId: 'group-1' },
    })
  })

  it('stays a single tap target of a usable size', () => {
    const wrapper = mountCard(group())

    expect(wrapper.classes()).toContain('tap-target')
  })
})
