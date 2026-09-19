<script setup lang="ts">
import { t } from '@/i18n'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  pendingCount: number
  rejectedCount: number
  isOffline: boolean
  isSyncing: boolean
}>()

const state = computed(() => {
  if (props.rejectedCount > 0) {
    return {
      key: 'rejected',
      label:
        props.rejectedCount === 1
          ? t('1 change needs attention')
          : t('{count} changes need attention', { count: props.rejectedCount }),
      tone: 'text-owing',
    }
  }
  if (props.isSyncing) return { key: 'syncing', label: t('Syncing'), tone: 'text-[var(--text-muted)]' }
  if (props.pendingCount > 0) {
    return {
      key: 'pending',
      label: props.isOffline
        ? t('Offline, {count} waiting', { count: props.pendingCount })
        : t('{count} waiting to sync', { count: props.pendingCount }),
      tone: 'text-accent',
    }
  }
  if (props.isOffline) return { key: 'offline', label: t('Offline'), tone: 'text-[var(--text-muted)]' }
  return { key: 'synced', label: t('All synced'), tone: 'text-[var(--text-muted)]' }
})

const hasSomethingToShow = computed(() => props.pendingCount > 0 || props.rejectedCount > 0)
</script>
<template>
  <component
    :is="hasSomethingToShow ? RouterLink : 'p'"
    :to="hasSomethingToShow ? { name: 'conflicts' } : undefined"
    :data-testid="hasSomethingToShow ? 'sync-indicator-link' : undefined"
    class="flex items-center gap-2 text-xs"
    :class="[state.tone, hasSomethingToShow ? 'underline decoration-dotted underline-offset-2' : '']"
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
  </component>
</template>
