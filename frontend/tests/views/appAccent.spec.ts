import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import App from '@/App.vue'
import { db, resetDatabase } from '@/offline/db'
import { setApiClient } from '@/api/provider'
import { useExpensesStore } from '@/stores/expenses'
import { useGroupsStore } from '@/stores/groups'
import { SyncEngine } from '@/offline/syncEngine'
import { findAccent } from '@/domain/themes'
import { fakeApi, fakeSyncApi, settle, signInForTests, testGroup } from '../support/viewHarness'

vi.mock('vue-router', () => ({
  RouterView: { template: '<div />' },
}))

vi.mock('@/router', () => ({ isNavigating: { value: false }, router: {} }))

describe('the colour the app wears', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
    document.documentElement.removeAttribute('style')
  })

  async function mountApp(groups: Array<ReturnType<typeof testGroup>>, accountAccent = 'violet') {
    setApiClient(fakeApi() as never)
    const auth = signInForTests()
    auth.user = { ...auth.user, themeName: accountAccent } as never

    for (const group of groups) await db.groups.put(group)

    const store = useGroupsStore()
    store.attachApi(
      fakeApi({
        '/groups': groups.map((group) => ({ ...group, memberCount: 2, lastActivityAt: null })),
      }) as never,
    )
    store.groups = groups
    store.setMainGroup(groups[0]?.id ?? '')

    useExpensesStore().attachSync(new SyncEngine(fakeSyncApi() as never, () => true))

    const wrapper = mount(App)
    await settle()

    return { wrapper, store }
  }

  const brandFill = () => document.documentElement.style.getPropertyValue('--color-brand-600')

  it('wears the account colour when the group has none of its own', async () => {
    await mountApp([testGroup({ themeName: null })])

    expect(brandFill()).toBe(findAccent('violet')!.shades[2])
    expect(document.documentElement.dataset.accent).toBe('violet')
  })

  it('wears the group colour while that group is the one being looked at', async () => {
    await mountApp([testGroup({ themeName: 'teal' })])

    expect(brandFill()).toBe(findAccent('teal')!.shades[2])
    expect(document.documentElement.dataset.accent).toBe('teal')
  })

  it('changes colour with the group', async () => {
    const trip = testGroup({ id: 'group-2', name: 'Ski trip', themeName: 'amber' })
    const { store } = await mountApp([testGroup({ themeName: 'teal' }), trip])

    store.setMainGroup('group-2')
    await settle()

    expect(brandFill()).toBe(findAccent('amber')!.shades[2])
  })

  it('falls back to the account colour for a name it does not know', async () => {
    await mountApp([testGroup({ themeName: 'chartreuse' })])

    expect(brandFill()).toBe(findAccent('violet')!.shades[2])
  })
})
