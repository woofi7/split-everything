import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import NavigationProgress from '@/components/ui/NavigationProgress.vue'
import { isNavigating, router } from '@/router'

/**
 * Saying that a screen is on its way.
 *
 * Screens are fetched on demand and the guard may have to bring a session back
 * first, so a tap can take a moment on a phone. With nothing on screen a slow tap
 * reads as an ignored one.
 */
describe('NavigationProgress', () => {
  const bar = '[data-testid="navigation-progress"]'

  it('says nothing about a navigation that is already done', () => {
    const wrapper = mount(NavigationProgress, { props: { active: false } })

    expect(wrapper.find(bar).exists()).toBe(false)
  })

  it('holds back on an instant navigation', async () => {
    vi.useFakeTimers()
    const wrapper = mount(NavigationProgress, { props: { active: true, delayMs: 150 } })

    await vi.advanceTimersByTimeAsync(100)
    await wrapper.setProps({ active: false })
    await vi.advanceTimersByTimeAsync(500)

    // Most navigations are instant. A bar that flashes on every tap is noise.
    expect(wrapper.find(bar).exists()).toBe(false)
    vi.useRealTimers()
  })

  it('appears once a navigation is slow enough to be worth mentioning', async () => {
    vi.useFakeTimers()
    const wrapper = mount(NavigationProgress, { props: { active: true, delayMs: 150 } })

    await vi.advanceTimersByTimeAsync(200)
    await wrapper.vm.$nextTick()

    expect(wrapper.find(bar).exists()).toBe(true)
    vi.useRealTimers()
  })

  it('goes as soon as the screen arrives', async () => {
    vi.useFakeTimers()
    const wrapper = mount(NavigationProgress, { props: { active: true, delayMs: 10 } })
    await vi.advanceTimersByTimeAsync(50)
    await wrapper.vm.$nextTick()

    await wrapper.setProps({ active: false })

    expect(wrapper.find(bar).exists()).toBe(false)
    vi.useRealTimers()
  })

  it('announces itself without claiming a position it does not know', async () => {
    vi.useFakeTimers()
    const wrapper = mount(NavigationProgress, { props: { active: true, delayMs: 10 } })
    await vi.advanceTimersByTimeAsync(50)
    await wrapper.vm.$nextTick()

    const element = wrapper.find(bar)
    expect(element.attributes('role')).toBe('status')
    expect(element.attributes('aria-label')).toBe('Loading')
    // Indeterminate: there is no progress to report, only a wait.
    expect(element.attributes('aria-valuenow')).toBeUndefined()
    vi.useRealTimers()
  })
})

describe('the router reporting a navigation', () => {
  it('is open while navigating and closed afterwards', async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await router.replace('/sign-in')
    await router.isReady()

    expect(isNavigating.value).toBe(false)

    await router.push('/join/some-token')

    // Closed again by the time the screen is on, or the bar would never go.
    expect(isNavigating.value).toBe(false)
  })

  it('is open while a guard is still deciding', async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await router.replace('/sign-in')
    await router.isReady()

    let seen = false
    const stop = router.beforeResolve(() => {
      seen = isNavigating.value
      return true
    })

    await router.push('/join/another-token')
    stop()

    expect(seen).toBe(true)
  })
})
