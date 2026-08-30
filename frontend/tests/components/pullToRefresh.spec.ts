import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import PullToRefresh from '@/components/ui/PullToRefresh.vue'

/**
 * Pull down at the top of a screen to sync.
 *
 * The app syncs on its own, but there was no way to ask, and watching a stale figure
 * wondering whether anything is happening is a bad place to leave somebody.
 *
 * The page is what scrolls here, not the window, so the gesture watches the page:
 * only from the very top, and only when the pull is more down than across, which is
 * what keeps it out of the way of the swipe that changes group.
 */

const mounted: VueWrapper[] = []

function mountPull() {
  const wrapper = mount(PullToRefresh, { global: { stubs: { teleport: true } } })
  mounted.push(wrapper)
  return wrapper
}

/** The shell's page, which is the thing that scrolls and the thing that moves. */
function pageElement(scrollTop = 0): HTMLElement {
  const page = document.createElement('main')
  page.setAttribute('data-app-page', '')
  Object.defineProperty(page, 'scrollTop', { value: scrollTop, writable: true, configurable: true })
  document.body.appendChild(page)
  return page
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

/** A finger going down, in the stages a browser reports them. */
function pull(from: Point, ...path: Point[]): void {
  touch('touchstart', [from])
  for (const point of path) touch('touchmove', [point])
}

const release = () => touch('touchend', [])

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  for (const wrapper of mounted.splice(0)) wrapper.unmount()
  vi.useRealTimers()
  document.querySelectorAll('[data-app-page]').forEach((page) => page.remove())
})

describe('PullToRefresh', () => {
  it('shows nothing until something is pulled', () => {
    pageElement()
    const wrapper = mountPull()

    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(false)
  })

  it('follows the finger down from the top', async () => {
    const page = pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 140 })
    await flushPromises()

    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(true)
    // Half the distance: a page that tracks a finger exactly reads as dragged
    // rather than stretched, and there is nothing underneath to drag it to.
    expect(page.style.transform).toBe('translateY(20px)')
  })

  it('turns the arrow over exactly when letting go would sync', async () => {
    pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 130 })
    await flushPromises()
    const early = wrapper.find('[data-testid="pull-arrow"]').attributes('style')

    touch('touchmove', [{ x: 200, y: 300 }])
    await flushPromises()
    const enough = wrapper.find('[data-testid="pull-arrow"]').attributes('style')

    // Pointing up is a promise about what release will do, not decoration.
    expect(early).toContain('rotate(')
    expect(early).not.toContain('rotate(180deg)')
    expect(enough).toContain('rotate(180deg)')
  })

  it('refuses to be pulled from halfway down a list', async () => {
    const page = pageElement(320)
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 200 })
    await flushPromises()

    // That gesture belongs to the list.
    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(false)
    expect(page.style.transform).toBe('')
  })

  it('refreshes when the pull is far enough, and holds while it works', async () => {
    pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 260 })
    release()
    await flushPromises()

    expect(wrapper.emitted('refresh')).toHaveLength(1)
    // Held, so the spinner reads as doing it rather than as done.
    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(true)
  })

  it('shows a spinner while it works rather than turning the arrow further', async () => {
    pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 260 })
    release()
    await flushPromises()

    // An arrow means a direction; a whole turn of one means nothing. Once the pull
    // has been let go the only question left is whether it is finished, and that is
    // what a ring going round answers.
    expect(wrapper.find('[data-testid="pull-spinner"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="pull-arrow"]').exists()).toBe(false)
  })

  it('lets go only when the screen says it is finished', async () => {
    const page = pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 260 })
    release()
    await flushPromises()
    expect(page.style.transform).not.toBe('translateY(0px)')

    wrapper.vm.done()
    await vi.advanceTimersByTimeAsync(400)

    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(false)
    // And the page is handed back as it was found: a lasting transform changes
    // what everything fixed inside it is positioned against.
    expect(page.style.transform).toBe('')
  })

  it('springs back without refreshing when the pull was short', async () => {
    const wrapper = mountPull()
    pageElement()

    pull({ x: 200, y: 100 }, { x: 200, y: 120 })
    release()
    await vi.advanceTimersByTimeAsync(400)

    expect(wrapper.emitted('refresh')).toBeUndefined()
    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(false)
  })

  it('ignores a pull upwards', async () => {
    pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 300 }, { x: 200, y: 200 })
    release()
    await flushPromises()

    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(false)
    expect(wrapper.emitted('refresh')).toBeUndefined()
  })

  it('leaves a sideways drag to the gesture that changes group', async () => {
    pageElement()
    const wrapper = mountPull()

    // More across than down: the swipe's business, not this one's.
    pull({ x: 300, y: 100 }, { x: 180, y: 130 })
    release()
    await flushPromises()

    expect(wrapper.find('[data-testid="pull-indicator"]').exists()).toBe(false)
    expect(wrapper.emitted('refresh')).toBeUndefined()
  })

  it('cancels a move it has claimed, so the browser does not bounce as well', () => {
    pageElement()
    mountPull()

    touch('touchstart', [{ x: 200, y: 100 }])
    const claimed = touch('touchmove', [{ x: 200, y: 160 }])

    expect(claimed.defaultPrevented).toBe(true)
  })

  it('leaves a move it has not claimed alone', () => {
    pageElement(200)
    mountPull()

    touch('touchstart', [{ x: 200, y: 100 }])
    const move = touch('touchmove', [{ x: 200, y: 160 }])

    expect(move.defaultPrevented).toBe(false)
  })

  it('ignores a pinch', async () => {
    pageElement()
    const wrapper = mountPull()

    touch('touchstart', [{ x: 200, y: 100 }, { x: 220, y: 100 }])
    touch('touchmove', [{ x: 200, y: 260 }])
    release()
    await flushPromises()

    expect(wrapper.emitted('refresh')).toBeUndefined()
  })

  it('leaves a gesture inside a sheet to the sheet', async () => {
    pageElement()
    const wrapper = mountPull()

    const sheet = document.createElement('div')
    sheet.setAttribute('role', 'dialog')
    document.body.appendChild(sheet)

    try {
      touch('touchstart', [{ x: 200, y: 100 }], sheet)
      touch('touchmove', [{ x: 200, y: 260 }], sheet)
      touch('touchend', [], sheet)
      await flushPromises()

      expect(wrapper.emitted('refresh')).toBeUndefined()
    } finally {
      sheet.remove()
    }
  })

  it('does not pull twice while it is already refreshing', async () => {
    pageElement()
    const wrapper = mountPull()

    pull({ x: 200, y: 100 }, { x: 200, y: 260 })
    release()
    await flushPromises()

    pull({ x: 200, y: 100 }, { x: 200, y: 260 })
    release()
    await flushPromises()

    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })

  it('stops listening once the screen is gone', async () => {
    const page = pageElement()
    const wrapper = mountPull()

    wrapper.unmount()
    pull({ x: 200, y: 100 }, { x: 200, y: 260 })
    release()
    await flushPromises()

    // A window listener outlives its component unless it is taken back down.
    expect(page.style.transform).toBe('')
  })
})
