<script setup lang="ts">
import { intlLocale, t } from '@/i18n'
import { computed, onMounted, ref, useTemplateRef, watch } from 'vue'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import PullToRefresh from '@/components/ui/PullToRefresh.vue'
import CollapsibleSection from '@/components/ui/CollapsibleSection.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { checkForAppUpdate } from '@/native/appUpdate'
import { useApi } from '@/api/provider'
import { looksOffline } from '@/api/client'
import { db } from '@/offline/db'
import { memberColor } from '@/domain/memberColors'
import { report } from '@/ui/toasts'

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
  await expenses.hydrate()
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

const settled = computed(() =>
  groups.mainGroupId ? expenses.settlementsForGroup(groups.mainGroupId) : [],
)

const memberName = (memberId: string) =>
  (groups.mainGroupId ? groups.membersOf(groups.mainGroupId) : [])
    .find((member) => member.id === memberId)?.displayName ?? ''

const settledOn = (iso: string) =>
  new Date(iso).toLocaleDateString(intlLocale.value, { day: 'numeric', month: 'short' })

async function unsettle(settlementId: string): Promise<void> {
  try {
    await expenses.unsettle(settlementId)
  } catch (caught) {
    report(caught, t('Could not take that settlement back.'))
  }
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
    width="wide"
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
    <CollapsibleSection
      v-if="settled.length > 0"
      :title="t('Already settled')"
      :count="settled.length"
      testid="already-settled"
    >
      <ul class="flex flex-col gap-2 text-sm">
        <li
          v-for="entry in settled"
          :key="entry.id"
          data-testid="settlement-row"
          class="flex items-center justify-between gap-2"
        >
          <span class="min-w-0">
            <span class="block truncate">
              {{ memberName(entry.fromMemberId) }} paid {{ memberName(entry.toMemberId) }}
            </span>
            <span class="block truncate text-xs text-[var(--text-muted)]">
              {{ settledOn(entry.settledAt) }}<template v-if="entry.note"> - {{ entry.note }}</template>
            </span>
          </span>
          <span class="flex shrink-0 items-center gap-2">
            <MoneyAmount :amount="entry.amount" :currency="entry.currency" size="sm" />
            <button
              type="button"
              :data-testid="`unsettle-${entry.id}`"
              class="tap-target px-2 text-xs text-[var(--text-muted)]"
              :aria-label="t('Take this settlement back')"
              :title="t('Take this settlement back')"
              @click="unsettle(entry.id)"
            >
              <span aria-hidden="true">x</span>
            </button>
          </span>
        </li>
      </ul>
    </CollapsibleSection>
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
