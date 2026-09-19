import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub, mount } from '@vue/test-utils'
import SideNav from '@/components/layout/SideNav.vue'
import AppShell from '@/components/layout/AppShell.vue'

let routeName = 'dashboard'

vi.mock('vue-router', () => ({
  useRoute: () => ({ name: routeName, params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

const mountRail = (name: string) => {
  routeName = name
  return mount(SideNav, { global: { stubs: { RouterLink: RouterLinkStub } } })
}

const mountShell = (props: Record<string, unknown> = {}) =>
  mount(AppShell, {
    props: { title: 'Roommates', ...props },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })

describe('the rail on a wide screen', () => {
  it('offers the same places as the bar on a phone', () => {
    const rail = mountRail('dashboard')

    expect(rail.findAll('[data-side-tab]').map((tab) => tab.attributes('data-side-tab')))
      .toEqual(['dashboard', 'activity', 'stats', 'profile'])
  })

  it('marks where you are', () => {
    const lit = mountRail('stats')
      .findAll('[data-side-tab]')
      .filter((tab) => tab.attributes('aria-current') === 'page')
      .map((tab) => tab.attributes('data-side-tab'))

    expect(lit).toEqual(['stats'])
  })

  it('counts a group screen as the dashboard, as the bar does', () => {
    const lit = mountRail('group')
      .findAll('[data-side-tab]')
      .filter((tab) => tab.attributes('aria-current') === 'page')
      .map((tab) => tab.attributes('data-side-tab'))

    expect(lit).toEqual(['dashboard'])
  })

  it('carries the action the phone puts in the middle of the bar', () => {
    expect(mountRail('dashboard').find('[data-testid="side-add"]').exists()).toBe(true)
  })
})

describe('the shell on a wide screen', () => {
  it('has both ways to navigate, and CSS decides which is shown', () => {
    const shell = mountShell()

    expect(shell.find('[data-testid="side-nav"]').exists()).toBe(true)
    expect(shell.find('nav.lg\\:hidden').exists()).toBe(true)
  })

  it('leaves both out on a screen that asked for no navigation', () => {
    const shell = mountShell({ showNav: false })

    expect(shell.find('[data-testid="side-nav"]').exists()).toBe(false)
    expect(shell.find('[data-tab]').exists()).toBe(false)
  })

  it('reads narrow unless a screen asks for the room', () => {
    expect(mountShell().find('[data-app-page]').attributes('data-width')).toBe('reading')
    expect(mountShell({ width: 'wide' }).find('[data-app-page]').attributes('data-width'))
      .toBe('wide')
  })
})
