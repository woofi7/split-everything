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

/**
 * The accent the app is wearing.
 *
 * The group's, where the group the app is on has one: a colour set on a group is
 * set for everyone in it, and it is worn by the whole screen - the background and
 * every card - rather than shown as a dot beside the name, so the group you are
 * looking at is obvious before a word of it is read. Otherwise the account
 * setting, which is what a person chose for themselves and the only thing left to
 * follow on a screen that is about no group at all.
 */
const accent = computed(() => findAccent(groups.mainGroup?.themeName) ?? auth.accent)

// The theme is an attribute on the root element, so CSS tokens swap without any
// component knowing which theme is active. The accent is the same idea by another
// route: the brand tokens themselves, set on the root, because every utility built
// from them reads them through var() and follows without being told.
watchEffect(() => {
  const root = document.documentElement
  root.dataset.theme = auth.theme
  root.dataset.accent = accent.value.name

  // The language too, which also belongs on the element: a screen reader and a
  // spell checker both read lang, and nothing else in the app would tell them.
  setLocale(auth.language)
  root.lang = auth.language

  for (const [token, value] of Object.entries(accentVariables(accent.value))) {
    root.style.setProperty(token, value)
  }

  paintBrowserChrome()
})

/**
 * Tells the browser what colour the page is.
 *
 * The phone paints its own bars around the app - the status bar above, the gesture
 * area below - from this, and it is a single colour declared in the document head,
 * so it knew nothing of the accent and stayed the old slate whatever the app was
 * wearing. Now that a group can turn the whole screen indigo or rose, a strip of
 * unrelated navy at the top of it is the first thing you see.
 *
 * Read back off the element rather than worked out here, because the surfaces are
 * the stylesheet's business: it derives them from the brand token in a way this has
 * no need to know.
 */
function paintBrowserChrome(): void {
  const meta = document.querySelector('meta[name="theme-color"]')
  if (!meta) return

  const surface = getComputedStyle(document.documentElement).getPropertyValue('--surface').trim()
  if (surface) meta.setAttribute('content', surface)
}

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
  <!-- What the app has to say, over whatever screen is up: a failure is news, and
       news belongs where the reader is looking rather than at the foot of a form. -->
  <ToastStack />
  <!-- A new build waits for a page to be closed, and on a phone that never
       happens. This asks instead. -->
  <UpdatePrompt />
</template>
