import { describe, expect, it } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import AppShell from '@/components/layout/AppShell.vue'
import BottomNav from '@/components/layout/BottomNav.vue'

const mountShell = (props: Record<string, unknown>, slots: Record<string, string> = {}) =>
  mount(AppShell, {
    props: { title: 'Groups', ...props },
    slots: { default: 'Body content', ...slots },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })

describe('AppShell', () => {
  it('shows the title and the body', () => {
    const wrapper = mountShell({})

    expect(wrapper.find('h1').text()).toBe('Groups')
    expect(wrapper.text()).toContain('Body content')
  })

  it('carries the app mark at the top left', () => {
    const wrapper = mountShell({})

    const icon = wrapper.find('[data-testid="app-icon"]')
    expect(icon.exists()).toBe(true)
    expect(icon.attributes('src')).toMatch(/svg/)
    expect(icon.attributes('alt')).toBe('')
  })

  it('puts the mark before the title', () => {
    const wrapper = mountShell({})

    const html = wrapper.html()
    expect(html.indexOf('app-icon')).toBeLessThan(html.indexOf('<h1'))
  })

  it('shows a subtitle when given one', () => {
    const wrapper = mountShell({ subtitle: '2 members' })

    expect(wrapper.text()).toContain('2 members')
  })

  it('leaves the subtitle out when there is none', () => {
    const wrapper = mountShell({})

    expect(wrapper.findAll('p')).toHaveLength(0)
  })

  it('renders a page action', () => {
    const wrapper = mountShell({}, { 'header-action': '<button>New</button>' })

    expect(wrapper.find('button').text()).toBe('New')
  })

  it('puts the back button top right, level with the title', () => {
    const wrapper = mountShell({ backTo: { name: 'dashboard' }, backLabel: 'Dashboard' })

    const back = wrapper.find('[data-testid="back"]')
    expect(back.classes()).toContain('btn-secondary')
    expect(back.classes()).toContain('rounded-full')

    const row = wrapper.find('[data-testid="title-row"]')
    expect(row.find('[data-testid="back"]').exists()).toBe(true)
  })

  it('puts back furthest right, past the page action', () => {
    const wrapper = mountShell(
      { backTo: { name: 'dashboard' } },
      { 'header-action': '<button>Change</button>' },
    )

    const html = wrapper.html()
    expect(html.indexOf('Change')).toBeLessThan(html.indexOf('data-testid="back"'))
  })

  it('names the screen behind it, rather than the one it belongs under', () => {
    window.history.replaceState({ back: '/activity' }, '')

    const wrapper = mountShell({ backTo: { name: 'group' }, backLabel: 'Group' })

    expect(wrapper.find('[data-testid="back"]').attributes('aria-label')).toBe('Back to Activity')

    window.history.replaceState(null, '')
  })

  it('falls back to the declared name when there is nothing behind it', () => {
    const wrapper = mountShell({ backTo: { name: 'group' }, backLabel: 'Roommates' })

    expect(wrapper.find('[data-testid="back"]').attributes('aria-label')).toBe('Back to Roommates')
  })

  it('has no back button on a screen a tab can reach', () => {
    const wrapper = mountShell({})

    expect(wrapper.find('[data-testid="back"]').exists()).toBe(false)
    expect(wrapper.find('h1').text()).toBe('Groups')
  })

  it('puts the page action on the title line', () => {
    const wrapper = mountShell({}, { 'header-action': '<button>Change</button>' })

    const row = wrapper.find('[data-testid="title-row"]')
    expect(row.find('button').text()).toBe('Change')
  })

  it('keeps the action on the title line beside back', () => {
    const wrapper = mountShell(
      { backTo: { name: 'dashboard' } },
      { 'header-action': '<button>Change</button>' },
    )

    const row = wrapper.find('[data-testid="title-row"]')
    expect(row.find('button').text()).toBe('Change')
    expect(row.find('[data-testid="back"]').exists()).toBe(true)
  })

  it('has no fixed chrome at the top', () => {
    const wrapper = mountShell({})

    expect(wrapper.find('header').exists()).toBe(false)
    expect(wrapper.find('.sticky').exists()).toBe(false)
  })

  it('says nothing about sync when there is nothing to say', () => {
    const wrapper = mountShell({})

    expect(wrapper.text()).not.toContain('All synced')
  })

  it('still says something when sync needs attention', () => {
    const wrapper = mountShell({ rejectedCount: 1 })

    expect(wrapper.text()).toContain('needs attention')
  })

  it('clears the notch at the top of the page', () => {
    const wrapper = mountShell({})

    expect(wrapper.find('main').classes().join(' ')).toContain('safe-area-inset-top')
  })

  it('shows the sync state', () => {
    const wrapper = mountShell({ pendingCount: 2, isOffline: true })

    expect(wrapper.find('[role="status"]').text()).toContain('Offline')
    expect(wrapper.find('[role="status"]').text()).toContain('2 waiting')
  })

  it('shows the bottom nav by default', () => {
    const wrapper = mountShell({})

    expect(wrapper.find('nav').exists()).toBe(true)
  })

  it('hides the bottom nav on a focused screen', () => {
    const wrapper = mountShell({ showNav: false })

    expect(wrapper.find('nav').exists()).toBe(false)
  })

  it('leaves room under the raised centre button of the nav', () => {
    const withNav = mountShell({})
    const withoutNav = mountShell({ showNav: false })

    expect(withNav.find('main').classes()).toContain('pb-10')
    expect(withoutNav.find('main').classes()).toContain('pb-[max(2rem,env(safe-area-inset-bottom))]')
  })

  describe('as a frame', () => {
    it('scrolls the page rather than the window', () => {
      const main = mountShell({}).find('main')

      expect(main.classes()).toContain('overflow-y-auto')
      expect(main.classes()).toContain('min-h-0')
      expect(main.classes()).toContain('flex-1')
    })

    it('names the page, so a gesture can move it and find its scroll', () => {
      const wrapper = mountShell({})

      expect(wrapper.find('[data-app-page]').element.tagName).toBe('MAIN')
    })

    it('holds the tab bar in the frame rather than over the page', () => {
      const nav = mountShell({}).find('nav')

      expect(nav.classes()).not.toContain('fixed')
      expect(nav.classes()).toContain('shrink-0')
    })
  })
})

