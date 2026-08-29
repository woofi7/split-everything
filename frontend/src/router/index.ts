import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: { name: 'groups' } },
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
  { path: '/groups', name: 'groups', component: () => import('@/views/GroupsView.vue') },
  { path: '/groups/new', name: 'new-group', component: () => import('@/views/NewGroupView.vue') },
  { path: '/groups/:groupId', name: 'group', component: () => import('@/views/GroupView.vue') },
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
  { path: '/add', name: 'add-expense', component: () => import('@/views/AddExpenseView.vue') },
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

router.beforeEach((to) => {
  const auth = useAuthStore()

  if (to.meta.public) return true
  if (auth.isSignedIn) return true

  // Remember where they were headed, so an invite or a deep link survives the
  // detour through sign-in.
  return { name: 'sign-in', query: { redirect: to.fullPath } }
})
