<script setup lang="ts">
import { t } from '@/i18n'
import { onMounted, ref, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { googleClientId } from '@/api/config'
import { loadGoogleIdentity } from '@/native/googleIdentity'
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
  // A device that already belongs to someone is not asked who it is. The router
  // covers the protected screens; this covers arriving here directly, which is
  // what a session ending mid-visit does.
  if (auth.isSignedIn || (await auth.resumeSession())) {
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

  // Google's library, which nothing else in the app needs and nothing was fetching.
  // Bounded inside, so a blocked script leaves this page usable rather than pending.
  await loadGoogleIdentity()

  mountGoogleButton()
})

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
  // The one corner of Google's SDK this app uses, described here because the
  // library ships no types worth depending on.
  const google = (
    window as unknown as {
      google?: {
        accounts: {
          id: {
            initialize: (options: Record<string, unknown>) => void
            renderButton: (target: HTMLElement, options: Record<string, unknown>) => void
            prompt?: () => void
          }
        }
      }
    }
  ).google
  const clientId = googleClientId()

  if (!google?.accounts?.id || !clientId) {
    // Not an error when there is another way in; the page says so below instead.
    if (!capabilities.value?.developmentSignIn) {
      error.value = t('Google sign-in is unavailable. Check your connection and try again.')
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
    error.value = caught instanceof Error ? caught.message : t('Could not sign you in.')
  } finally {
    isSigningIn.value = false
  }
}

async function handleCredential(credential?: string): Promise<void> {
  if (!credential) {
    error.value = t('Google did not return a credential. Try again.')
    return
  }

  isSigningIn.value = true
  error.value = null

  try {
    await auth.signInWithGoogle(credential)
    await router.replace(redirectTarget())
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not sign you in.')
  } finally {
    isSigningIn.value = false
  }
}
</script>

<template>
  <AppShell :title="t('Split Everything')" :show-nav="false">
    <div class="mx-auto flex max-w-sm flex-col items-center gap-6 py-12 text-center">
      <img
        src="/icons/icon.svg"
        alt=""
        width="64"
        height="64"
        class="h-16 w-16 rounded-2xl"
      />

      <!--
        No "welcome back, continue as you" step. Being asked to confirm who you
        are on your own phone is a question the device already has the answer to;
        it reconnects on its own before this page renders, so reaching this page
        means it could not, and the only useful thing here is a way in.
      -->
      <div>
        <h2 class="text-xl font-semibold">{{ t('Shared expenses, settled properly') }}</h2>
        <p class="mt-2 text-sm text-[var(--text-muted)]">{{ t('Sign in with Google to see your groups. There is no password to remember.') }}
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
          <p class="text-sm font-medium">{{ t('Development sign-in') }}</p>
          <p class="text-xs text-[var(--text-muted)]">{{ t('This server has no Google client configured, so it is letting you in with just an address. Use a different one to act as a second person and test sharing. Never enabled in production.') }}
          </p>
        </div>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Email') }}</span>
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
          <span class="text-xs text-[var(--text-muted)]">{{ t('Name') }}</span>
          <input
            v-model="devName"
            type="text"
            :placeholder="t('Alice')"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          />
        </label>

        <button
          type="submit"
          class="btn btn-press btn-primary"
          :disabled="isSigningIn || !devEmail"
        >{{ t('Continue') }}
        </button>
      </form>

      <p v-if="isSigningIn" class="text-sm text-[var(--text-muted)]">{{ t('Signing you in') }}</p>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
    </div>
  </AppShell>
</template>
