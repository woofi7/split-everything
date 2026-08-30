<script setup lang="ts">
import { onMounted, ref, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { googleClientId } from '@/api/config'
import { useApi } from '@/api/provider'

interface AuthCapabilities {
  googleConfigured: boolean
  developmentSignIn: boolean
}

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const error = ref<string | null>(null)
const isSigningIn = ref(false)

// A template ref rather than a document lookup: two mounted instances would fight
// over the same element id, and a detached component would silently render no
// button at all.
const buttonHost = useTemplateRef<HTMLElement>('buttonHost')

const capabilities = ref<AuthCapabilities | null>(null)
const devEmail = ref('')
const devName = ref('')



onMounted(async () => {
  if (auth.isSignedIn) {
    void router.replace(redirectTarget())
    return
  }

  // Asked, not assumed: the page has to tell "not configured yet" apart from
  // "broken", and only the server knows whether the development sign-in is on.
  try {
    capabilities.value = await useApi().get<AuthCapabilities>('/auth/capabilities')
  } catch {
    capabilities.value = null
  }

  mountGoogleButton()
})

/**
 * Signs back in as the account this device belongs to.
 *
 * Only reachable when the development form is available, because that is the only
 * way to sign someone in from a name alone. With Google the account is a hint on
 * its own button: the credential still has to come from Google.
 */
async function continueAsRemembered(): Promise<void> {
  const remembered = auth.rememberedAccount
  if (!remembered) return

  error.value = null
  isSigningIn.value = true

  try {
    await auth.signInAsDeveloper(remembered.email)
    await router.replace(redirectTarget())
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not sign in.'
  } finally {
    isSigningIn.value = false
  }
}

function redirectTarget(): string {
  const redirect = route.query.redirect
  return typeof redirect === 'string' && redirect.startsWith('/') ? redirect : '/dashboard'
}

/**
 * Google Identity Services renders its own button and hands back a credential.
 * Loading it lazily keeps the sign-in page usable (and the error visible) even
 * when the script is blocked or the device is offline.
 */
function mountGoogleButton(): void {
  const google = (window as unknown as { google?: any }).google
  const clientId = googleClientId()

  if (!google?.accounts?.id || !clientId) {
    // Not an error when there is another way in; the page says so below instead.
    if (!capabilities.value?.developmentSignIn) {
      error.value = 'Google sign-in is unavailable. Check your connection and try again.'
    }
    return
  }

  google.accounts.id.initialize({
    client_id: clientId,
    callback: (response: { credential?: string }) => void handleCredential(response.credential),
    // So the chooser opens on the account this device belongs to, rather than on
    // every account signed into the browser.
    login_hint: auth.rememberedAccount?.email,
  })

  if (buttonHost.value) {
    google.accounts.id.renderButton(buttonHost.value, {
      theme: 'filled_black',
      size: 'large',
      shape: 'pill',
      text: 'continue_with',
      width: 280,
    })
  }
}

async function signInAsDeveloper(): Promise<void> {
  isSigningIn.value = true
  error.value = null

  try {
    await auth.signInAsDeveloper(devEmail.value, devName.value)
    await router.replace(redirectTarget())
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not sign you in.'
  } finally {
    isSigningIn.value = false
  }
}

async function handleCredential(credential?: string): Promise<void> {
  if (!credential) {
    error.value = 'Google did not return a credential. Try again.'
    return
  }

  isSigningIn.value = true
  error.value = null

  try {
    await auth.signInWithGoogle(credential)
    await router.replace(redirectTarget())
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not sign you in.'
  } finally {
    isSigningIn.value = false
  }
}
</script>

<template>
  <AppShell title="Split Everything" :show-nav="false">
    <div class="mx-auto flex max-w-sm flex-col items-center gap-6 py-12 text-center">
      <div
        class="flex h-16 w-16 items-center justify-center rounded-2xl bg-brand-600 text-2xl font-semibold text-white"
        aria-hidden="true"
      >
        SE
      </div>

      <div v-if="auth.rememberedAccount" class="w-full">
        <h2 class="text-xl font-semibold">Welcome back</h2>
        <p class="mt-2 text-sm text-[var(--text-muted)]">
          This device belongs to
          <span class="text-[var(--text)]">{{ auth.rememberedAccount.displayName }}</span>
          ({{ auth.rememberedAccount.email }}).
        </p>

        <button
          v-if="capabilities?.developmentSignIn"
          type="button"
          data-testid="continue-as"
          class="btn btn-press btn-primary mt-4 w-full"
          :disabled="isSigningIn"
          @click="continueAsRemembered"
        >
          Continue as {{ auth.rememberedAccount.displayName }}
        </button>

        <button
          type="button"
          data-testid="forget-device"
          class="tap-target mt-2 w-full text-xs text-[var(--text-muted)] underline"
          @click="auth.forgetDevice()"
        >
          Use a different account
        </button>
      </div>

      <div v-else>
        <h2 class="text-xl font-semibold">Shared expenses, settled properly</h2>
        <p class="mt-2 text-sm text-[var(--text-muted)]">
          Sign in with Google to see your groups. There is no password to remember.
        </p>
      </div>

      <div ref="buttonHost" class="min-h-[44px]" />

      <form
        v-if="capabilities?.developmentSignIn"
        class="flex w-full flex-col gap-3 border-t pt-6 text-left"
        style="border-color: var(--border)"
        @submit.prevent="signInAsDeveloper"
      >
        <div>
          <p class="text-sm font-medium">Development sign-in</p>
          <p class="text-xs text-[var(--text-muted)]">
            This server has no Google client configured, so it is letting you in with
            just an address. Use a different one to act as a second person and test
            sharing. Never enabled in production.
          </p>
        </div>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">Email</span>
          <input
            v-model="devEmail"
            type="email"
            required
            placeholder="alice@example.com"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          />
        </label>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">Name</span>
          <input
            v-model="devName"
            type="text"
            placeholder="Alice"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          />
        </label>

        <button
          type="submit"
          class="btn btn-press btn-primary"
          :disabled="isSigningIn || !devEmail"
        >
          Continue
        </button>
      </form>

      <p v-if="isSigningIn" class="text-sm text-[var(--text-muted)]">Signing you in</p>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
    </div>
  </AppShell>
</template>
