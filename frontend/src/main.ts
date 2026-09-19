import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router, warmRoutes } from './router'
import { ApiClient } from './api/client'
import { setApiClient } from './api/provider'
import { apiBaseUrl, appVersion } from './api/config'
import { SyncEngine } from './offline/syncEngine'
import { HttpSyncApi } from './api/syncApi'
import { deviceIdNow, getDeviceId, isReplicaResponsive, onDatabaseBlocked } from './offline/db'
import { useAuthStore } from './stores/auth'
import { useGroupsStore } from './stores/groups'
import { useExpensesStore } from './stores/expenses'
import { createRealtimeConnection } from './offline/realtime'
import { watchForInstallPrompt } from './native/install'
import {
  BLOCKED_MESSAGE,
  RENDER_MESSAGE,
  WEDGED_MESSAGE,
  settleWithin,
  showStartupProblem,
} from './startup'
import { setLocale, t } from './i18n'
import './styles/main.css'
import {
  describeVueError,
  installErrorReporting,
  reportClientError,
  watchForUncaughtErrors,
} from './diagnostics'

async function bootstrap(): Promise<void> {
  const app = createApp(App)
  app.use(createPinia())

  const auth = useAuthStore()
  auth.restore()

  setLocale(auth.user?.locale)

  const api = new ApiClient({
    baseUrl: apiBaseUrl(),
    getAccessToken: () => auth.accessToken,
    getDeviceId: () => deviceIdNow(),
    refreshAccessToken: () => auth.refresh(),
    onUnauthorized: () => {
      auth.sessionExpired()
      void router.push({ name: 'sign-in' })
    },
  })

  setApiClient(api)

  installErrorReporting({
    send: (report) => api.post('/diagnostics/client-error', report),
    route: () => String(router.currentRoute.value.name ?? router.currentRoute.value.path),
    deviceId: () => deviceIdNow() ?? undefined,
    appVersion: appVersion(),
  })

  const stopWatchingErrors = watchForUncaughtErrors()
  void stopWatchingErrors

  app.config.errorHandler = (error, _instance, info) => {
    console.error('Vue error', error, info)
    reportClientError(describeVueError(error, info))
    showStartupProblem(t(RENDER_MESSAGE))
  }
  auth.attachApi(api)
  const groupsStore = useGroupsStore()
  groupsStore.attachApi(api)
  groupsStore.restoreMainGroup()

  const expenses = useExpensesStore()
  expenses.attachSync(new SyncEngine(new HttpSyncApi(api)))
  expenses.attachApi(api)

  const blocked = new Promise<'blocked'>((resolve) => {
    onDatabaseBlocked(() => {
      resolve('blocked')
      showStartupProblem(t(BLOCKED_MESSAGE))
    })
  })

  const started = prepare(auth, expenses).catch((error: unknown) => {
    console.error('Startup work failed; showing the app anyway.', error)
    reportClientError({
      kind: 'startup',
      message: error instanceof Error ? error.message : String(error),
      stack: error instanceof Error ? error.stack : undefined,
    })
  })

  const outcome = await Promise.race([settleWithin(started), blocked])

  if (outcome === 'blocked') {
    showStartupProblem(t(BLOCKED_MESSAGE))
    return
  }

  if (outcome === 'timed-out') {
    console.warn('Startup work is still running; showing the app anyway.')
  }

  createRealtimeConnection({
    getAccessToken: () => auth.accessToken,
    onChanged: () => void expenses.sync(),
  })

  app.use(router)
  app.mount('#app')

  void watchReplica()

  void warmRoutes()

  watchForInstallPrompt()
}

async function watchReplica(): Promise<void> {
  if (await isReplicaResponsive()) return

  showStartupProblem(t(WEDGED_MESSAGE))
}

async function prepare(
  auth: ReturnType<typeof useAuthStore>,
  expenses: ReturnType<typeof useExpensesStore>,
): Promise<void> {
  await getDeviceId()

  await auth.resumeSession()

  await expenses.hydrate()

  await expenses.reconcile()
}

void bootstrap().catch((error: unknown) => {
  console.error('Startup failed.', error)
  showStartupProblem(
    error instanceof Error && error.message
      ? error.message
      : 'Something went wrong while starting up.',
  )
})
