import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import App from '@/App.vue'
import { resetDatabase } from '@/offline/db'
import { setApiClient } from '@/api/provider'
import { useExpensesStore } from '@/stores/expenses'
import { SyncEngine } from '@/offline/syncEngine'
import { fakeApi, fakeSyncApi, settle, signInForTests } from '../support/viewHarness'

vi.mock('vue-router', () => ({
  RouterView: { template: '<div />' },
}))

vi.mock('@/router', () => ({ isNavigating: { value: false }, router: {} }))

describe('syncing around the sign-in page', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  function mountApp(signedIn: boolean) {
    const api = fakeApi()
    setApiClient(api as never)
    if (signedIn) signInForTests()

    const sync = fakeSyncApi()
    useExpensesStore().attachSync(new SyncEngine(sync, () => true))

    return { wrapper: mount(App), api, sync }
  }

  it('sends nothing when the tab becomes visible with nobody signed in', async () => {
    const { api, sync } = mountApp(false)

    document.dispatchEvent(new Event('visibilitychange'))
    await settle()

    expect(sync.pull).not.toHaveBeenCalled()
    expect(sync.push).not.toHaveBeenCalled()
    expect(api.get).not.toHaveBeenCalled()
  })

  it('sends nothing when the network comes back with nobody signed in', async () => {
    const { api, sync } = mountApp(false)

    window.dispatchEvent(new Event('online'))
    await settle()

    expect(sync.pull).not.toHaveBeenCalled()
    expect(api.get).not.toHaveBeenCalled()
  })

  it('syncs on the same events once someone is signed in', async () => {
    const { sync } = mountApp(true)
    sync.pull.mockClear()

    window.dispatchEvent(new Event('online'))
    await settle()

    expect(sync.pull).toHaveBeenCalled()
  })
})
