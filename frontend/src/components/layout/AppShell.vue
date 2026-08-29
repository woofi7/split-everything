<script setup lang="ts">
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
  }>(),
  {
    subtitle: undefined,
    pendingCount: 0,
    rejectedCount: 0,
    isOffline: false,
    isSyncing: false,
    showNav: true,
  },
)
</script>

<template>
  <div class="mx-auto flex min-h-screen max-w-2xl flex-col">
    <header
      class="sticky top-0 z-20 border-b bg-[var(--surface)]/95 px-4 pt-[env(safe-area-inset-top)] backdrop-blur"
      style="border-color: var(--border)"
    >
      <div class="flex items-baseline justify-between gap-3 py-3">
        <div class="min-w-0">
          <h1 class="truncate text-lg font-semibold">{{ title }}</h1>
          <p v-if="subtitle" class="truncate text-sm text-[var(--text-muted)]">{{ subtitle }}</p>
        </div>
        <slot name="header-action" />
      </div>

      <SyncIndicator
        class="pb-2"
        :pending-count="props.pendingCount"
        :rejected-count="props.rejectedCount"
        :is-offline="props.isOffline"
        :is-syncing="props.isSyncing"
      />
    </header>

    <!-- Padded so the bottom nav never covers the last row of a list. -->
    <main class="flex-1 px-4 py-4" :class="props.showNav ? 'pb-28' : 'pb-8'">
      <slot />
    </main>

    <BottomNav v-if="props.showNav" />
  </div>
</template>
