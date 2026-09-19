import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount, type VueWrapper } from '@vue/test-utils'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import { useGroupsStore } from '@/stores/groups'
import { resetDatabase } from '@/offline/db'

const group = (id: string, name: string) => ({
  id,
  name,
  baseCurrency: 'CAD',
  colorHex: '#4f46e5',
  iconName: null,
  isArchived: false,
  lineageId: `l-${id}`,
  members: [],
  memberCount: 2,
  myNetBalance: 0,
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

function pageElement(): HTMLElement {
  const page = document.createElement('main')
  page.setAttribute('data-app-page', '')
  document.body.appendChild(page)
  return page
}

const mounted: VueWrapper[] = []

function mountSwipe(): VueWrapper {
  const wrapper = mount(GroupSwipe, { global: { stubs: { teleport: true } } })
  mounted.push(wrapper)
  return wrapper
}

interface Point {
  x: number
  y: number
}

function touch(type: string, points: Point[], target: EventTarget = window): Event {
  const event = new Event(type, { bubbles: true, cancelable: type === 'touchmove' })
  const list = points.map((point) => ({ clientX: point.x, clientY: point.y }))

  Object.assign(event, { touches: list, changedTouches: list })
  target.dispatchEvent(event)
  return event
}

function claimed(from: Point, to: Point): boolean {
  touch('touchstart', [from])
  const claim = touch('touchmove', [to]).defaultPrevented
  touch('touchend', [to])
  return claim
}

function drag(from: Point, ...path: Point[]): void {
  touch('touchstart', [from])
  for (const point of path) touch('touchmove', [point])
}

function release(at: Point): void {
  touch('touchend', [at])
}

async function land(): Promise<void> {
  await vi.advanceTimersByTimeAsync(1000)
}

describe('GroupSwipe', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
    vi.useFakeTimers()
  })

  afterEach(() => {
    for (const wrapper of mounted.splice(0)) wrapper.unmount()
    vi.useRealTimers()
    document.querySelectorAll('[data-app-page]').forEach((page) => page.remove())
  })

  it('moves to the next group on a swipe to the left', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 250, y: 402 }, { x: 100, y: 405 })
    release({ x: 100, y: 405 })
    await land()

    expect(store.mainGroupId).toBe('g2')
  })

  it('moves back on a swipe to the right', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    store.setMainGroup('g2')
    mountSwipe()

    drag({ x: 100, y: 400 }, { x: 160, y: 400 }, { x: 300, y: 395 })
    release({ x: 300, y: 395 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('brings the other group in with the finger', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'), group('g3', 'Gamma'))
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 200, y: 400 })
    await wrapper.vm.$nextTick()

    const peek = wrapper.find('[data-testid="swipe-peek"]')
    expect(peek.exists()).toBe(true)
    expect(peek.text()).toContain('Beta')
    expect(wrapper.find('[data-testid="peek-position"]').text()).toBe('2 of 3')
  })

  it('follows the finger, rather than jumping when it lets go', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 240, y: 400 })
    await wrapper.vm.$nextTick()
    const near = offsetOf(wrapper.find('[data-testid="swipe-peek"]').attributes('style'))

    touch('touchmove', [{ x: 120, y: 400 }])
    await wrapper.vm.$nextTick()
    const nearer = offsetOf(wrapper.find('[data-testid="swipe-peek"]').attributes('style'))

    expect(nearer).toBeLessThan(near)
  })

  it('takes the page it is leaving with it', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 200, y: 400 })

    expect(page.style.transform).toBe('translateX(-100px)')
  })

  it('puts everything back when the swipe is abandoned', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 280, y: 400 })
    release({ x: 280, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g1')
    expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
    expect(page.style.transform).toBe('')
  })

  it('lands the new group at the top of its screen', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    page.scrollTop = 400
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g2')
    expect(page.scrollTop).toBe(0)
  })

  it('stops the browser holding the old scroll through the change', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 200, y: 400 })

    expect(page.style.overflowAnchor).toBe('none')

    release({ x: 200, y: 400 })
    await land()

    expect(page.style.overflowAnchor).toBe('')
  })

  it('lets go of the page once it has landed', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    expect(page.style.transform).toBe('')
    expect(page.style.transition).toBe('')
    expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
  })

  it('completes a slow drag most of the way across', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 900, y: 400 }])
    vi.advanceTimersByTime(2000)
    touch('touchmove', [{ x: 800, y: 400 }])
    touch('touchmove', [{ x: 100, y: 400 }])
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g2')
  })

  it('takes a flick that ends before a single move arrives', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 300, y: 400 }])
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g2')
  })

  it('ignores a scroll down the page', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const wrapper = mountSwipe()

    drag({ x: 300, y: 600 }, { x: 290, y: 400 }, { x: 240, y: 100 })
    release({ x: 240, y: 100 })
    await land()

    expect(store.mainGroupId).toBe('g1')
    expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
  })

  it('stays out of a scroll that wanders back across', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    drag({ x: 300, y: 600 }, { x: 295, y: 300 }, { x: 100, y: 280 })
    release({ x: 100, y: 280 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('picks up a sweep that started too steeply to claim', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 60, y: 500 }])
    expect(touch('touchmove', [{ x: 70, y: 480 }]).defaultPrevented).toBe(false)
    touch('touchmove', [{ x: 150, y: 462 }])
    touch('touchmove', [{ x: 280, y: 450 }])
    release({ x: 300, y: 448 })
    await land()

    expect(store.mainGroupId).toBe('g2')
  })

  it('starts afresh when a gesture was never reported as over', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 200, y: 600 }])
    touch('touchmove', [{ x: 202, y: 500 }])

    drag({ x: 300, y: 400 }, { x: 250, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g2')
  })

  it('ignores a tap', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 303, y: 402 })
    release({ x: 303, y: 402 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('ignores a pinch, which is two fingers going opposite ways', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 300, y: 400 }, { x: 320, y: 400 }])
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('ignores a second finger arriving part way through', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 300, y: 400 }])
    touch('touchmove', [{ x: 250, y: 400 }, { x: 200, y: 500 }])
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('leaves a gesture inside a sheet to the sheet', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    const sheet = document.createElement('div')
    sheet.setAttribute('role', 'dialog')
    const row = document.createElement('button')
    sheet.appendChild(row)
    document.body.appendChild(sheet)

    try {
      touch('touchstart', [{ x: 300, y: 400 }], row)
      touch('touchmove', [{ x: 200, y: 400 }], row)
      touch('touchend', [{ x: 100, y: 400 }], row)
      await land()

      expect(store.mainGroupId).toBe('g1')
    } finally {
      sheet.remove()
    }
  })

  it('ignores a gesture the browser cancelled', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    touch('touchstart', [{ x: 300, y: 400 }])
    touch('touchcancel', [{ x: 200, y: 400 }])
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('has nowhere to go with a single group', async () => {
    const store = withGroups(group('g1', 'Alpha'))
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g1')
    expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
  })

  it('brings in the other group when the finger goes back the other way', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'), group('g3', 'Gamma'))
    store.setMainGroup('g2')
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 240, y: 400 })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="swipe-peek"]').text()).toContain('Gamma')

    touch('touchmove', [{ x: 380, y: 400 }])
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="swipe-peek"]').text()).toContain('Alpha')
  })

  it('stops listening once the screen is gone', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const wrapper = mountSwipe()

    wrapper.unmount()
    drag({ x: 300, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  describe('deciding between a swipe and a scroll', () => {
    beforeEach(() => {
      withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
      pageElement()
      mountSwipe()
    })

    it('takes the gesture on the first movement across', () => {
      expect(claimed({ x: 200, y: 400 }, { x: 214, y: 402 })).toBe(true)
    })

    it('takes a thumb sweeping right, arc and all', () => {
      expect(claimed({ x: 60, y: 500 }, { x: 80, y: 486 })).toBe(true)
    })

    it('leaves a movement down the screen to the browser', () => {
      expect(claimed({ x: 200, y: 400 }, { x: 204, y: 380 })).toBe(false)
    })

    it('leaves a movement too small to read alone', () => {
      expect(claimed({ x: 200, y: 400 }, { x: 204, y: 402 })).toBe(false)
    })

    it('keeps cancelling once it has the gesture', () => {
      touch('touchstart', [{ x: 200, y: 400 }])
      touch('touchmove', [{ x: 180, y: 402 }])

      expect(touch('touchmove', [{ x: 120, y: 430 }]).defaultPrevented).toBe(true)
    })

    it('does not come back to a gesture it left alone', () => {
      touch('touchstart', [{ x: 200, y: 600 }])
      touch('touchmove', [{ x: 202, y: 560 }])

      expect(touch('touchmove', [{ x: 100, y: 555 }]).defaultPrevented).toBe(false)
    })
  })

  describe('with less motion asked for', () => {
    let matchMedia: typeof window.matchMedia

    beforeEach(() => {
      matchMedia = window.matchMedia
      window.matchMedia = ((query: string) => ({
        matches: query.includes('prefers-reduced-motion'),
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
      })) as never
    })

    afterEach(() => {
      window.matchMedia = matchMedia
    })

    it('changes group with nothing sliding about', async () => {
      const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
      const page = pageElement()
      const wrapper = mountSwipe()

      drag({ x: 300, y: 400 }, { x: 200, y: 400 })
      expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
      expect(page.style.transform).toBe('')

      release({ x: 100, y: 400 })
      await land()

      expect(store.mainGroupId).toBe('g2')
    })

    it('still takes the gesture, so the page cannot scroll under it', () => {
      withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
      mountSwipe()

      expect(claimed({ x: 300, y: 400 }, { x: 280, y: 402 })).toBe(true)
    })
  })
})

function offsetOf(style: string | undefined): number {
  const match = /translateX\((-?[\d.]+)px\)/.exec(style ?? '')
  expect(match).not.toBeNull()
  return Number(match![1])
}