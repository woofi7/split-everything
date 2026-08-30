<script setup lang="ts">
import { RouterLink } from 'vue-router'

/**
 * The mobile-first bottom tab bar from the spec: Dashboard, Activity, a centre
 * add-expense button, and Profile. The centre action is deliberately the largest
 * target, because adding an expense is the thing people open this app to do.
 *
 * The tab you are on is lifted into a circle that stands proud of the bar, the
 * same shape as the centre button. Colour alone was easy to miss on a phone in
 * daylight, and the raised disc reads as position rather than decoration.
 */
const tabs = [
  { name: 'dashboard', label: 'Dashboard', to: { name: 'dashboard' }, icon: 'M4 6h16M4 12h16M4 18h10' },
  { name: 'activity', label: 'Activity', to: { name: 'activity' }, icon: 'M12 8v4l3 2M3 12a9 9 0 1 0 18 0a9 9 0 0 0-18 0' },
  { name: 'stats', label: 'Stats', to: { name: 'stats' }, icon: 'M4 20V10M10 20V4M16 20v-7M22 20H2' },
  { name: 'profile', label: 'Profile', to: { name: 'profile' }, icon: 'M5 20a7 7 0 0 1 14 0M12 3a4 4 0 1 1 0 8a4 4 0 0 1 0-8' },
]
</script>

<template>
  <nav
    class="fixed inset-x-0 bottom-0 z-30 overflow-visible border-t bg-[var(--surface-raised)] pb-[env(safe-area-inset-bottom)]"
    style="border-color: var(--border)"
    aria-label="Main"
  >
    <ul class="mx-auto grid max-w-2xl grid-cols-5 items-end">
      <li v-for="tab in tabs.slice(0, 2)" :key="tab.name" class="contents">
        <RouterLink
          :to="tab.to"
          :data-tab="tab.name"
          class="nav-tab tap-target flex flex-col items-center gap-1 py-2 text-xs text-[var(--text-muted)]"
          active-class="nav-tab-active text-brand-400"
        >
          <span data-testid="tab-icon" class="nav-tab-icon">
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.8"
              aria-hidden="true"
            >
              <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </span>
          {{ tab.label }}
        </RouterLink>
      </li>

      <li class="flex justify-center">
        <RouterLink
          :to="{ name: 'add-expense' }"
          class="tap-target -mt-8 flex h-16 w-16 items-center justify-center rounded-full bg-brand-600 text-white shadow-lg shadow-brand-600/40 transition-transform active:scale-95"
          aria-label="Add an expense"
        >
          <svg class="h-7 w-7" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" aria-hidden="true">
            <path d="M12 5v14M5 12h14" stroke-linecap="round" />
          </svg>
        </RouterLink>
      </li>

      <li v-for="tab in tabs.slice(2)" :key="tab.name" class="contents">
        <RouterLink
          :to="tab.to"
          :data-tab="tab.name"
          class="nav-tab tap-target flex flex-col items-center gap-1 py-2 text-xs text-[var(--text-muted)]"
          active-class="nav-tab-active text-brand-400"
        >
          <span data-testid="tab-icon" class="nav-tab-icon">
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.8"
              aria-hidden="true"
            >
              <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </span>
          {{ tab.label }}
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.nav-tab-icon {
  display: flex;
  height: 1.25rem;
  width: 1.25rem;
  align-items: center;
  justify-content: center;
  border-radius: 9999px;
  transition:
    height 150ms ease,
    width 150ms ease,
    margin 150ms ease,
    background-color 150ms ease;
}

/* Stands proud of the bar, the same shape as the centre action. Colour alone was
   easy to miss on a phone in daylight. */
.nav-tab-active .nav-tab-icon {
  height: 3rem;
  width: 3rem;
  margin-top: -1.75rem;
  background-color: var(--color-brand-600);
  color: #fff;
  box-shadow: 0 10px 15px -3px color-mix(in srgb, var(--color-brand-600) 30%, transparent);
}
</style>
