import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub, mount } from '@vue/test-utils'
import BottomNav from '@/components/layout/BottomNav.vue'

let routeName = 'dashboard'

vi.mock('vue-router', () => ({
  useRoute: () => ({ name: routeName, params: {}, query: {} }),
  RouterLink: RouterLinkStub,
}))

function mountNav(name: string) {
  routeName = name
  return mount(BottomNav, { global: { stubs: { RouterLink: RouterLinkStub } } })
}

function litTabs(wrapper: ReturnType<typeof mountNav>): string[] {
  return wrapper
    .findAll('[data-tab]')
    .filter((tab) => (tab.attributes('class') ?? '').includes('nav-tab-active'))
    .map((tab) => tab.attributes('data-tab')!)
}

describe('BottomNav', () => {
  it('lights the dashboard on the dashboard', () => {
    expect(litTabs(mountNav('dashboard'))).toEqual(['dashboard'])
  })

  it('lights the dashboard on a group opened by its own URL', () => {
    expect(litTabs(mountNav('group'))).toEqual(['dashboard'])
  })

  it('lights activity on the activity screen', () => {
    expect(litTabs(mountNav('activity'))).toEqual(['activity'])
  })

  it('lights stats on the stats screen', () => {
    expect(litTabs(mountNav('stats'))).toEqual(['stats'])
  })

  it('lights profile on the profile screen', () => {
    expect(litTabs(mountNav('profile'))).toEqual(['profile'])
  })

  it('lights exactly one tab at a time', () => {
    for (const name of ['dashboard', 'group', 'activity', 'stats', 'profile']) {
      expect(litTabs(mountNav(name))).toHaveLength(1)
    }
  })

  it('lights nothing on a screen no tab owns', () => {
    for (const name of ['settle', 'group-settings', 'expense', 'import', 'sign-in']) {
      expect(litTabs(mountNav(name))).toEqual([])
    }
  })

  it('offers the four tabs and the add button', () => {
    const wrapper = mountNav('dashboard')

    expect(wrapper.findAll('[data-tab]')).toHaveLength(4)
    expect(wrapper.find('[aria-label="Add an expense"]').exists()).toBe(true)
  })
})
