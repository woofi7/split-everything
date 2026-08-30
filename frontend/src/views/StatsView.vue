<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useApi } from '@/api/provider'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { memberColor } from '@/domain/memberColors'
import { formatMoney } from '@/domain/money'

interface SpendPointMember {
  memberId: string
  memberName: string
  amount: number
}

interface SpendPoint {
  bucket: string
  amount: number
  expenseCount: number
  /** Who paid within this bucket, largest first, summing to amount. */
  byMember: SpendPointMember[]
}

interface MemberSpend {
  memberId: string
  memberName: string
  paid: number
  owed: number
  net: number
}

interface Dashboard {
  currency: string
  totalSpend: number
  myShare: number
  myPaid: number
  expenseCount: number
  spendOverTime: SpendPoint[]
  byMember: MemberSpend[]
}

const groups = useGroupsStore()
const expenses = useExpensesStore()

const dashboard = ref<Dashboard | null>(null)
/** Starts on the main group, which is what the rest of the app is showing. */
const groupId = ref<string>('')
const granularity = ref<'day' | 'week' | 'month'>('month')
const isLoading = ref(true)
const isOffline = ref(false)


onMounted(async () => {
  await groups.loadAll()

  // Opens on the group the rest of the app is showing, rather than on a total
  // across groups that nobody asked for.
  groupId.value = groups.mainGroupId ?? ''

  await load()

  /**
   * Follows the group the menu switches to.
   *
   * The filter below can also say All groups, which the app's group never does,
   * so the two are not the same choice. But changing group in the corner is a
   * statement about what you are looking at, and a chart left on the old one
   * would be answering a question nobody is asking any more.
   *
   * Watched from here rather than at setup, so that loading the groups settling
   * the main group for the first time does not read as someone switching it and
   * send a second request for the same chart.
   */
  watch(() => groups.mainGroupId, (next) => {
    groupId.value = next ?? ''
    void load()
  })
})

async function load(): Promise<void> {
  isLoading.value = true
  try {
    dashboard.value = await useApi().get<Dashboard>('/stats', {
      groupId: groupId.value || undefined,
      granularity: granularity.value,
    })
    isOffline.value = false
  } catch {
    isOffline.value = true
  } finally {
    isLoading.value = false
  }
}

/**
 * Everyone who paid anything in the window, in a stable order, for the key under
 * the chart. Taken across buckets rather than per bucket so the key does not
 * change as you switch granularity.
 */
const chartPeople = computed(() => {
  const seen = new Map<string, SpendPointMember>()

  for (const point of dashboard.value?.spendOverTime ?? []) {
    for (const member of point.byMember ?? []) {
      if (!seen.has(member.memberId)) seen.set(member.memberId, member)
    }
  }

  return [...seen.values()].sort((left, right) => left.memberName.localeCompare(right.memberName))
})

// From the group's roster rather than from whoever appears in the chart: the
// palette walks to the next free colour in the order it is given, so a different
// list makes the same person a different colour from the expense cards.
const colours = computed(() =>
  groupId.value ? groups.colorsOf(groupId.value) : {},
)

const colourOf = (memberId: string) => colours.value[memberId] ?? memberColor(memberId)

/**
 * Each person's share of their own bucket, so the segments fill the bar whatever
 * the bar's height. A bucket the server sent without a breakdown falls back to one
 * whole segment rather than an empty bar.
 */
function segmentsOf(point: SpendPoint) {
  const members = point.byMember ?? []
  if (members.length === 0 || point.amount <= 0) {
    return [{ memberId: 'total', memberName: 'Total', share: 1 }]
  }

  return members.map((member) => ({ ...member, share: member.amount / point.amount }))
}

function bucketTitle(point: SpendPoint): string {
  const parts = (point.byMember ?? []).map(
    (member) => `${member.memberName} ${formatMoney(member.amount, dashboard.value?.currency ?? 'CAD')}`,
  )

  const total = formatMoney(point.amount, dashboard.value?.currency ?? 'CAD')
  return parts.length > 0
    ? `${bucketLabel(point.bucket)}: ${total} (${parts.join(', ')})`
    : `${bucketLabel(point.bucket)}: ${total}`
}

/** A stack of coloured blocks says nothing to a screen reader without this. */
const chartDescription = computed(() => {
  const points = dashboard.value?.spendOverTime ?? []
  if (points.length === 0) return 'Spending over time'

  return `Spending over time, by who paid: ${points.map(bucketTitle).join('; ')}`
})

/** Scaled against the largest bucket, so the bars are readable at any spend level. */
const peak = computed(() =>
  Math.max(1, ...(dashboard.value?.spendOverTime.map((point) => point.amount) ?? [1])),
)

