import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount, type VueWrapper } from '@vue/test-utils'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import { useGroupsStore } from '@/stores/groups'
import { resetDatabase } from '@/offline/db'

/**
 * Swiping across the screen to change group.
 *
 * The app is on one group at a time, so moving between them is the navigation it
 * does most, and it used to cost a tap on the mark and a tap in a sheet. The
 * group being swiped to comes in with the finger: a gesture that only acted on
 * release would leave the screen dead while a finger dragged across it, with no
 * way to change your mind.
 *
 * The gesture listens on the window rather than wrapping the page, because a
 * short screen is mostly empty space and a swipe that only worked over the
 * content would look broken exactly there.
 */

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

/** The shell's page, which the gesture slides out from under the incoming one. */
function pageElement(): HTMLElement {
  const page = document.createElement('main')
  page.setAttribute('data-swipe-page', '')
  document.body.appendChild(page)
  return page
}

/**
 * Mounted swipes, so every one of them is taken back down again.
 *
 * They listen on the window, so one left behind by an earlier test goes on
 * answering gestures in the next one - and a component left mid-drag cancels
 * every move it sees, which is exactly what these tests measure.
 */
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

/**
 * A touch, as the browser reports one.
 *
 * jsdom has no TouchEvent, so the fields the component reads are set on a plain
 * event. Dispatched on the window, because that is where the component listens,
 * which is the part worth keeping honest.
 */
function touch(type: string, points: Point[], target: EventTarget = window): Event {
  const event = new Event(type, { bubbles: true, cancelable: type === 'touchmove' })
  const list = points.map((point) => ({ clientX: point.x, clientY: point.y }))

  Object.assign(event, { touches: list, changedTouches: list })
  target.dispatchEvent(event)
  return event
}

/**
 * Whether the swipe took the gesture off the browser.
 *
 * The one thing that matters about when the decision is made: a browser told a
 * move was allowed through starts scrolling and keeps scrolling for the rest of
 * the gesture, and cancelling a later move does not take that back.
 */
function claimed(from: Point, to: Point): boolean {
  touch('touchstart', [from])
  const claim = touch('touchmove', [to]).defaultPrevented
  // Put the finger down again, so nothing is left mid-gesture.
  touch('touchend', [to])
  return claim
}

/** A finger going across, in the stages the browser reports them in. */
function drag(from: Point, ...path: Point[]): void {
  touch('touchstart', [from])
  for (const point of path) touch('touchmove', [point])
}

function release(at: Point): void {
  touch('touchend', [at])
}

