<script setup lang="ts">
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { computed } from 'vue'
import BottomNav from './BottomNav.vue'
import { labelForPath, previousScreen } from '@/router/backTarget'
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
     * Where this screen goes back to when there is nothing behind it: opened from
     * a notification, a shared link, or a cold start. The rest of the time the
     * control goes back the way the person came, which is what back means - an
     * expense reached from the activity feed returns to the feed, not to the group
     * the expense happens to belong to.
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

const router = useRouter()

/**
 * Back the way the person came, or to the screen this one belongs under.
 *
 * History first, because that is what back means: an expense opened from the
 * activity feed returns to the feed, and the same expense opened from its group
 * returns to the group, without either screen having to know where it was reached
 * from. The declared destination is for when there is no history to use - a shared
 * link, a notification, a cold start - where going back would leave the app.
 */
function goBack(): void {
  if (previousScreen()) {
    router.back()
    return
  }

  if (props.backTo) void router.push(props.backTo)
}

/** What the control says it leads to, which is the screen actually behind it. */
const backDestination = computed(() => {
  const previous = previousScreen()
  return (previous && labelForPath(previous)) || props.backLabel
})

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
  <!--
    The frame: the height of the screen, once, and it never grows. Everything that
    scrolls scrolls inside it, which is what keeps the tab bar below from moving -
    a window that scrolls is a window whose browser slides its own toolbar about,
    and everything pinned to the bottom of the screen goes with it.

    Full width, with the page centred inside: the bar spans the window on a wide
    screen the way it did when it was pinned there.
  -->
  <div class="flex h-full flex-col">
    <!--
      No chrome at the top: the page is the page, and the only furniture is the tab
      bar at the bottom. What the header used to hold now scrolls with the content,
      so it takes no room once you are reading.

      Named so a gesture can move it, too. Changing group is a swipe across the
      screen on the group screens, and the page it is leaving slides out with the
      finger; the shell holds the page, so the shell has to be the movable thing.
    -->
    <main
      data-app-page
      class="mx-auto min-h-0 w-full max-w-2xl flex-1 overflow-y-auto overscroll-contain px-4 pt-[max(1rem,env(safe-area-inset-top))]"
      :class="props.showNav ? 'pb-10' : 'pb-[max(2rem,env(safe-area-inset-bottom))]'"
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
            Top left, where a person looks to know what they have open. The app's
            mark by default, and the group's own where the screen is about a group,
            because that is what tells two groups apart at a glance.

            Decorative either way: the title beside it already says the name, so a
            screen reader announcing both would say it twice.
          -->
          <slot name="mark">
            <img
              src="/icons/icon.svg"
              alt=""
              width="32"
              height="32"
              data-testid="app-icon"
              class="mt-0.5 h-8 w-8 shrink-0 rounded-lg"
            />
          </slot>

          <div class="min-w-0">
            <h1 class="truncate text-xl font-semibold">{{ title }}</h1>
            <p v-if="subtitle" class="truncate text-sm text-[var(--text-muted)]">{{ subtitle }}</p>
          </div>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <slot name="header-action" />

          <button
            v-if="props.backTo"
            type="button"
            data-testid="back"
            class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
            :aria-label="`Back to ${backDestination}`"
            @click="goBack"
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
          </button>
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
