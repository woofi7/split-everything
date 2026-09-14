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

/**
 * The colour the app is wearing.
 *
 * Set on the root element as the brand tokens themselves, because every surface in
 * the stylesheet is mixed from them: the background, the cards, the borders. Which
 * is what makes a group's colour worth having - the whole screen says which group
 * you are on before a word of it is read.
 */
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

    // The list as the server answers it, because the app loads groups the moment
    // it is up and a store seeded by hand would be replaced by that answer.
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

    // A group that has never been given a colour must not quietly overrule the
    // colour somebody chose for their own account.
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
    // An older client meeting a newer server's name: better the person's own
    // colour than an app with no accent at all.
    await mountApp([testGroup({ themeName: 'chartreuse' })])

    expect(brandFill()).toBe(findAccent('violet')!.shades[2])
  })
})
