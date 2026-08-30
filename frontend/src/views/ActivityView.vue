<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useApi } from '@/api/provider'
import { memberColor } from '@/domain/memberColors'

interface ActivityEntry {
  id: number
  groupId: string | null
  groupName: string | null
  kind: string
  /** The membership that acted, which is what colours are keyed by. */
  actorMemberId: string | null
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
  await groups.loadAll()
  await load()
})

// Follows the group the rest of the app is on, and reloads when that changes.
watch(() => groups.mainGroupId, () => void load())

async function load(): Promise<void> {
  isLoading.value = true

  try {
    const page = await useApi().get<{ items: ActivityEntry[] }>('/activity', {
      pageSize: 100,
      groupId: groups.mainGroupId ?? undefined,
    })
    entries.value = page.items
    isOffline.value = false
  } catch {
    // The feed is a server-rendered read; offline it simply has nothing to show.
    isOffline.value = true
  } finally {
    isLoading.value = false
  }
}

const when = (iso: string) => new Date(iso).toLocaleString()

const colours = computed(() =>
  groups.mainGroupId ? groups.colorsOf(groups.mainGroupId) : {},
)

/**
 * The colour of whoever acted, as the card, matching the expense cards exactly.
 *
 * Mixed with the surface rather than used raw: the text has to stay readable, and
 * mixing with the token keeps it right in both themes. An entry with nobody behind
 * it, such as a system event, keeps the plain surface rather than borrowing
 * somebody else's colour.
 */
function cardStyle(entry: ActivityEntry) {
  if (!entry.actorMemberId) return undefined

  const colour = colours.value[entry.actorMemberId] ?? memberColor(entry.actorMemberId)

  return {
    backgroundColor: `color-mix(in oklab, ${colour} 16%, var(--surface-raised))`,
    borderColor: `color-mix(in oklab, ${colour} 35%, transparent)`,
    borderLeftColor: colour,
  }
}

/**
 * Where an entry leads.
 *
 * The feed is where you notice something, and noticing it is useless if you cannot
 * then look at it. An expense entry opens that expense. Everything else opens the
 * group it happened in, which is the nearest thing there is to look at: a member
 * being added has no screen of its own, but the roster does.
 *
 * Only an entry with no group at all stays as plain text, rather than being a card
 * that looks tappable and does nothing.
 */
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

    <!--
      Renders nothing but a moment's confirmation: swiping across the screen moves
      to the next group, which is the navigation this app does most.
    -->
    <GroupSwipe />

    <ul v-if="entries.length > 0" class="flex flex-col gap-2">
      <li v-for="entry in entries" :key="entry.id">
        <!-- A link where there is something to open, and plain text where there is
             not, rather than a card that looks tappable and does nothing. -->
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

          <!--
            Styled as a button but not one: the whole card is already the link, and
            a button inside a link is neither valid nor predictable. Tapping it
            follows the card, which is what it looks like it does. Hidden from a
            screen reader, which is being handed the link itself.
          -->
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

    <p v-else-if="isOffline" class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">{{ t('The activity feed needs a connection. Your groups and expenses still work offline.') }}
    </p>

    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">{{ t('Nothing has happened yet.') }}
    </p>
  </AppShell>
</template>
