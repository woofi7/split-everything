<script setup lang="ts">
import { onMounted, watchEffect } from 'vue'
import { RouterView } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useExpensesStore } from '@/stores/expenses'
import { useGroupsStore } from '@/stores/groups'

const auth = useAuthStore()
const expenses = useExpensesStore()
const groups = useGroupsStore()

// The theme is an attribute on the root element, so CSS tokens swap without any
// component knowing which theme is active.
watchEffect(() => {
  document.documentElement.dataset.theme = auth.theme
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
  try {
    await expenses.sync()
    await groups.loadAll()
  } catch {
    // Sync failures are expected and already surfaced by the indicator.
  }
}
</script>

<template>
  <RouterView />
</template>
