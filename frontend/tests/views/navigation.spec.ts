import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import type { Component } from 'vue'
import ActivityView from '@/views/ActivityView.vue'
import ExpenseFormView from '@/views/ExpenseFormView.vue'
import ConflictsView from '@/views/ConflictsView.vue'
import ExpenseView from '@/views/ExpenseView.vue'
import GroupSettingsView from '@/views/GroupSettingsView.vue'
import DashboardView from '@/views/DashboardView.vue'
import ImportView from '@/views/ImportView.vue'
import JoinView from '@/views/JoinView.vue'
import NewGroupView from '@/views/NewGroupView.vue'
import NotFoundView from '@/views/NotFoundView.vue'
import ProfileView from '@/views/ProfileView.vue'
import SettleView from '@/views/SettleView.vue'
import SignInView from '@/views/SignInView.vue'
import StatsView from '@/views/StatsView.vue'
import { GROUP_ID, fakeApi, mountView, testExpense, testGroup } from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({
    params: { groupId: GROUP_ID, expenseId: 'expense-1', token: 'invite-token' },
    query: {},
    fullPath: '/',
  }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

vi.mock('@/import/statementWorkerClient', () => ({
  StatementWorkerClient: class {
    parseCsv = vi.fn()
    parsePdf = vi.fn()
    dispose = vi.fn()
  },
}))

const api = () =>
  fakeApi({
    '/groups': () => testGroup(),
    '/activity': () => ({ items: [] }),
    '/stats': () => ({
      currency: 'CAD',
      totalSpend: 0,
      myShare: 0,
      myPaid: 0,
      expenseCount: 0,
      spendOverTime: [],
      byCategory: [],
      byMember: [],
    }),
    '/import/category-rules': () => [],
    '/invites/invite-token': () => ({
      groupId: GROUP_ID,
      groupName: 'Roommates',
      iconName: null,
      invitedByName: 'Alice',
      memberCount: 2,
      isRedeemable: true,
    }),
    '/auth/capabilities': () => ({ googleConfigured: true, developmentSignIn: false }),
  })

/**
 * The tab bar is how people get anywhere. A screen that hides it is a dead end
 * they have to use the browser back button to escape, so only the two screens
 * reached before signing in are allowed to.
 */
const inApp: Array<[string, Component]> = [
  ['Dashboard', DashboardView],
  ['Group settings', GroupSettingsView],
  ['New group', NewGroupView],
  ['Add expense', ExpenseFormView],
  ['Expense', ExpenseView],
  ['Settle up', SettleView],
  ['Activity', ActivityView],
  ['Stats', StatsView],
  ['Import', ImportView],
  ['Conflicts', ConflictsView],
  ['Profile', ProfileView],
  ['Not found', NotFoundView],
]

const preAuth: Array<[string, Component]> = [
  ['Sign in', SignInView],
  ['Join', JoinView],
]

/**
 * Screens the tab bar cannot reach. A tab is the way back to a top-level screen,
 * but nothing in the bar leads to a group, an expense or a settings page, so
 * without a back button the only way out is the browser's own.
 */
const subScreens: Array<[string, Component, string]> = [
  ['Group settings', GroupSettingsView, 'Roommates'],
  ['New group', NewGroupView, 'Dashboard'],
  ['Expense', ExpenseView, 'Roommates'],
  ['Settle up', SettleView, 'Roommates'],
  ['Import', ImportView, 'Profile'],
  ['Conflicts', ConflictsView, 'Profile'],
  ['Not found', NotFoundView, 'Dashboard'],
]

/**
 * The expense form is left out on purpose. The route mock above hands every view
 * the same params, which puts the form into edit mode, where it does have a back
 * button. Both of its modes are covered in its own spec, where the route can be
 * set per test.
 */
const tabScreens: Array<[string, Component]> = [
  ['Dashboard', DashboardView],
  ['Activity', ActivityView],
  ['Stats', StatsView],
  ['Profile', ProfileView],
]

describe('the back button', () => {
  it.each(subScreens)('is on the %s screen', async (_name, component) => {
    const { wrapper } = await mountView(component, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(wrapper.find('[data-testid="back"]').exists()).toBe(true)
  })

  it.each(subScreens)('says where it goes on the %s screen', async (_name, component, parent) => {
    const { wrapper } = await mountView(component, {
      api: api(),
      expenses: [testExpense()],
    })

    // Named, so the label reads as a destination rather than just "back".
    expect(wrapper.find('[data-testid="back"]').attributes('aria-label')).toBe(`Back to ${parent}`)
  })

  it.each(subScreens)('points somewhere real on the %s screen', async (_name, component) => {
    const { wrapper } = await mountView(component, {
      api: api(),
      expenses: [testExpense()],
    })

    // A link, not history: a screen opened from a notification or a shared URL
    // has nothing to go back to.
    const to = wrapper.findComponent('[data-testid="back"]').props('to') as { name?: string }
    expect(to?.name).toBeTruthy()
  })

  it.each(tabScreens)('is not on the %s screen, which is a tab', async (_name, component) => {
    const { wrapper } = await mountView(component, {
      api: api(),
      expenses: [testExpense()],
    })

    // The tab is already lit; a back button here would compete with it.
    expect(wrapper.find('[data-testid="back"]').exists()).toBe(false)
  })

  it.each(preAuth)('is not on the %s screen', async (_name, component) => {
    const { wrapper } = await mountView(component, { api: api(), signedIn: false })

    expect(wrapper.find('[data-testid="back"]').exists()).toBe(false)
  })
})

describe('the bottom tab bar', () => {
  it.each(inApp)('is on the %s screen', async (_name, component) => {
    const { wrapper } = await mountView(component, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(wrapper.find('nav[aria-label="Main"]').exists()).toBe(true)
  })

  it.each(preAuth)('is not on the %s screen, which is reached before signing in', async (_name, component) => {
    const { wrapper } = await mountView(component, { api: api(), signedIn: false })

    // Every tab would bounce straight back to sign-in.
    expect(wrapper.find('nav[aria-label="Main"]').exists()).toBe(false)
  })

  it.each(inApp)('leaves room for it on the %s screen', async (_name, component) => {
    const { wrapper } = await mountView(component, {
      api: api(),
      expenses: [testExpense()],
    })

    // Without the padding the bar covers the last row of a list.
    expect(wrapper.find('main').classes()).toContain('pb-28')
  })
})
