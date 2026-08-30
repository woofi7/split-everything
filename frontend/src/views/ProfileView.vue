<script setup lang="ts">
import { LOCALES, t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { useExpensesStore } from '@/stores/expenses'
import { useApi } from '@/api/provider'
import { appVersion } from '@/api/config'
import { getDeviceId } from '@/offline/db'
import {
  pushState,
  registerForPush,
  unregisterPush,
  type PushOutcome,
  type PushState,
} from '@/native/push'
import {
  canBeInstalled,
  canInstall,
  install,
  installsByHand,
  isInstalled,
} from '@/native/install'
import AccentChoice from '@/components/ui/AccentChoice.vue'

const auth = useAuthStore()
const expenses = useExpensesStore()
const router = useRouter()

const displayName = ref(auth.user?.displayName ?? '')
const defaultCurrency = ref(auth.user?.defaultCurrency ?? 'CAD')
const message = ref<string | null>(null)
const error = ref<string | null>(null)
const confirmingDelete = ref(false)

const currencies = ['CAD', 'USD', 'EUR', 'GBP', 'CHF', 'AUD', 'JPY']


const isLight = computed(() => auth.theme === 'light')
const isSaving = ref(false)

/**
 * Whether anything here differs from the account as it stands.
 *
 * The same shape as the group's settings: these are settings, edited and then
 * kept, so one button saves them and one puts them back. Neither the accent nor
 * the light switch is among them, because both are applied as they are chosen -
 * the whole application changes, and there is nothing left to preview.
 */
const isDirty = computed(() => {
  const user = auth.user
  if (!user) return false

  if (displayName.value.trim() !== user.displayName) return true
  return defaultCurrency.value !== user.defaultCurrency
})

/** Puts the form back to the account, so a change can be abandoned. */
function revert(): void {
  const user = auth.user
  displayName.value = user?.displayName ?? ''
  defaultCurrency.value = user?.defaultCurrency ?? 'CAD'
  message.value = null
  error.value = null
}

/** The accent the app is wearing, which is an account setting like the rest. */
const accent = computed(() => auth.accent.name)

async function pickAccent(name: string): Promise<void> {
  error.value = null
  await auth.setAccent(name)
}

/**
 * Notifications on this device.
 *
 * Asked rather than assumed: the browser owns the permission and the subscription,
 * so the only honest answer comes from looking. Read again after every change,
 * because a permission dialog can be dismissed and the switch has to reflect that.
 */
const notifications = ref<PushState>('off')
const isTogglingPush = ref(false)

async function readNotifications(): Promise<void> {
  notifications.value = await pushState()
}

onMounted(readNotifications)

async function toggleNotifications(): Promise<void> {
  error.value = null
  isTogglingPush.value = true

  try {
    if (notifications.value === 'on') {
      await unregisterPush(useApi())
    } else {
      const deviceId = await getDeviceId()
      const outcome = await registerForPush(useApi(), deviceId)
      if (outcome !== 'on') error.value = whyNotNotifications(outcome)
    }
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not change notifications.')
  } finally {
    await readNotifications()
    isTogglingPush.value = false
  }
}

/**
 * Why the switch did not stay on.
 *
 * Each of these needs a different person to do something about it, which is the
 * whole reason they are told apart: 'denied' is the browser's site settings,
 * 'unconfigured' is whoever runs the server, and 'failed' is worth another tap.
 */
function whyNotNotifications(outcome: PushOutcome): string {
  if (outcome === 'denied') return t('Notifications were not allowed.')
  if (outcome === 'unsupported') return t('This browser cannot do notifications.')
  if (outcome === 'unconfigured') {
    return t(
      'This server has no notification keys yet, so it cannot send any. Whoever runs it has to add them.',
    )
  }
  return t('Could not turn notifications on. Try again.')
}

/**
 * Which build is running, on this device and on the server.
 *
 * The app's own version is baked in at build time. The server's is asked for, and
 * left null when it cannot be reached: offline is not a version mismatch, and a
 * dash where a number belongs would read like one.
 */
const version = appVersion()
const serverVersion = ref<string | null>(null)

onMounted(async () => {
  try {
    const health = await useApi().get<{ version?: string }>('/health')
    serverVersion.value = health.version ?? null
  } catch {
    serverVersion.value = null
  }
})

/** Installing it, which the browser either offers or cannot do at all. */
const installed = ref(isInstalled())
const installable = ref(canBeInstalled())
const installByHand = ref(installsByHand())

async function installApp(): Promise<void> {
  const outcome = await install()
  if (outcome === 'accepted') installed.value = true
}

/** The language it is read in, which is the same kind of setting. */
const language = computed(() => auth.language)

async function pickLanguage(tag: string): Promise<void> {
  error.value = null
  await auth.setLanguage(tag)
}

async function save(): Promise<void> {
  error.value = null
  message.value = null
  isSaving.value = true

  try {
    await auth.updateProfile({
      displayName: displayName.value,
      defaultCurrency: defaultCurrency.value,
    })
    message.value = t('Saved.')
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not save your profile.')
  } finally {
    isSaving.value = false
  }
}

async function exportData(): Promise<void> {
  error.value = null

  try {
    const blob = await useApi().blob('/auth/me/export')
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `split-everything-export-${new Date().toISOString().slice(0, 10)}.json`
    link.click()
    URL.revokeObjectURL(url)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not export your data.')
  }
}

async function signOut(): Promise<void> {
  await auth.signOut()
  await router.replace({ name: 'sign-in' })
}

async function deleteAccount(): Promise<void> {
  error.value = null

  try {
    await auth.deleteAccount()
    await router.replace({ name: 'sign-in' })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not delete your account.')
  }
}
</script>

<template>
  <AppShell
    :title="t('Profile')"
    :subtitle="auth.user?.email"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-syncing="expenses.isSyncing"
  >
    <form class="surface-card mb-4 flex flex-col gap-4 p-4" @submit.prevent="save">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Display name') }}</span>
        <input
          v-model="displayName"
          type="text"
          maxlength="120"
          class="tap-target rounded-lg border bg-[var(--surface)] px-3"
          style="border-color: var(--border)"
        />
      </label>

      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Your currency, used for totals across groups') }}
        </span>
        <select
          v-model="defaultCurrency"
          class="tap-target rounded-lg border bg-[var(--surface)] px-3"
          style="border-color: var(--border)"
        >
          <option v-for="code in currencies" :key="code" :value="code">{{ code }}</option>
        </select>
      </label>

      <!-- Enter still saves; the buttons that do it are at the foot of the screen,
           where they can speak for the colour below as well as these fields. -->
      <button type="submit" class="hidden" aria-hidden="true" tabindex="-1" />

      <p v-if="message" class="text-sm text-owed" role="status">{{ message }}</p>
    </form>

    <section class="surface-card mb-4 p-4">
      <p class="text-sm">{{ t('App colour') }}</p>
      <p class="mb-3 text-xs text-[var(--text-muted)]">{{ t('Applies everywhere, and follows your account onto any device you sign in on.') }}
      </p>

      <AccentChoice :value="accent" :label="t('App colour')" @pick="pickAccent" />
    </section>

    <section class="surface-card mb-4 p-4">
      <p class="text-sm">{{ t('Language') }}</p>
      <p class="mb-3 text-xs text-[var(--text-muted)]">
        {{ t('Applies to the whole app, and follows your account.') }}
      </p>

      <!--
        Each language named in itself, which is how somebody looking for it reads,
        and applied on the tap like the colour: the screen is the confirmation.
      -->
      <div class="flex gap-2">
        <button
          v-for="choice in LOCALES"
          :key="choice.tag"
          type="button"
          :data-testid="`language-${choice.tag}`"
          class="btn btn-press flex-1"
          :class="language === choice.tag ? 'btn-primary' : 'btn-secondary'"
          :aria-pressed="language === choice.tag"
          @click="pickLanguage(choice.tag)"
        >
          {{ choice.label }}
        </button>
      </div>
    </section>

    <!--
      Notifications, which needed a way in: the registration existed and nothing
      ever called it. What it says depends on why it cannot be offered, because
      those have different answers - a plain-HTTP address needs the app served
      properly, a refused permission needs the browser's own settings.
    -->
    <section class="surface-card mb-4 p-4">
      <div class="flex items-center justify-between gap-2">
        <span class="text-sm">{{ t('Notifications on this device') }}</span>

        <button
          v-if="notifications === 'on' || notifications === 'off'"
          type="button"
          data-testid="notifications-toggle"
          class="btn btn-press btn-secondary min-h-0 rounded-full px-3 py-1 text-sm"
          style="border-color: var(--border)"
          :aria-pressed="notifications === 'on'"
          :disabled="isTogglingPush"
          @click="toggleNotifications"
        >
          {{ isTogglingPush ? t('Working') : notifications === 'on' ? t('On') : t('Off') }}
        </button>
      </div>

      <p
        v-if="notifications !== 'on'"
        data-testid="notifications-note"
        class="mt-2 text-xs text-[var(--text-muted)]"
      >
        <template v-if="notifications === 'insecure'">
          {{ t('Notifications need the app served over https. On a plain address like a local network one, the browser turns them off entirely.') }}
        </template>
        <template v-else-if="notifications === 'denied'">
          {{ t('This browser is blocking notifications for this site. Allow them in its site settings, then come back.') }}
        </template>
        <template v-else-if="notifications === 'unsupported'">
          {{ t('This browser cannot do notifications.') }}
        </template>
        <template v-else>
          {{ t('Told about a new expense, a settlement or a comment while the app is closed.') }}
        </template>
      </p>
    </section>

    <!--
      Installing it, so it opens like an application rather than a page: no browser
      chrome, its own icon, and its own place in the app switcher. Chrome offers
      this itself, iOS does not offer it at all and has to be told what to tap.
    -->
    <section v-if="!installed" class="surface-card mb-4 p-4">
      <p class="text-sm">{{ t('Install on this device') }}</p>

      <p class="mt-1 mb-3 text-xs text-[var(--text-muted)]">
        <template v-if="installByHand">
          {{ t('In Safari: Share, then Add to Home Screen. It then opens like an app, and notifications become possible.') }}
        </template>
        <template v-else-if="!installable">
          {{ t('Installing needs the app served over https. A plain address like a local network one cannot be installed.') }}
        </template>
        <template v-else>
          {{ t('Opens without browser chrome, keeps its own icon, and works offline.') }}
        </template>
      </p>

      <button
        v-if="canInstall"
        type="button"
        data-testid="install-app"
        class="btn btn-press btn-primary w-full"
        @click="installApp"
      >
        {{ t('Install') }}
      </button>
    </section>

    <section class="surface-card mb-4 flex items-center justify-between p-4">
      <span class="text-sm">{{ t('Light mode') }}</span>
      <button
        type="button"
        class="btn btn-press btn-secondary min-h-0 rounded-full px-3 py-1 text-sm"
        style="border-color: var(--border)"
        data-testid="theme-toggle"
        :aria-pressed="isLight"
        @click="auth.setTheme(isLight ? 'dark' : 'light')"
      >
        {{ isLight ? 'On' : 'Off' }}
      </button>
    </section>

    <section class="surface-card mb-4 flex flex-col gap-3 p-4">
      <RouterLink :to="{ name: 'import' }" class="btn btn-press btn-secondary w-full justify-start">{{ t('Import a Settle Up export or a statement') }}
      </RouterLink>
      <RouterLink :to="{ name: 'conflicts' }" class="btn btn-press btn-secondary w-full justify-start">{{ t('Changes needing attention') }}
      </RouterLink>
    </section>

    <section class="surface-card flex flex-col gap-3 p-4">
      <button
        type="button"
        class="btn btn-press btn-secondary w-full justify-start"
        @click="exportData"
      >{{ t('Download all my data') }}
      </button>

      <!-- Named for what it does to this device, and filled, because as a line of
           bare text it read as a label rather than a button. -->
      <button
        type="button"
        data-testid="disconnect"
        class="btn btn-press btn-secondary w-full justify-start"
        @click="signOut"
      >{{ t('Disconnect this device') }}
      </button>
      <p class="-mt-1 text-xs text-[var(--text-muted)]">{{ t('Signs you out here and stops this device reconnecting on its own, so the next start asks for an account. Your data stays on the server.') }}
      </p>

      <button
        v-if="!confirmingDelete"
        type="button"
        class="btn btn-press btn-danger w-full justify-start"
        @click="confirmingDelete = true"
      >{{ t('Delete my account') }}
      </button>

      <div v-else class="flex flex-col gap-2">
        <p class="text-sm text-[var(--text-muted)]">{{ t("Your name stays on past expenses so other people's balances remain correct, but your account and sign-in are removed. This cannot be undone.") }}
        </p>
        <div class="flex gap-2">
          <button
            type="button"
            class="btn btn-press btn-secondary flex-1"
            style="border-color: var(--border)"
            @click="confirmingDelete = false"
          >{{ t('Keep my account') }}
          </button>
          <button
            type="button"
            class="btn btn-press btn-danger flex-1"
            @click="deleteAccount"
          >{{ t('Delete it') }}
          </button>
        </div>
      </div>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
    </section>

    <!--
      Which build this is, at the foot of the page where an about line belongs.

      Both halves, because they can differ: the images are built and deployed as a
      pair but nothing forces them to arrive together, and a new app against an old
      server is a specific kind of confusing that this makes obvious. When they
      match, it reads as one version.
    -->
    <p data-testid="app-version" class="pb-2 text-center text-xs text-[var(--text-muted)]">
      {{ serverVersion && serverVersion !== version
        ? t('Version {app} (server {server})', { app: version, server: serverVersion })
        : t('Version {version}', { version }) }}
    </p>

    <!--
      One save for the settings on this screen, and one way back. In the corner
      and only once something differs, the same as the group's settings: the fields
      it covers are spread down the page, and a button that has scrolled away
      cannot be the answer to "I changed something".
    -->
    <div
      v-if="isDirty"
      data-testid="save-bar"
      class="fixed right-4 z-40 flex gap-2"
      style="bottom: calc(6rem + env(safe-area-inset-bottom))"
    >
      <button
        type="button"
        data-testid="cancel-changes"
        class="btn btn-press btn-secondary shadow-lg"
        style="border-color: var(--border)"
        :disabled="isSaving"
        @click="revert"
      >{{ t('Cancel') }}
      </button>
      <button
        type="button"
        data-testid="save-settings"
        class="btn btn-press btn-primary shadow-lg"
        :disabled="isSaving"
        @click="save"
      >
        {{ isSaving ? t('Saving') : t('Save changes') }}
      </button>
    </div>
  </AppShell>
</template>
