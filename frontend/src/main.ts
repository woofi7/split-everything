import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { ApiClient } from './api/client'
import { setApiClient } from './api/provider'
import { apiBaseUrl } from './api/config'
import { SyncEngine } from './offline/syncEngine'
import { HttpSyncApi } from './api/syncApi'
import { deviceIdNow, getDeviceId } from './offline/db'
import { useAuthStore } from './stores/auth'
import { useGroupsStore } from './stores/groups'
import { useExpensesStore } from './stores/expenses'
import { createRealtimeConnection } from './offline/realtime'
import './styles/main.css'

async function bootstrap(): Promise<void> {
  const app = createApp(App)
  app.use(createPinia())

  const auth = useAuthStore()
  auth.restore()

  // Resolved at start because it keys every vector clock. Read live rather than
  // captured, since signing in as a different account mints a new one.
  await getDeviceId()

  const api = new ApiClient({
    baseUrl: apiBaseUrl(),
    getAccessToken: () => auth.accessToken,
    getDeviceId: () => deviceIdNow(),
    refreshAccessToken: () => auth.refresh(),
    onUnauthorized: () => {
      void auth.signOut()
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

  // Before the router runs, so the guard sees the session rather than bouncing
  // someone to sign-in while a good session sits in the cookie the app cannot
  // read. Costs one request, and only when nothing was stored locally.
  await auth.resumeSession()

  const engine = new SyncEngine(new HttpSyncApi(api))
  const expenses = useExpensesStore()
  expenses.attachSync(engine)
  await expenses.hydrate()

  // Repairs anything a previous session stranded: a change the server refused and
  // nothing retried, or a row left marked unsent with nothing queued for it. Both
  // read as "waiting to sync" forever otherwise.
  await expenses.reconcile()

  // Live sync when connected; the delta pull covers everything a dropped
  // connection missed, so this is an optimisation rather than a requirement.
  createRealtimeConnection({
    getAccessToken: () => auth.accessToken,
    onChanged: () => void expenses.sync(),
  })

  app.use(router)
  app.mount('#app')
}

void bootstrap()
