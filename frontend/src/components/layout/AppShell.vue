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
      <!--
        Back on its own, top left, as a button rather than a chevron tucked beside
        the title. It is the way out of every screen a tab cannot reach, so it
        should not have to be found.
      -->
      <!--
        Title on the left, everything that acts on the page on the right, all on one
        line. Aligned to the top of the block so they track the title rather than
        drifting to the middle when there is a subtitle under it.

        Back sits furthest right, in the corner: it is the one control that is not
        about this page but about leaving it.
      -->
      <div data-testid="title-row" class="mb-4 flex items-start justify-between gap-3">
        <div class="flex min-w-0 items-start gap-3">
          <!--
            The app's mark, top left, where a person looks to know what they have
            open. Decorative: the title beside it already says the name, so a
            screen reader announcing both would say it twice.
          -->
          <img
            src="/icons/icon.svg"
            alt=""
            width="32"
            height="32"
            data-testid="app-icon"
            class="mt-0.5 h-8 w-8 shrink-0 rounded-lg"
          />

          <div class="min-w-0">
            <h1 class="truncate text-xl font-semibold">{{ title }}</h1>
            <p v-if="subtitle" class="truncate text-sm text-[var(--text-muted)]">{{ subtitle }}</p>
          </div>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <slot name="header-action" />

          <RouterLink
            v-if="props.backTo"
            :to="props.backTo"
            data-testid="back"
            class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
            :aria-label="`Back to ${props.backLabel}`"
          >
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2.2"
              aria-hidden="true"
            >
              <path d="M15 5l-7 7 7 7" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </RouterLink>
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
