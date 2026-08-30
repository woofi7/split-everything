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

  it('shows a subtitle when given one', () => {
    const wrapper = mountShell({ subtitle: '2 members' })

    expect(wrapper.text()).toContain('2 members')
  })

  it('leaves the subtitle out when there is none', () => {
    const wrapper = mountShell({})

    // Nothing to say about sync either, so the page starts with the title alone.
    expect(wrapper.findAll('p')).toHaveLength(0)
  })

  it('renders a page action', () => {
    const wrapper = mountShell({}, { 'header-action': '<button>New</button>' })

    expect(wrapper.find('button').text()).toBe('New')
  })

  it('has no fixed chrome at the top', () => {
    const wrapper = mountShell({})

    // The page is the page. The only fixed furniture is the tab bar.
    expect(wrapper.find('header').exists()).toBe(false)
    expect(wrapper.find('.sticky').exists()).toBe(false)
  })

  it('says nothing about sync when there is nothing to say', () => {
    const wrapper = mountShell({})

    // "All synced" on every screen forever reports that nothing is wrong, which
    // is not news.
    expect(wrapper.text()).not.toContain('All synced')
  })

  it('still says something when sync needs attention', () => {
    const wrapper = mountShell({ rejectedCount: 1 })

    expect(wrapper.text()).toContain('needs attention')
  })

  it('clears the notch at the top of the page', () => {
    const wrapper = mountShell({})

    // With no header, the content itself has to clear the status bar.
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
    // A form screen has its own way back; a tab bar there invites losing input.
    const wrapper = mountShell({ showNav: false })

    expect(wrapper.find('nav').exists()).toBe(false)
  })

  it('leaves room for the nav so it never covers the last row', () => {
    const withNav = mountShell({})
    const withoutNav = mountShell({ showNav: false })

    expect(withNav.find('main').classes()).toContain('pb-28')
    expect(withoutNav.find('main').classes()).toContain('pb-8')
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

    // Every tab carries the icon holder; the active class is what grows it, so the
    // holder has to be a element of its own rather than the svg.
    const holders = wrapper.findAll('[data-testid="tab-icon"]')
    expect(holders).toHaveLength(4)
    expect(holders[0].classes()).toContain('nav-tab-icon')
  })

  it('puts the add action in the middle, where a thumb reaches', () => {
    const wrapper = mountNav()

    const items = wrapper.findAll('li')
    // Two tabs, the action, then two more tabs.
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
