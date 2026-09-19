import { ref } from 'vue'
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { SCREEN_MESSAGE, settleWithin, showStartupProblem } from '@/startup'
import { reportClientError } from '@/diagnostics'

const RESUME_BUDGET_MS = 4000

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: { name: 'dashboard' } },
  {
    path: '/sign-in',
    name: 'sign-in',
    component: () => import('@/views/SignInView.vue'),
    meta: { public: true },
  },
  {
    path: '/join/:token',
    name: 'join',
    component: () => import('@/views/JoinView.vue'),
    meta: { public: true },
  },
  { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
  { path: '/groups', redirect: { name: 'dashboard' } },
  { path: '/groups/new', name: 'new-group', component: () => import('@/views/NewGroupView.vue') },
  {
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
    path: '/groups/:groupId/file-expenses',
    name: 'file-expenses',
    component: () => import('@/views/FileExpensesView.vue'),
  },
  {
    path: '/groups/:groupId/expenses/:expenseId',
    name: 'expense',
    component: () => import('@/views/ExpenseView.vue'),
  },
  { path: '/add', name: 'add-expense', component: () => import('@/views/ExpenseFormView.vue') },
  {
    path: '/add/payment',
    name: 'add-payment',
    component: () => import('@/views/PaymentFormView.vue'),
  },
  {
    path: '/groups/:groupId/expenses/:expenseId/edit',
    name: 'edit-expense',
    component: () => import('@/views/ExpenseFormView.vue'),
  },
  { path: '/activity', name: 'activity', component: () => import('@/views/ActivityView.vue') },
  { path: '/stats', name: 'stats', component: () => import('@/views/StatsView.vue') },
  { path: '/import', name: 'import', component: () => import('@/views/ImportView.vue') },
  { path: '/conflicts', name: 'conflicts', component: () => import('@/views/ConflictsView.vue') },
  { path: '/profile', name: 'profile', component: () => import('@/views/ProfileView.vue') },
  {
    path: '/admin',
    name: 'admin',
    component: () => import('@/views/AdminView.vue'),
  },
  {
    path: '/profile/settings',
    name: 'profile-settings',
    component: () => import('@/views/ProfileSettingsView.vue'),
  },
  { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/NotFoundView.vue') },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

export async function warmRoutes(): Promise<void> {
  const screens = routes
    .map((route) => route.component)
    .filter((component): component is () => Promise<unknown> => typeof component === 'function')

  for (const load of screens) {
    try {
      await load()
    } catch {
    }
  }
}

export const isNavigating = ref(false)

router.beforeEach(() => {
  isNavigating.value = true
  return true
})

router.afterEach(() => {
  isNavigating.value = false
})

router.onError((error) => {
  isNavigating.value = false

  const message = error instanceof Error ? error.message : String(error)
  const missingCode = /dynamically imported module|Importing a module script failed|Failed to fetch/i.test(message)

  console.error('Navigation failed.', error)
  reportClientError({
    kind: 'navigation',
    message,
    stack: error instanceof Error ? error.stack : undefined,
  })

  if (missingCode) showStartupProblem(SCREEN_MESSAGE)
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.public) return true
  if (auth.isSignedIn) return true

  const resumed = auth.resumeSession()
  if ((await settleWithin(resumed, RESUME_BUDGET_MS)) === 'finished' && (await resumed)) {
    return true
  }

  return { name: 'sign-in', query: { redirect: to.fullPath } }
})
