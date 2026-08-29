<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useApi } from '@/api/provider'

interface ActivityEntry {
  id: number
  groupId: string | null
  groupName: string | null
  kind: string
  actorName: string | null
  summary: string
  occurredAt: string
}

const groups = useGroupsStore()
const expenses = useExpensesStore()

const entries = ref<ActivityEntry[]>([])
const isLoading = ref(true)
const isOffline = ref(false)


onMounted(async () => {
  try {
    const page = await useApi().get<{ items: ActivityEntry[] }>('/activity', { pageSize: 100 })
    entries.value = page.items
    isOffline.value = false
  } catch {
    // The feed is a server-rendered read; offline it simply has nothing to show.
    isOffline.value = true
  } finally {
    isLoading.value = false
  }
})

const when = (iso: string) => new Date(iso).toLocaleString()
</script>

<template>
  <AppShell
    title="Activity"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="isOffline || groups.isOffline"
    :is-syncing="expenses.isSyncing"
  >
    <ul v-if="entries.length > 0" class="flex flex-col gap-2">
      <li v-for="entry in entries" :key="entry.id" class="surface-card p-3">
        <p class="text-sm">{{ entry.summary }}</p>
        <p class="mt-1 text-xs text-[var(--text-muted)]">
          <template v-if="entry.groupName">{{ entry.groupName }} - </template>
          {{ when(entry.occurredAt) }}
        </p>
      </li>
    </ul>

    <p v-else-if="isLoading" class="py-12 text-center text-sm text-[var(--text-muted)]">
      Loading activity
    </p>

    <p v-else-if="isOffline" class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      The activity feed needs a connection. Your groups and expenses still work offline.
    </p>

    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      Nothing has happened yet.
    </p>
  </AppShell>
</template>
