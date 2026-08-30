import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import UpdatePrompt from '@/components/ui/UpdatePrompt.vue'
import { pwa, resetPwa } from '../support/pwaRegister'

/**
 * A new version is waiting.
 *
 * A service worker that has downloaded a new build waits for every page using the
 * old one to close, and on a phone a page is never closed: the app sat on whatever
 * version it was installed with. That is how a client ends up older than the server
 * it is talking to.
 */

const mountPrompt = async () => {
  const wrapper = mount(UpdatePrompt, { global: { stubs: { teleport: true } } })
  await flushPromises()
  return wrapper
}

beforeEach(() => resetPwa())
afterEach(() => resetPwa())

describe('UpdatePrompt', () => {
  it('says nothing while the app is up to date', async () => {
    const wrapper = await mountPrompt()

    expect(wrapper.find('[data-testid="update-prompt"]').exists()).toBe(false)
  })

  it('registers the worker itself, rather than waiting to be asked', async () => {
    await mountPrompt()

    expect(pwa.options).not.toBeNull()
    expect(pwa.options?.immediate).toBe(true)
  })

  it('asks when a new version is waiting', async () => {
    const wrapper = await mountPrompt()

    pwa.options?.onNeedRefresh?.()
    await flushPromises()

    expect(wrapper.find('[data-testid="update-prompt"]').text()).toContain('new version')
  })

  it('reloads every tab when told to, since they share one replica', async () => {
    const wrapper = await mountPrompt()
    pwa.options?.onNeedRefresh?.()
    await flushPromises()

    await wrapper.find('[data-testid="apply-update"]').trigger('click')
    await flushPromises()

    expect(pwa.applied).toBe(true)
    expect(pwa.reloaded).toBe(true)
  })

  it('takes Later for an answer', async () => {
    const wrapper = await mountPrompt()
    pwa.options?.onNeedRefresh?.()
    await flushPromises()

    await wrapper.find('[data-testid="dismiss-update"]').trigger('click')
    await flushPromises()

    // Nothing is lost by waiting: the outbox holds what has not been sent.
    expect(wrapper.find('[data-testid="update-prompt"]').exists()).toBe(false)
    expect(pwa.applied).toBe(false)
  })

  it('mentions being ready to work offline, once', async () => {
    const wrapper = await mountPrompt()

    pwa.options?.onOfflineReady?.()
    await flushPromises()

    expect(wrapper.find('[data-testid="offline-ready"]').exists()).toBe(true)
  })
})
