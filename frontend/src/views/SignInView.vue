<script setup lang="ts">
import { onMounted, ref, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { googleClientId } from '@/api/config'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const error = ref<string | null>(null)
const isSigningIn = ref(false)

// A template ref rather than a document lookup: two mounted instances would fight
// over the same element id, and a detached component would silently render no
// button at all.
const buttonHost = useTemplateRef<HTMLElement>('buttonHost')



onMounted(() => {
  if (auth.isSignedIn) {
    void router.replace(redirectTarget())
    return
  }

  mountGoogleButton()
})

function redirectTarget(): string {
  const redirect = route.query.redirect
  return typeof redirect === 'string' && redirect.startsWith('/') ? redirect : '/groups'
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
    error.value = 'Google sign-in is unavailable. Check your connection and try again.'
    return
  }

  google.accounts.id.initialize({
    client_id: clientId,
    callback: (response: { credential?: string }) => void handleCredential(response.credential),
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

      <div>
        <h2 class="text-xl font-semibold">Shared expenses, settled properly</h2>
        <p class="mt-2 text-sm text-[var(--text-muted)]">
          Sign in with Google to see your groups. There is no password to remember.
        </p>
      </div>

      <div ref="buttonHost" class="min-h-[44px]" />

      <p v-if="isSigningIn" class="text-sm text-[var(--text-muted)]">Signing you in</p>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
    </div>
  </AppShell>
</template>
