<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
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
  /** What the entry is about, so an expense can be opened from here. */
  subjectType: string | null
  subjectId: string | null
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

/**
 * Where an entry leads, when it leads anywhere.
 *
 * The feed is where you notice something, and noticing it is useless if you cannot
 * then look at it. An expense entry opens the expense; the rest have no single
 * thing to open, so they stay as text rather than pretending to be links.
 */
function targetOf(entry: ActivityEntry) {
  if (entry.subjectType !== 'Expense' || !entry.subjectId || !entry.groupId) return null

  return {
    name: 'expense',
    params: { groupId: entry.groupId, expenseId: entry.subjectId },
  }
}
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
      <li v-for="entry in entries" :key="entry.id">
        <!-- A link where there is something to open, and plain text where there is
             not, rather than a card that looks tappable and does nothing. -->
        <RouterLink
          v-if="targetOf(entry)"
          :to="targetOf(entry)!"
          data-testid="activity-row"
          data-linked="true"
          class="surface-card tap-target block p-3"
        >
          <span class="block text-sm">{{ entry.summary }}</span>
          <span class="mt-1 block text-xs text-[var(--text-muted)]">
            <template v-if="entry.groupName">{{ entry.groupName }} - </template>
            {{ when(entry.occurredAt) }}
          </span>
        </RouterLink>

        <div v-else data-testid="activity-row" data-linked="false" class="surface-card p-3">
          <p class="text-sm">{{ entry.summary }}</p>
          <p class="mt-1 text-xs text-[var(--text-muted)]">
            <template v-if="entry.groupName">{{ entry.groupName }} - </template>
            {{ when(entry.occurredAt) }}
          </p>
        </div>
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
