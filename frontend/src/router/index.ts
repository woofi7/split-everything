import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: { name: 'dashboard' } },
  {
    path: '/sign-in',
    name: 'sign-in',
    component: () => import('@/views/SignInView.vue'),
    meta: { public: true },
  },
  {
    // The magic-link landing page. Public, because the whole point is that
    // someone who is not signed in yet can open it and see what they are joining.
    path: '/join/:token',
    name: 'join',
    component: () => import('@/views/JoinView.vue'),
    meta: { public: true },
  },
  { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
  // Anything already pointing at the old path still lands somewhere.
  { path: '/groups', redirect: { name: 'dashboard' } },
  { path: '/groups/new', name: 'new-group', component: () => import('@/views/NewGroupView.vue') },
  {
    // The same screen as the dashboard, deliberately. There is one group view, and
    // opening a group by its URL makes that group the one the app is on, which is
    // what every other screen then follows.
    path: '/groups/:groupId',
    name: 'group',
    component: () => import('@/views/DashboardView.vue'),
  },
  {
    path: '/groups/:groupId/settings',
    name: 'group-settings',
    component: () => import('@/views/GroupSettingsView.vue'),
  },
  {
    path: '/groups/:groupId/settle',
    name: 'settle',
    component: () => import('@/views/SettleView.vue'),
  },
  {
    path: '/groups/:groupId/expenses/:expenseId',
    name: 'expense',
    component: () => import('@/views/ExpenseView.vue'),
  },
  { path: '/add', name: 'add-expense', component: () => import('@/views/ExpenseFormView.vue') },
  {
    // The same form as adding, deliberately. Editing an expense asks exactly the
    // same questions, and a second copy of the split logic would drift from this
    // one the first time either changed.
    path: '/groups/:groupId/expenses/:expenseId/edit',
    name: 'edit-expense',
    component: () => import('@/views/ExpenseFormView.vue'),
  },
  { path: '/activity', name: 'activity', component: () => import('@/views/ActivityView.vue') },
  { path: '/stats', name: 'stats', component: () => import('@/views/StatsView.vue') },
  { path: '/import', name: 'import', component: () => import('@/views/ImportView.vue') },
  { path: '/conflicts', name: 'conflicts', component: () => import('@/views/ConflictsView.vue') },
  { path: '/profile', name: 'profile', component: () => import('@/views/ProfileView.vue') },
  { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/NotFoundView.vue') },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.public) return true
  if (auth.isSignedIn) return true

  // Asked here rather than only at startup, so a session that ended mid-visit is
  // picked up again in place. A device that already belongs to someone gets
  // itself back in; the sign-in page is for devices that do not.
  if (await auth.resumeSession()) return true

  // Remember where they were headed, so an invite or a deep link survives the
  // detour through sign-in.
  return { name: 'sign-in', query: { redirect: to.fullPath } }
})
