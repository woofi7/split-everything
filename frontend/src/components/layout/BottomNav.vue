<script setup lang="ts">
import { RouterLink } from 'vue-router'

/**
 * The mobile-first bottom tab bar from the spec: Groups, Activity, a centre
 * add-expense button, and Profile. The centre action is deliberately the largest
 * target, because adding an expense is the thing people open this app to do.
 */
const tabs = [
  { name: 'groups', label: 'Groups', to: { name: 'groups' }, icon: 'M4 6h16M4 12h16M4 18h10' },
  { name: 'activity', label: 'Activity', to: { name: 'activity' }, icon: 'M12 8v4l3 2M3 12a9 9 0 1 0 18 0a9 9 0 0 0-18 0' },
  { name: 'stats', label: 'Stats', to: { name: 'stats' }, icon: 'M4 20V10M10 20V4M16 20v-7M22 20H2' },
  { name: 'profile', label: 'Profile', to: { name: 'profile' }, icon: 'M5 20a7 7 0 0 1 14 0M12 3a4 4 0 1 1 0 8a4 4 0 0 1 0-8' },
]
</script>

<template>
  <nav
    class="fixed inset-x-0 bottom-0 z-30 border-t bg-[var(--surface-raised)] pb-[env(safe-area-inset-bottom)]"
    style="border-color: var(--border)"
    aria-label="Main"
  >
    <ul class="mx-auto grid max-w-2xl grid-cols-5 items-end">
      <li v-for="tab in tabs.slice(0, 2)" :key="tab.name" class="contents">
        <RouterLink
          :to="tab.to"
          class="tap-target flex flex-col items-center gap-1 py-2 text-xs text-[var(--text-muted)]"
          active-class="text-brand-400"
        >
          <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          {{ tab.label }}
        </RouterLink>
      </li>

      <li class="flex justify-center">
        <RouterLink
          :to="{ name: 'add-expense' }"
          class="tap-target -mt-5 flex h-14 w-14 items-center justify-center rounded-full bg-brand-600 text-white shadow-lg shadow-brand-600/30"
          aria-label="Add an expense"
        >
          <svg class="h-6 w-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
            <path d="M12 5v14M5 12h14" stroke-linecap="round" />
          </svg>
        </RouterLink>
      </li>

      <li v-for="tab in tabs.slice(2)" :key="tab.name" class="contents">
        <RouterLink
          :to="tab.to"
          class="tap-target flex flex-col items-center gap-1 py-2 text-xs text-[var(--text-muted)]"
          active-class="text-brand-400"
        >
          <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          {{ tab.label }}
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>
