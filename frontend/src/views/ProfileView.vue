<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { useExpensesStore } from '@/stores/expenses'
import { useApi } from '@/api/provider'
import ColorChoice from '@/components/ui/ColorChoice.vue'

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
 * kept, so one button saves them and one puts them back. The theme is not among
 * them, because it is applied as it is switched and there is nothing to preview.
 */
const isDirty = computed(() => {
  const user = auth.user
  if (!user) return false

  if (displayName.value.trim() !== user.displayName) return true
  if (defaultCurrency.value !== user.defaultCurrency) return true

  return pickedColour.value !== undefined
})

/** Puts the form back to the account, so a change can be abandoned. */
function revert(): void {
  const user = auth.user
  displayName.value = user?.displayName ?? ''
  defaultCurrency.value = user?.defaultCurrency ?? 'CAD'
  pickedColour.value = undefined
  message.value = null
  error.value = null
}

/**
 * The colour this person would like in the groups they join.
 *
 * A wish rather than a setting: a group where somebody already has it gives them
 * the next free one, because two people the same colour in one group defeats the
 * point. Saved on the tap, since there is nothing else to fill in.
 */
const stored = computed(() => auth.user?.preferredColorHex ?? null)

/**
 * Picked but not saved, like the name and the currency beside it.
 *
 * Undefined means untouched, and null means asked to be cleared, which is not the
 * same thing: this screen has a Save, so a colour should wait for it rather than
 * committing on a tap.
 */
const pickedColour = ref<string | null | undefined>(undefined)

const preferredColour = computed(() =>
  pickedColour.value === undefined ? stored.value : pickedColour.value,
)

function pickColour(colorHex: string): void {
  // Tapping the one already showing clears it, which is the only way back to
  // having no preference.
  const next = preferredColour.value?.toLowerCase() === colorHex.toLowerCase() ? null : colorHex
  pickedColour.value = next === stored.value ? undefined : next
}

async function save(): Promise<void> {
  error.value = null
  message.value = null
  isSaving.value = true

  try {
    await auth.updateProfile({
      displayName: displayName.value,
      defaultCurrency: defaultCurrency.value,
      // Only when it was touched. The API reads null as "not supplied" and an
      // empty string as an explicit clear.
      ...(pickedColour.value === undefined
        ? {}
        : { preferredColorHex: pickedColour.value ?? '' }),
    })
    pickedColour.value = undefined
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
      <p class="text-sm">Your colour</p>
      <p class="mb-3 text-xs text-[var(--text-muted)]">
        Used in the groups you join, when nobody there has it already. Tap the one
        you have to go back to no preference.
      </p>

      <ColorChoice
        :value="preferredColour"
        label="Your preferred colour"
        @pick="pickColour"
      />

      <p class="mt-2 text-xs text-[var(--text-muted)]">
        Saved with the rest of your profile, above.
      </p>
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
