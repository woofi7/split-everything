<script setup lang="ts">
import { computed, onMounted, watchEffect } from 'vue'
import { RouterView } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useExpensesStore } from '@/stores/expenses'
import { useGroupsStore } from '@/stores/groups'
import NavigationProgress from '@/components/ui/NavigationProgress.vue'
import UpdatePrompt from '@/components/ui/UpdatePrompt.vue'
import ToastStack from '@/components/ui/ToastStack.vue'
import { isNavigating } from '@/router'
import { accentVariables, findAccent } from '@/domain/themes'
import { setLocale } from '@/i18n'

const auth = useAuthStore()
const expenses = useExpensesStore()
const groups = useGroupsStore()

const accent = computed(() => findAccent(groups.mainGroup?.themeName) ?? auth.accent)

watchEffect(() => {
  const root = document.documentElement
  root.dataset.theme = auth.theme
  root.dataset.accent = accent.value.name

  setLocale(auth.language)
  root.lang = auth.language

  for (const [token, value] of Object.entries(accentVariables(accent.value))) {
    root.style.setProperty(token, value)
  }

  paintBrowserChrome()
})

function paintBrowserChrome(): void {
  const meta = document.querySelector('meta[name="theme-color"]')
  if (!meta) return

  const surface = getComputedStyle(document.documentElement).getPropertyValue('--surface').trim()
  if (surface) meta.setAttribute('content', surface)
}

onMounted(() => {
  window.addEventListener('online', () => void safeSync())
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') void safeSync()
  })

  if (auth.isSignedIn) void safeSync()
})

async function safeSync(): Promise<void> {
  if (!auth.isSignedIn) return

  try {
    await expenses.sync()
    await groups.loadAll()
  } catch {
  }
}
</script>
<template>
  <NavigationProgress :active="isNavigating" />
  <RouterView />
  <ToastStack />
  <UpdatePrompt />
</template>
