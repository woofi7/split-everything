import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { ApiClient } from './api/client'
import { setApiClient } from './api/provider'
import { apiBaseUrl } from './api/config'
import { SyncEngine } from './offline/syncEngine'
import { HttpSyncApi } from './api/syncApi'
import { deviceIdNow, getDeviceId, isReplicaResponsive, onDatabaseBlocked } from './offline/db'
import { useAuthStore } from './stores/auth'
import { useGroupsStore } from './stores/groups'
import { useExpensesStore } from './stores/expenses'
import { createRealtimeConnection } from './offline/realtime'
import {
  BLOCKED_MESSAGE,
  WEDGED_MESSAGE,
  settleWithin,
  showStartupProblem,
} from './startup'
import './styles/main.css'

async function bootstrap(): Promise<void> {
  const app = createApp(App)
  app.use(createPinia())

  const auth = useAuthStore()
  auth.restore()

  const api = new ApiClient({
    baseUrl: apiBaseUrl(),
    getAccessToken: () => auth.accessToken,
    // Read live rather than captured: it is resolved from the replica below, and
    // signing in as a different account mints a new one.
    getDeviceId: () => deviceIdNow(),
    refreshAccessToken: () => auth.refresh(),
    // Not a sign-out: nobody asked for this. Clearing the session and keeping
    // the account means the sign-in page can put the device straight back in,
    // rather than asking someone who has not gone anywhere to identify themselves.
    onUnauthorized: () => {
      auth.sessionExpired()
      void router.push({ name: 'sign-in' })
    },
  })

  // Views resolve the client through the provider rather than building their own.
  setApiClient(api)
  auth.attachApi(api)
  const groupsStore = useGroupsStore()
  groupsStore.attachApi(api)
  // Restored before any screen reads it, so the app opens on the group it was left
  // on rather than flicking to a different one.
  groupsStore.restoreMainGroup()

  const expenses = useExpensesStore()
  expenses.attachSync(new SyncEngine(new HttpSyncApi(api)))

  // A replica another tab is holding at an older schema version is a wait with no
  // end, so it is raced rather than waited out, and the app stops there: every
  // screen reads from that replica, so none of them would work.
  //
  // Listened for throughout, not only during startup. The browser can report this
  // after the app is already up, and an app running over a replica that will never
  // answer is every screen stuck on its own spinner: a stopped clock rather than
  // an error, which is exactly what it looked like.
  const blocked = new Promise<'blocked'>((resolve) => {
    onDatabaseBlocked(() => {
      resolve('blocked')
      showStartupProblem(BLOCKED_MESSAGE)
    })
  })

  const started = prepare(auth, expenses).catch((error: unknown) => {
    // The app is still worth showing: each screen loads its own data.
    console.error('Startup work failed; showing the app anyway.', error)
  })

  const outcome = await Promise.race([settleWithin(started), blocked])

  if (outcome === 'blocked') {
    showStartupProblem(BLOCKED_MESSAGE)
    return
  }

  if (outcome === 'timed-out') {
    // Slow, not broken. The app comes up and each screen loads its own data, so
    // this costs a moment of emptier first render rather than correctness.
    console.warn('Startup work is still running; showing the app anyway.')
  }

  createRealtimeConnection({
    getAccessToken: () => auth.accessToken,
    onChanged: () => void expenses.sync(),
  })

  app.use(router)
  app.mount('#app')

  // Nothing above proves the replica is answering: startup has a budget, so it
  // reaches here either way. Asked once the app is up, because a screen that
  // spins forever needs a reason on it and there is no other way to notice.
  void watchReplica()
}

async function watchReplica(): Promise<void> {
  if (await isReplicaResponsive()) return

  showStartupProblem(WEDGED_MESSAGE)
}

/**
 * Everything worth having before the first render, and nothing that is required
 * for it. Bounded by the caller, and its failures are the caller's to shrug off:
 * a screen that loads its own data is better than no screen.
 */
async function prepare(
  auth: ReturnType<typeof useAuthStore>,
  expenses: ReturnType<typeof useExpensesStore>,
): Promise<void> {
  // Keys every vector clock, so it is resolved before anything can write.
  await getDeviceId()

  // Before the router runs, so the guard sees the session rather than bouncing
  // someone to sign-in while a good session sits in the cookie the app cannot
  // read.
  await auth.resumeSession()

  await expenses.hydrate()

  // Repairs anything a previous session stranded: a change the server refused and
  // nothing retried, or a row left marked unsent with nothing queued for it. Both
  // read as "waiting to sync" forever otherwise.
  await expenses.reconcile()
}

void bootstrap().catch((error: unknown) => {
  // Whatever this was, the alternative to saying so is a white screen.
  console.error('Startup failed.', error)
  showStartupProblem(
    error instanceof Error && error.message
      ? error.message
      : 'Something went wrong while starting up.',
  )
})
