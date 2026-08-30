import { ref } from 'vue'
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { settleWithin } from '@/startup'

/**
 * How long a navigation waits for a session to come back.
 *
 * Enough for a slow connection to answer in place, not enough to hold a screen
 * blank on a stalled one. Nothing is lost by giving up early: the attempt is
 * shared, so the sign-in page picks up the same one and moves on when it lands.
 */
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

/**
 * Whether a navigation is open.
 *
 * Kept here because the router is the only thing that knows: a screen is fetched
 * on demand and the guard may have to bring a session back first, so the gap
 * between a tap and a new screen is real and needs reporting.
 */
export const isNavigating = ref(false)

router.beforeEach(() => {
  isNavigating.value = true
  return true
})

router.afterEach(() => {
  isNavigating.value = false
})

// A cancelled or failed navigation ends too, and leaving the bar up forever would
// be worse than never showing it.
router.onError(() => {
  isNavigating.value = false
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.public) return true
  if (auth.isSignedIn) return true

  // Asked here rather than only at startup, so a session that ended mid-visit is
  // picked up again in place. A device that already belongs to someone gets
  // itself back in; the sign-in page is for devices that do not.
  const resumed = auth.resumeSession()
  if ((await settleWithin(resumed, RESUME_BUDGET_MS)) === 'finished' && (await resumed)) {
    return true
  }

  // Remember where they were headed, so an invite or a deep link survives the
  // detour through sign-in.
  return { name: 'sign-in', query: { redirect: to.fullPath } }
})
