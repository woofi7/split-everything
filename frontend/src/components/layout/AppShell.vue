<script setup lang="ts">
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import { computed } from 'vue'
import BottomNav from './BottomNav.vue'
import SyncIndicator from '@/components/ui/SyncIndicator.vue'

/**
 * Defaults are declared rather than inferred: Vue casts an absent Boolean prop to
 * false, so treating "not passed" as "on" by comparing against false hid the tab
 * bar on every screen that did not spell it out.
 */
const props = withDefaults(
  defineProps<{
    title: string
    subtitle?: string
    pendingCount?: number
    rejectedCount?: number
    isOffline?: boolean
    isSyncing?: boolean
    showNav?: boolean
    /**
     * Where this screen goes back to, on the screens no tab can reach. A route
     * rather than history: a screen opened from a notification, a shared link or
     * a reload has no history to go back to, and after a redirect the previous
     * entry is not where the person came from.
     */
    backTo?: RouteLocationRaw
    /** Named, so the control reads as a destination rather than just "back". */
    backLabel?: string
  }>(),
  {
    subtitle: undefined,
    pendingCount: 0,
    rejectedCount: 0,
    isOffline: false,
    isSyncing: false,
    showNav: true,
    backTo: undefined,
    backLabel: 'Back',
  },
)

/**
 * Whether the sync state is worth a line on screen. Nothing to report is the normal
 * case, and saying so on every screen forever is furniture rather than information.
 */
const hasSyncNews = computed(
  () =>
    props.pendingCount > 0 ||
    props.rejectedCount > 0 ||
    props.isOffline ||
    props.isSyncing,
)
</script>

<template>
  <div class="mx-auto flex min-h-screen max-w-2xl flex-col">
    <!--
      No chrome at the top: the page is the page, and the only fixed furniture is
      the tab bar at the bottom. What the header used to hold now scrolls with the
      content, so it takes no room once you are reading.
    -->
    <main
      class="flex-1 px-4 pt-[max(1rem,env(safe-area-inset-top))]"
      :class="props.showNav ? 'pb-28' : 'pb-8'"
    >
      <div class="mb-4 flex items-start justify-between gap-3">
        <div class="flex min-w-0 items-center gap-1">
          <RouterLink
            v-if="props.backTo"
            :to="props.backTo"
            data-testid="back"
            class="tap-target -ml-2 flex shrink-0 items-center text-[var(--text-muted)]"
            :aria-label="`Back to ${props.backLabel}`"
          >
            <svg
              class="h-6 w-6"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <path d="M15 5l-7 7 7 7" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </RouterLink>

          <div class="min-w-0">
            <h1 class="truncate text-xl font-semibold">{{ title }}</h1>
            <p v-if="subtitle" class="truncate text-sm text-[var(--text-muted)]">{{ subtitle }}</p>
          </div>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <slot name="header-action" />
        </div>
      </div>

      <!--
        Only when it has something to say. "All synced" on every screen forever was
        a line of furniture reporting that nothing is wrong.
      -->
      <SyncIndicator
        v-if="hasSyncNews"
        class="mb-4"
        :pending-count="props.pendingCount"
        :rejected-count="props.rejectedCount"
        :is-offline="props.isOffline"
        :is-syncing="props.isSyncing"
      />

      <slot />
    </main>

    <BottomNav v-if="props.showNav" />
  </div>
</template>
