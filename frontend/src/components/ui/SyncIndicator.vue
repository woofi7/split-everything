<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  pendingCount: number
  rejectedCount: number
  isOffline: boolean
  isSyncing: boolean
}>()

/**
 * Tells the user where their changes stand.
 *
 * Offline-first only works if people trust it, and that trust comes from being
 * told plainly that a change is saved and waiting rather than lost.
 */
const state = computed(() => {
  if (props.rejectedCount > 0) {
    return {
      key: 'rejected',
      label:
        props.rejectedCount === 1
          ? '1 change needs attention'
          : `${props.rejectedCount} changes need attention`,
      tone: 'text-owing',
    }
  }
  if (props.isSyncing) return { key: 'syncing', label: 'Syncing', tone: 'text-[var(--text-muted)]' }
  if (props.pendingCount > 0) {
    return {
      key: 'pending',
      label: props.isOffline
        ? `Offline, ${props.pendingCount} waiting`
        : `${props.pendingCount} waiting to sync`,
      tone: 'text-brand-400',
    }
  }
  if (props.isOffline) return { key: 'offline', label: 'Offline', tone: 'text-[var(--text-muted)]' }
  return { key: 'synced', label: 'All synced', tone: 'text-[var(--text-muted)]' }
})
</script>

<template>
  <p
    class="flex items-center gap-2 text-xs"
    :class="state.tone"
    :data-state="state.key"
    role="status"
    aria-live="polite"
  >
    <span
      class="inline-block h-2 w-2 rounded-full"
      :class="{
        'bg-owing': state.key === 'rejected',
        'bg-brand-400': state.key === 'pending',
        'bg-owed': state.key === 'synced',
        'bg-[var(--color-ink-500)]': state.key === 'offline' || state.key === 'syncing',
        'animate-pulse': state.key === 'syncing',
      }"
      aria-hidden="true"
    />
    {{ state.label }}
  </p>
</template>
