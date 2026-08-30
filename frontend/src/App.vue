<script setup lang="ts">
import { onMounted, watchEffect } from 'vue'
import { RouterView } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useExpensesStore } from '@/stores/expenses'
import { useGroupsStore } from '@/stores/groups'
import NavigationProgress from '@/components/ui/NavigationProgress.vue'
import { isNavigating } from '@/router'
import { accentVariables } from '@/domain/themes'

const auth = useAuthStore()
const expenses = useExpensesStore()
const groups = useGroupsStore()

// The theme is an attribute on the root element, so CSS tokens swap without any
// component knowing which theme is active. The accent is the same idea by another
// route: the brand tokens themselves, set on the root, because every utility built
// from them reads them through var() and follows without being told.
watchEffect(() => {
  const root = document.documentElement
  root.dataset.theme = auth.theme
  root.dataset.accent = auth.accent.name

  for (const [token, value] of Object.entries(accentVariables(auth.accent))) {
    root.style.setProperty(token, value)
  }
})

onMounted(() => {
  // Coming back online is the moment the queue should drain, and it is also when
  // another device's changes are waiting to be pulled.
  window.addEventListener('online', () => void safeSync())
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') void safeSync()
  })

  if (auth.isSignedIn) void safeSync()
})

async function safeSync(): Promise<void> {
  // The listeners fire whether or not anyone is signed in, and coming back to a
  // sign-in page is exactly one of the moments they fire on.
  if (!auth.isSignedIn) return

  try {
    await expenses.sync()
    await groups.loadAll()
  } catch {
    // Sync failures are expected and already surfaced by the indicator.
  }
}
</script>

<template>
  <NavigationProgress :active="isNavigating" />
  <RouterView />
</template>
