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

const replace = vi.fn()

vi.mock('vue-router', () => ({
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

    await waitFor(() =>
      replace.mock.calls.some(
        ([to]) => to?.name === 'group' && to?.params?.groupId === 'group-2',
      ),
    )
  })
})