/** Lets every stage of the landing animation run. */
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
    document.querySelectorAll('[data-swipe-page]').forEach((page) => page.remove())
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

    // On screen while the finger is still down: this is the answer to a gesture,
    // not a report of one.
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

    // Further across the screen means further in: the two move together.
    expect(nearer).toBeLessThan(near)
  })

  it('takes the page it is leaving with it', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 200, y: 400 })

    // The two pages move as one, which is what makes it a page turning rather
    // than something sliding over the top.
    expect(page.style.transform).toBe('translateX(-100px)')
  })

  it('puts everything back when the swipe is abandoned', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 280, y: 400 })
    release({ x: 280, y: 400 })
    await land()

    // Changing your mind part way is the point of showing it during the gesture.
    expect(store.mainGroupId).toBe('g1')
    expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
    expect(page.style.transform).toBe('')
  })

  it('lands the new group at the top of its screen', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    pageElement()
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
    Object.defineProperty(window, 'scrollY', { value: 400, configurable: true })
    mountSwipe()

    try {
      drag({ x: 300, y: 400 }, { x: 100, y: 400 })
      release({ x: 100, y: 400 })
      await land()

      // Otherwise it opens wherever the last group was being read, which is
      // nowhere in this one, and a phone answers a scroll position that jumps by
      // a few hundred pixels by sliding its own toolbar about, taking the tab bar
      // with it.
      expect(store.mainGroupId).toBe('g2')
      expect(scrollTo).toHaveBeenCalledWith(0, 0)
    } finally {
      scrollTo.mockRestore()
      Object.defineProperty(window, 'scrollY', { value: 0, configurable: true })
    }
  })

  it('stops the browser holding the old scroll through the change', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    mountSwipe()

    drag({ x: 300, y: 400 }, { x: 200, y: 400 })

    // Anchoring is right when something loads in above what you are reading, and
    // wrong when the whole screen becomes another group's.
    expect(page.style.overflowAnchor).toBe('none')

    release({ x: 200, y: 400 })
    await land()

    // Only for the length of the swipe.
    expect(page.style.overflowAnchor).toBe('')
  })

  it('lets go of the page once it has landed', async () => {
    withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const page = pageElement()
    const wrapper = mountSwipe()

    drag({ x: 300, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    // An element that keeps a transform is what everything fixed inside it is
    // positioned against, so the page has to be handed back as it was found.
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

    // Too slow to be a flick, but nobody drags a page four fifths of the way
    // across by accident.
    expect(store.mainGroupId).toBe('g2')
  })

  it('takes a flick that ends before a single move arrives', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    // A fast enough gesture reports a start and an end and nothing in between.
    touch('touchstart', [{ x: 300, y: 400 }])
    release({ x: 100, y: 400 })
    await land()

    expect(store.mainGroupId).toBe('g2')
  })

  it('ignores a scroll down the page', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const wrapper = mountSwipe()

    // The gesture it has to stay out of the way of: changing group under someone
    // reading a list is the worst thing this could do.
    drag({ x: 300, y: 600 }, { x: 290, y: 400 }, { x: 240, y: 100 })
    release({ x: 240, y: 100 })
    await land()

    expect(store.mainGroupId).toBe('g1')
    expect(wrapper.find('[data-testid="swipe-peek"]').exists()).toBe(false)
  })

  it('stays out of a scroll that wanders back across', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    // Left to the browser at the first movement, and read whole at the end it is
    // still a scroll: 200 across against 320 down is nobody's swipe.
    drag({ x: 300, y: 600 }, { x: 295, y: 300 }, { x: 100, y: 280 })
    release({ x: 100, y: 280 })
    await land()

    expect(store.mainGroupId).toBe('g1')
  })

  it('picks up a sweep that started too steeply to claim', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    mountSwipe()

    /*
     * A thumb pivots at the base of the hand, so a sweep across the screen arcs,
     * and the first report of one can be steeper than it is wide. There is no
     * fighting the browser for it by then - but by the end it is plainly a swipe,
     * and at that point there is nothing left to fight about.
     */
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

    // Left alone as a scroll, and then no touchend: the next swipe still has to
    // work rather than being refused for the rest of the session.
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
      // The picker and the icon chooser are full of things to drag past, and a
      // gesture over a sheet belongs to the sheet.
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

    // Dragged back past where it started, it is the group on the other side.
    expect(wrapper.find('[data-testid="swipe-peek"]').text()).toContain('Alpha')
  })

  it('stops listening once the screen is gone', async () => {
    const store = withGroups(group('g1', 'Alpha'), group('g2', 'Beta'))
    const wrapper = mountSwipe()

    wrapper.unmount()
    drag({ x: 300, y: 400 }, { x: 100, y: 400 })
    release({ x: 100, y: 400 })
    await land()

    // A window listener outlives its component unless it is taken back down.
    expect(store.mainGroupId).toBe('g1')
  })

  /**
   * Which of the two gestures this is, decided on the first movement.
   *
   * A thumb pivots at the base of the hand, so a sweep across the screen arcs as
   * it goes. That arc was being scrolled: the page ran up under the finger, and a
   * phone answers a page running up by hiding its toolbar, which drops everything
   * fixed to the bottom of the window - the tab bar included.
   */
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
      // Left to right with the arc that comes with it: the case that was being
      // scrolled instead of swiped.
      expect(claimed({ x: 60, y: 500 }, { x: 80, y: 486 })).toBe(true)
    })

    it('leaves a movement down the screen to the browser', () => {
      expect(claimed({ x: 200, y: 400 }, { x: 204, y: 380 })).toBe(false)
    })

    it('leaves a movement too small to read alone', () => {
      // Nothing is claimed and nothing is cancelled until there is something to
      // go on, and a few pixels is not enough to tell the two apart.
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

      // The browser is already scrolling by now, and it will not stop.
      expect(touch('touchmove', [{ x: 100, y: 555 }]).defaultPrevented).toBe(false)
    })
  })

  /**
   * Someone who has asked their phone for less motion.
   */
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

      // The reason to claim it even with nothing to show: otherwise the page runs
      // about under the finger, which is the thing being fixed.
      expect(claimed({ x: 300, y: 400 }, { x: 280, y: 402 })).toBe(true)
    })
  })
})

/** The pixels in a `translateX(...)` transform, whichever way it is written. */
function offsetOf(style: string | undefined): number {
  const match = /translateX\((-?[\d.]+)px\)/.exec(style ?? '')
  expect(match).not.toBeNull()
  return Number(match![1])
}