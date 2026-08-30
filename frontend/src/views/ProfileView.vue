<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { useExpensesStore } from '@/stores/expenses'
import { useApi } from '@/api/provider'
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

async function save(): Promise<void> {
  error.value = null
  message.value = null
  isSaving.value = true

  try {
    await auth.updateProfile({
      displayName: displayName.value,
      defaultCurrency: defaultCurrency.value,
    })
    message.value = 'Saved.'
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not save your profile.'
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
    error.value = caught instanceof Error ? caught.message : 'Could not export your data.'
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
    error.value = caught instanceof Error ? caught.message : 'Could not delete your account.'
  }
}
</script>

<template>
  <AppShell
    title="Profile"
    :subtitle="auth.user?.email"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-syncing="expenses.isSyncing"
  >
    <form class="surface-card mb-4 flex flex-col gap-4 p-4" @submit.prevent="save">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Display name</span>
        <input
          v-model="displayName"
          type="text"
          maxlength="120"
          class="tap-target rounded-lg border bg-[var(--surface)] px-3"
          style="border-color: var(--border)"
        />
      </label>

      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">
          Your currency, used for totals across groups
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
      <p class="text-sm">App colour</p>
      <p class="mb-3 text-xs text-[var(--text-muted)]">
        Applies everywhere, and follows your account onto any device you sign in on.
      </p>

      <AccentChoice :value="accent" label="App colour" @pick="pickAccent" />
    </section>

    <section class="surface-card mb-4 flex items-center justify-between p-4">
      <span class="text-sm">Light mode</span>
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
      <RouterLink :to="{ name: 'import' }" class="btn btn-press btn-secondary w-full justify-start">
        Import a Settle Up export or a statement
      </RouterLink>
      <RouterLink :to="{ name: 'conflicts' }" class="btn btn-press btn-secondary w-full justify-start">
        Changes needing attention
      </RouterLink>
    </section>

    <section class="surface-card flex flex-col gap-3 p-4">
      <button
        type="button"
        class="btn btn-press btn-secondary w-full justify-start"
        @click="exportData"
      >
        Download all my data
      </button>

      <!-- Named for what it does to this device, and filled, because as a line of
           bare text it read as a label rather than a button. -->
      <button
        type="button"
        data-testid="disconnect"
        class="btn btn-press btn-secondary w-full justify-start"
        @click="signOut"
      >
        Disconnect this device
      </button>
      <p class="-mt-1 text-xs text-[var(--text-muted)]">
        Signs you out here and stops this device reconnecting on its own, so the
        next start asks for an account. Your data stays on the server.
      </p>

      <button
        v-if="!confirmingDelete"
        type="button"
        class="btn btn-press btn-danger w-full justify-start"
        @click="confirmingDelete = true"
      >
        Delete my account
      </button>

      <div v-else class="flex flex-col gap-2">
        <p class="text-sm text-[var(--text-muted)]">
          Your name stays on past expenses so other people's balances remain correct, but your
          account and sign-in are removed. This cannot be undone.
        </p>
        <div class="flex gap-2">
          <button
            type="button"
            class="btn btn-press btn-secondary flex-1"
            style="border-color: var(--border)"
            @click="confirmingDelete = false"
          >
            Keep my account
          </button>
          <button
            type="button"
            class="btn btn-press btn-danger flex-1"
            @click="deleteAccount"
          >
            Delete it
          </button>
        </div>
      </div>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
    </section>

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
      >
        Cancel
      </button>
      <button
        type="button"
        data-testid="save-settings"
        class="btn btn-press btn-primary shadow-lg"
        :disabled="isSaving"
        @click="save"
      >
        {{ isSaving ? 'Saving' : 'Save changes' }}
      </button>
    </div>
  </AppShell>
</template>
