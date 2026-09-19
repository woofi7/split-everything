<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, useTemplateRef, watch } from 'vue'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import PullToRefresh from '@/components/ui/PullToRefresh.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { checkForAppUpdate } from '@/native/appUpdate'
import { useApi } from '@/api/provider'
import { looksOffline } from '@/api/client'
import { db } from '@/offline/db'
import { memberColor } from '@/domain/memberColors'

interface ActivityEntry {
  id: number
  groupId: string | null
  groupName: string | null
  kind: string
  actorMemberId: string | null
  actorName: string | null
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

const KEPT_ENTRIES = 300

onMounted(async () => {
  await groups.loadAll()
  await load()
})

watch(() => groups.mainGroupId, () => void load())

async function load(): Promise<void> {
  isLoading.value = true

  entries.value = await stored()
  if (entries.value.length > 0) isLoading.value = false

  try {
    const page = await useApi().get<{ items: ActivityEntry[] }>('/activity', {
      pageSize: 100,
      groupId: groups.mainGroupId ?? undefined,
    })
    entries.value = page.items
    isOffline.value = false
    await keep(page.items)
  } catch (caught) {
    isOffline.value = looksOffline(caught)
  } finally {
    isLoading.value = false
  }
}

async function stored(): Promise<ActivityEntry[]> {
  const groupId = groups.mainGroupId
  const rows = groupId
    ? await db.activity.where('groupId').equals(groupId).toArray()
    : await db.activity.toArray()

  return rows
    .slice()
    .sort((left, right) => right.occurredAt.localeCompare(left.occurredAt))
}

async function keep(items: ActivityEntry[]): Promise<void> {
  if (items.length === 0) return

  await db.activity.bulkPut(items)

  const total = await db.activity.count()
  if (total <= KEPT_ENTRIES) return

  const oldest = await db.activity.orderBy('occurredAt').limit(total - KEPT_ENTRIES).toArray()
  await db.activity.bulkDelete(oldest.map((row) => row.id))
}

const when = (iso: string) => new Date(iso).toLocaleString()

const colours = computed(() =>
  groups.mainGroupId ? groups.colorsOf(groups.mainGroupId) : {},
)

function cardStyle(entry: ActivityEntry) {
  if (!entry.actorMemberId) return undefined

  const colour = colours.value[entry.actorMemberId] ?? memberColor(entry.actorMemberId)

  return {
    backgroundColor: `color-mix(in oklab, ${colour} 16%, var(--surface-raised))`,
    borderColor: `color-mix(in oklab, ${colour} 35%, transparent)`,
    borderLeftColor: colour,
  }
}

function targetOf(entry: ActivityEntry) {
  if (!entry.groupId) return null

  if (entry.subjectType === 'Expense' && entry.subjectId) {
    return {
      name: 'expense',
      params: { groupId: entry.groupId, expenseId: entry.subjectId },
    }
  }

  return { name: 'group', params: { groupId: entry.groupId } }
}

const pull = useTemplateRef<{ done: () => void }>('pull')

async function refresh(): Promise<void> {
  try {
    await checkForAppUpdate()
    await expenses.sync()
  } catch {
  }

  await load()
  pull.value?.done()
}
</script>
<template>
  <AppShell
    :title="groups.mainGroup?.name ?? 'Activity'"
    :subtitle="groups.mainGroup ? t('Activity') : undefined"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="isOffline || groups.isOffline"
    :is-syncing="expenses.isSyncing"
  >
    <template #mark>
      <GroupMark />
    </template>
    <template #header-action>
      <GroupSettingsButton />
    </template>
    <GroupSwipe />
    <PullToRefresh ref="pull" @refresh="refresh" />
    <ul v-if="entries.length > 0" class="flex flex-col gap-2">
      <li v-for="entry in entries" :key="entry.id">
        <RouterLink
          v-if="targetOf(entry)"
          :to="targetOf(entry)!"
          data-testid="activity-row"
          data-linked="true"
          class="tap-target flex items-center justify-between gap-3 rounded-xl border border-l-4 p-3"
          :class="entry.actorMemberId ? '' : 'surface-card'"
          :style="cardStyle(entry)"
        >
          <span class="min-w-0">
            <span class="block text-sm">{{ entry.summary }}</span>
            <span class="mt-1 block text-xs text-[var(--text-muted)]">
              <template v-if="entry.groupName">{{ entry.groupName }} - </template>
              {{ when(entry.occurredAt) }}
            </span>
          </span>
          <span
            data-testid="activity-view"
            aria-hidden="true"
            class="shrink-0 rounded-lg border px-2.5 py-1 text-xs text-[var(--text-muted)]"
            style="border-color: var(--border); background: var(--surface-raised)"
          >{{ t('View') }}
          </span>
        </RouterLink>
        <div
          v-else
          data-testid="activity-row"
          data-linked="false"
          class="rounded-xl border border-l-4 p-3"
          :class="entry.actorMemberId ? '' : 'surface-card'"
          :style="cardStyle(entry)"
        >
          <p class="text-sm">{{ entry.summary }}</p>
          <p class="mt-1 text-xs text-[var(--text-muted)]">
            <template v-if="entry.groupName">{{ entry.groupName }} - </template>
            {{ when(entry.occurredAt) }}
          </p>
        </div>
      </li>
    </ul>
    <p v-else-if="isLoading" class="py-12 text-center text-sm text-[var(--text-muted)]">{{ t('Loading activity') }}
    </p>
    <p v-else-if="isOffline" class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      {{ t('No activity stored on this device yet. It fills in next time you are online.') }}
    </p>
    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">{{ t('Nothing has happened yet.') }}
    </p>
  </AppShell>
</template>
