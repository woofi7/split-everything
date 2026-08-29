import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { ApiClient } from './api/client'
import { setApiClient } from './api/provider'
import { apiBaseUrl } from './api/config'
import { SyncEngine } from './offline/syncEngine'
import { HttpSyncApi } from './api/syncApi'
import { getDeviceId } from './offline/db'
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

  // Resolved once at start, because it keys every vector clock and must be stable
  // for the life of the install.
  const deviceId = await getDeviceId()

  const api = new ApiClient({
    baseUrl: apiBaseUrl(),
    getAccessToken: () => auth.accessToken,
    getDeviceId: () => deviceId,
    refreshAccessToken: () => auth.refresh(),
    onUnauthorized: () => {
      void auth.signOut()
      void router.push({ name: 'sign-in' })
    },
  })

  // Views resolve the client through the provider rather than building their own.
  setApiClient(api)
  auth.attachApi(api)
  useGroupsStore().attachApi(api)

  const engine = new SyncEngine(new HttpSyncApi(api))
  const expenses = useExpensesStore()
  expenses.attachSync(engine)
  await expenses.hydrate()

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
