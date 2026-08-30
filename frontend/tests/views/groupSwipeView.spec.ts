import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import DashboardView from '@/views/DashboardView.vue'
import {
  GROUP_ID,
  fakeApi,
  mountView,
  settle,
  testGroup,
  waitFor,
} from '../support/viewHarness'

/**
 * Swiping across the dashboard to change group.
 *
 * The screen itself has to follow, and so does the address: a group's own URL is
 * what makes it the group the app is on, so a stale one sends a reload back to
 * the group that was swiped away from.
 *
 * Real timers throughout, because the landing is an animation and the local
 * replica resolves on a macrotask: waiting for the state rather than for a
 * duration is what keeps this from being a race.
 */

const replace = vi.fn()

vi.mock('vue-router', () => ({
  // Opened at a group's own URL, which is where the address matters.
  useRoute: () => ({ params: { groupId: GROUP_ID }, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

const twoGroups = () =>
  fakeApi({
    '/groups': () => [
      { ...testGroup(), id: GROUP_ID, name: 'Roommates' },
      { ...testGroup(), id: 'group-2', name: 'Ski trip' },
    ],
  })

/** One stage of a finger going across the screen, as the browser reports it. */
function touch(type: string, x: number): void {
  const event = new Event(type, { bubbles: true, cancelable: type === 'touchmove' })
  const list = [{ clientX: x, clientY: 400 }]

  Object.assign(event, { touches: list, changedTouches: list })
  window.dispatchEvent(event)
}

function swipeLeft(): void {
  touch('touchstart', 300)
  touch('touchmove', 250)
  touch('touchmove', 100)
  touch('touchend', 100)
}

describe('swiping between groups on the dashboard', () => {
  beforeEach(() => {
    replace.mockClear()
  })

  it('shows the group swiped to', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    swipeLeft()

    await waitFor(() => wrapper.find('h1').text().includes('Ski trip'))
  })

  it('brings that group in with the finger', async () => {
    const { wrapper } = await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    touch('touchstart', 300)
    touch('touchmove', 200)
    await wrapper.vm.$nextTick()

    // Named while the finger is still down, so a swipe can be thought better of.
    const peek = wrapper.find('[data-testid="swipe-peek"]')
    expect(peek.exists()).toBe(true)
    expect(peek.text()).toContain('Ski trip')

    touch('touchend', 200)
    await settle()
  })

  it('keeps the address on the group being shown', async () => {
    await mountView(DashboardView, { api: twoGroups(), groups: [] })
    await settle()

    swipeLeft()

    // Otherwise reloading lands back on the group that was swiped away from.
    await waitFor(() =>
      replace.mock.calls.some(
        ([to]) => to?.name === 'group' && to?.params?.groupId === 'group-2',
      ),
    )
  })
})
