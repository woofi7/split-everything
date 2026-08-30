import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub, mount } from '@vue/test-utils'
import BottomNav from '@/components/layout/BottomNav.vue'

/**
 * Which tab is lit.
 *
 * Decided by route name rather than by the router's path matching. A group opened
 * by its own URL is the dashboard, rendered by the same component, so coming back
 * to it from an expense or the settle screen left every tab unlit: the path was
 * /groups/<id> while the tab points at /dashboard.
 */
let routeName = 'dashboard'

vi.mock('vue-router', () => ({
  useRoute: () => ({ name: routeName, params: {}, query: {} }),
  RouterLink: RouterLinkStub,
}))

function mountNav(name: string) {
  routeName = name
  return mount(BottomNav, { global: { stubs: { RouterLink: RouterLinkStub } } })
}

/** The tabs marked active, by their data-tab name. */
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
    // The reported bug: back from an expense lands here, and nothing was lit.
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
    // Settling up and group settings are reached from a tab, not by one, and the
    // way back out of them is the back button rather than the bar.
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