describe('BottomNav', () => {
  const mountNav = () =>
    mount(BottomNav, { global: { stubs: { RouterLink: RouterLinkStub } } })

  it('offers the four tabs plus the add action', () => {
    const wrapper = mountNav()

    const text = wrapper.text()
    for (const label of ['Dashboard', 'Activity', 'Stats', 'Profile']) {
      expect(text).toContain(label)
    }
    expect(wrapper.find('[aria-label="Add an expense"]').exists()).toBe(true)
  })

  it('lifts the tab you are on into a circle', () => {
    const wrapper = mountNav()

    const holders = wrapper.findAll('[data-testid="tab-icon"]')
    expect(holders).toHaveLength(4)
    expect(holders[0].classes()).toContain('nav-tab-icon')
  })

  it('puts the add action in the middle, where a thumb reaches', () => {
    const wrapper = mountNav()

    const items = wrapper.findAll('li')
    expect(items).toHaveLength(5)
    expect(items[2].find('[aria-label="Add an expense"]').exists()).toBe(true)
  })

  it('routes each tab to its screen', () => {
    const wrapper = mountNav()

    const targets = wrapper
      .findAllComponents(RouterLinkStub)
      .map((link) => (link.props().to as { name: string }).name)

    expect(targets).toEqual(['dashboard', 'activity', 'add-expense', 'stats', 'profile'])
  })

  it('names itself for a screen reader', () => {
    const wrapper = mountNav()

    expect(wrapper.find('nav').attributes('aria-label')).toBe('Main')
  })

  it('keeps every target big enough to tap', () => {
    const wrapper = mountNav()

    const links = wrapper.findAllComponents(RouterLinkStub)
    expect(links.every((link) => link.classes().includes('tap-target'))).toBe(true)
  })

  it('hides the decorative icons from assistive tech', () => {
    const wrapper = mountNav()

    expect(wrapper.findAll('svg').every((svg) => svg.attributes('aria-hidden') === 'true')).toBe(
      true,
    )
  })
})