const bucketLabel = (bucket: string) =>
  new Date(`${bucket}T00:00:00Z`).toLocaleDateString(undefined, {
    month: 'short',
    year: granularity.value === 'month' ? '2-digit' : undefined,
    day: granularity.value === 'month' ? undefined : 'numeric',
  })
</script>

<template>
  <AppShell
    :title="groups.mainGroup?.name ?? 'Stats'"
    :subtitle="groups.mainGroup ? 'Stats' : undefined"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="isOffline"
    :is-syncing="expenses.isSyncing"
  >
    <template #mark>
      <GroupMark />
    </template>

    <template #header-action>
      <GroupSettingsButton />
    </template>

    <div class="mb-4 flex gap-2">
      <select
        v-model="groupId"
        class="tap-target flex-1 rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
        style="border-color: var(--border)"
        @change="load"
      >
        <option value="">All groups</option>
        <option v-for="group in groups.visibleGroups" :key="group.id" :value="group.id">
          {{ group.name }}
        </option>
      </select>

      <select
        v-model="granularity"
        class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
        style="border-color: var(--border)"
        @change="load"
      >
        <option value="day">Daily</option>
        <option value="week">Weekly</option>
        <option value="month">Monthly</option>
      </select>
    </div>

    <template v-if="dashboard">
      <section class="surface-card mb-4 grid grid-cols-3 gap-3 p-4 text-center">
        <div>
          <p class="text-xs text-[var(--text-muted)]">Total</p>
          <MoneyAmount :amount="dashboard.totalSpend" :currency="dashboard.currency" size="sm" />
        </div>
        <div>
          <p class="text-xs text-[var(--text-muted)]">Your share</p>
          <MoneyAmount :amount="dashboard.myShare" :currency="dashboard.currency" size="sm" />
        </div>
        <div>
          <p class="text-xs text-[var(--text-muted)]">You paid</p>
          <MoneyAmount :amount="dashboard.myPaid" :currency="dashboard.currency" size="sm" />
        </div>
      </section>

      <section v-if="dashboard.spendOverTime.length > 0" class="surface-card mb-4 p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">Spending over time</h2>
        <ul
          class="flex h-32 items-end gap-1"
          data-testid="spend-chart"
          role="img"
          :aria-label="chartDescription"
        >
          <li
            v-for="point in dashboard.spendOverTime"
            :key="point.bucket"
            class="flex h-full flex-1 items-end"
            :title="bucketTitle(point)"
          >
            <!--
              Stacked by whoever paid, in that person's colour. The total alone says
              how much a month cost; the split also says who carried it, which is the
              thing a shared account argues about.
            -->
            <span
              class="flex w-full flex-col-reverse overflow-hidden rounded-t"
              :style="{ height: `${Math.max(4, (point.amount / peak) * 100)}%` }"
            >
              <span
                v-for="member in segmentsOf(point)"
                :key="member.memberId"
                data-testid="bar-segment"
                class="block w-full"
                :style="{
                  height: `${member.share * 100}%`,
                  backgroundColor: colourOf(member.memberId),
                }"
              />
            </span>
          </li>
        </ul>

        <ul v-if="chartPeople.length > 0" class="mt-3 flex flex-wrap gap-x-3 gap-y-1 text-xs">
          <li v-for="person in chartPeople" :key="person.memberId" class="flex items-center gap-1.5">
            <span
              class="h-2 w-2 shrink-0 rounded-full"
              :style="{ backgroundColor: colourOf(person.memberId) }"
              aria-hidden="true"
            />
            <span class="text-[var(--text-muted)]">{{ person.memberName }}</span>
          </li>
        </ul>
        <div class="mt-1 flex justify-between text-[10px] text-[var(--text-muted)]">
          <span>{{ bucketLabel(dashboard.spendOverTime[0].bucket) }}</span>
          <span>
            {{ bucketLabel(dashboard.spendOverTime[dashboard.spendOverTime.length - 1].bucket) }}
          </span>
        </div>
      </section>

      <section v-if="dashboard.byMember.length > 0" class="surface-card p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">Who owes whom</h2>
        <ul class="flex flex-col gap-2 text-sm">
          <li v-for="member in dashboard.byMember" :key="member.memberId" class="flex justify-between">
            <span>{{ member.memberName }}</span>
            <MoneyAmount :amount="member.net" :currency="dashboard.currency" signed size="sm" />
          </li>
        </ul>
      </section>
    </template>

    <p v-else-if="isLoading" class="py-12 text-center text-sm text-[var(--text-muted)]">
      Loading stats
    </p>

    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      Stats need a connection. Your groups and expenses still work offline.
    </p>
  </AppShell>
</template>
