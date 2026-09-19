<script setup lang="ts">
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { computed } from 'vue'
import BottomNav from './BottomNav.vue'
import { labelForPath, previousScreen } from '@/router/backTarget'
import SyncIndicator from '@/components/ui/SyncIndicator.vue'

const props = withDefaults(
  defineProps<{
    title: string
    subtitle?: string
    pendingCount?: number
    rejectedCount?: number
    isOffline?: boolean
    isSyncing?: boolean
    showNav?: boolean
    backTo?: RouteLocationRaw
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

function goBack(): void {
  if (previousScreen()) {
    router.back()
    return
  }

  if (props.backTo) void router.push(props.backTo)
}

const backDestination = computed(() => {
  const previous = previousScreen()
  return (previous && labelForPath(previous)) || props.backLabel
})

const hasSyncNews = computed(
  () =>
    props.pendingCount > 0 ||
    props.rejectedCount > 0 ||
    props.isOffline ||
    props.isSyncing,
)
</script>
<template>
  <div class="flex h-full flex-col">
    <main
      data-app-page
      class="mx-auto min-h-0 w-full max-w-2xl flex-1 overflow-y-auto overscroll-contain px-4 pt-[max(1rem,env(safe-area-inset-top))]"
      :class="props.showNav ? 'pb-10' : 'pb-[max(2rem,env(safe-area-inset-bottom))]'"
    >
      <div data-testid="title-row" class="mb-4 flex items-start justify-between gap-3">
        <div class="flex min-w-0 items-start gap-3">
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
