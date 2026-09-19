<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, useTemplateRef, watch } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import PullToRefresh from '@/components/ui/PullToRefresh.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import AcrossGroups from '@/components/stats/AcrossGroups.vue'
import { useApi } from '@/api/provider'
import { looksOffline } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { computeStats } from '@/domain/localStats'
import { categoryFor } from '@/domain/categories'
import { resolveIcon } from '@/domain/icons'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { useExpensesStore } from '@/stores/expenses'
import { checkForAppUpdate } from '@/native/appUpdate'
import { memberColor } from '@/domain/memberColors'
import { formatMoney } from '@/domain/money'
import {
  fillBuckets,
  formatBucket,
  formatBucketRange,
  type Granularity,
} from '@/domain/buckets'

interface SpendPointMember {
  memberId: string
  memberName: string
  amount: number
}

interface SpendPointCategory {
  key: string | null
  amount: number
}

interface SpendPoint {
  bucket: string
  amount: number
  expenseCount: number
  byMember: SpendPointMember[]
  byCategory?: SpendPointCategory[]
}

interface CategorySpend {
  key: string | null
  amount: number
  expenseCount: number
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
  byCategory?: CategorySpend[]
}

const auth = useAuthStore()
const groups = useGroupsStore()
const expenses = useExpensesStore()

const dashboard = ref<Dashboard | null>(null)
const groupId = ref<string>('')
const granularity = ref<Granularity>('month')
const isLoading = ref(true)
const isOffline = ref(false)

watch(groupId, (id) => {
  if (id) void groups.loadCategories(id)
})

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()

  groupId.value = groups.mainGroupId ?? ''

  await load()

  watch(() => groups.mainGroupId, (next) => {
    groupId.value = next ?? ''
    void load()
  })
})

async function load(): Promise<void> {
  isLoading.value = true
  forget()

  dashboard.value = fromReplica()

  try {
    dashboard.value = await useApi().get<Dashboard>('/stats', {
      groupId: groupId.value || undefined,
      granularity: granularity.value,
    })
    isOffline.value = false
  } catch (caught) {
    isOffline.value = looksOffline(caught)
  } finally {
    isLoading.value = false
  }
}

function fromReplica(): Dashboard | null {
  const scope = groupId.value
    ? groups.groups.filter((group) => group.id === groupId.value)
    : groups.groups.filter((group) => !group.isArchived)

  if (scope.length === 0) return null

  const rows = scope.flatMap((group) => expenses.forGroup(group.id))
  const settled = scope.flatMap((group) => expenses.settlementsForGroup(group.id))
  const roster = scope.flatMap((group) => group.members)
  const userId = auth.user?.id

  return computeStats({
    currency: scope.length === 1 ? scope[0].baseCurrency : (auth.user?.defaultCurrency ?? 'CAD'),
    granularity: granularity.value,
    myMemberIds: roster.filter((member) => member.userId === userId).map((member) => member.id),
    members: roster.map((member) => ({ id: member.id, displayName: member.displayName })),
    expenses: rows,
    settlements: settled,
  })
}

const points = computed(() =>
  fillBuckets(dashboard.value?.spendOverTime ?? [], granularity.value, (bucket) => ({
    bucket,
    amount: 0,
    expenseCount: 0,
    byMember: [],
    byCategory: [],
  })),
)

const chartPeople = computed(() => {
  const seen = new Map<string, SpendPointMember>()

  for (const point of points.value) {
    for (const member of point.byMember ?? []) {
      if (!seen.has(member.memberId)) seen.set(member.memberId, member)
    }
  }

  return [...seen.values()].sort((left, right) => left.memberName.localeCompare(right.memberName))
})

const colours = computed(() =>
  groupId.value ? groups.colorsOf(groupId.value) : {},
)

const colourOf = (memberId: string) => colours.value[memberId] ?? memberColor(memberId)

const spendByCategory = computed(() => {
  const rows = dashboard.value?.byCategory ?? []
  const largest = rows.reduce((most, row) => Math.max(most, row.amount), 0)
  const known = groupId.value ? groups.categoriesOf(groupId.value) : []

  return rows.map((row) => {
    const category = categoryFor(row.key, known)

    return {
      key: row.key ?? '',
      name: category?.name ?? (row.key ? row.key : t('Not filed')),
      icon: resolveIcon(category?.iconName ?? null),
      colour: category?.colorHex ?? 'var(--text-muted)',
      amount: row.amount,
      expenseCount: row.expenseCount,
      share: largest > 0 ? row.amount / largest : 0,
    }
  })
})

const scopeName = computed(() => {
  if (!groupId.value) return t('All groups')
  return groups.groups.find((group) => group.id === groupId.value)?.name ?? t('All groups')
})

function segmentsOf(point: SpendPoint) {
  const members = point.byMember ?? []
  if (members.length === 0 || point.amount <= 0) {
    return [{ memberId: 'total', memberName: t('Total'), share: 1 }]
  }

  return members.map((member) => ({ ...member, share: member.amount / point.amount }))
}

function bucketTitle(point: SpendPoint): string {
  const parts = (point.byMember ?? []).map(
    (member) => `${member.memberName} ${formatMoney(member.amount, dashboard.value?.currency ?? 'CAD')}`,
  )

  const total = formatMoney(point.amount, dashboard.value?.currency ?? 'CAD')
  const when = bucketRange(point.bucket)

  for (const line of [...tracedLines.value].reverse()) {
    parts.unshift(
      `${line.name} ${formatMoney(amountIn(point, line.key), dashboard.value?.currency ?? 'CAD')}`,
    )
  }

  return parts.length > 0 ? `${when}: ${total} (${parts.join(', ')})` : `${when}: ${total}`
}

const chartDescription = computed(() => {
  const busy = points.value.filter((point) => point.amount > 0)
  if (busy.length === 0) return 'Spending over time'

  return `Spending over time, by who paid: ${busy.map(bucketTitle).join('; ')}`
})

const hoveredBucket = ref<string | null>(null)
const pinnedBucket = ref<string | null>(null)

const selectedBucket = computed(() => hoveredBucket.value ?? pinnedBucket.value)

const selected = computed(
  () => points.value.find((point) => point.bucket === selectedBucket.value) ?? null,
)

function look(bucket: string): void {
  hoveredBucket.value = bucket
}

function lookAway(): void {
  hoveredBucket.value = null
}

function pin(bucket: string): void {
  pinnedBucket.value = pinnedBucket.value === bucket ? null : bucket
  hoveredBucket.value = pinnedBucket.value === null ? null : bucket
}

function forget(): void {
  hoveredBucket.value = null
  pinnedBucket.value = null
}

function paidIn(point: SpendPoint, memberId: string): number {
  return point.byMember?.find((member) => member.memberId === memberId)?.amount ?? 0
}

function shareIn(point: SpendPoint, memberId: string): string {
  if (point.amount <= 0) return '0%'
  return `${Math.round((paidIn(point, memberId) / point.amount) * 100)}%`
}

const peak = computed(() => Math.max(1, ...points.value.map((point) => point.amount)))

const canTrace = computed(() =>
  (dashboard.value?.spendOverTime ?? []).some((point) => point.byCategory !== undefined),
)

function amountIn(point: SpendPoint, key: string): number {
  return (point.byCategory ?? []).find((row) => (row.key ?? '') === key)?.amount ?? 0
}

const CHART_BOX = 100

function lineFor(key: string): string {
  const amounts = points.value.map((point) => amountIn(point, key))

  const heightOf = (amount: number) =>
    CHART_BOX - Math.min(CHART_BOX, (amount / peak.value) * CHART_BOX)
  const middleOfColumn = (index: number) => ((index + 0.5) / amounts.length) * CHART_BOX

  if (amounts.length === 1) return markAcrossTheChart(heightOf(amounts[0]))

  return amounts
    .map((amount, index) => `${middleOfColumn(index)},${heightOf(amount)}`)
    .join(' ')
}

function markAcrossTheChart(height: number): string {
  const inset = CHART_BOX / 4
  return `${inset},${height} ${CHART_BOX - inset},${height}`
}

const tracedLines = computed(() =>
  canTrace.value
    ? spendByCategory.value
        .map((row) => ({
          key: row.key,
          name: row.name,
          colour: row.colour,
          points: lineFor(row.key),
        }))
        .filter((line) => line.points !== '')
    : [],
)

function shareOfCategory(point: SpendPoint, key: string): string {
  if (point.amount <= 0) return '0%'
  return `${Math.round((amountIn(point, key) / point.amount) * 100)}%`
}

const CROWDED_BUCKETS = 20
const VERY_CROWDED_BUCKETS = 40

const chartGap = computed(() => {
  if (points.value.length > VERY_CROWDED_BUCKETS) return 'gap-px'
  if (points.value.length > CROWDED_BUCKETS) return 'gap-0.5'
  return 'gap-1'
})

const SMALLEST_VISIBLE_BAR_PERCENT = 4

const barHeightPercent = (amount: number) =>
  Math.max(SMALLEST_VISIBLE_BAR_PERCENT, (amount / peak.value) * 100)

const bucketLabel = (bucket: string) => formatBucket(bucket, granularity.value)

const bucketRange = (bucket: string) => formatBucketRange(bucket, granularity.value)

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
    :title="groups.mainGroup?.name ?? 'Stats'"
    :subtitle="groups.mainGroup ? t('Stats') : undefined"
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
    <GroupSwipe />
    <PullToRefresh ref="pull" @refresh="refresh" />
    <div class="mb-4 flex gap-2">
      <select
        v-model="groupId"
        class="tap-target flex-1 rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
        style="border-color: var(--border)"
        @change="load"
      >
        <option value="">{{ t('All groups') }}</option>
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
        <option value="day">{{ t('Daily') }}</option>
        <option value="week">{{ t('Weekly') }}</option>
        <option value="month">{{ t('Monthly') }}</option>
      </select>
    </div>
    <template v-if="dashboard">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">
        {{ scopeName }}
      </h2>
      <section class="surface-card mb-4 grid grid-cols-3 gap-3 p-4 text-center">
        <div>
          <p class="text-xs text-[var(--text-muted)]">{{ t('Total') }}</p>
          <MoneyAmount :amount="dashboard.totalSpend" :currency="dashboard.currency" size="sm" />
        </div>
        <div>
          <p class="text-xs text-[var(--text-muted)]">{{ t('Your share') }}</p>
          <MoneyAmount :amount="dashboard.myShare" :currency="dashboard.currency" size="sm" />
        </div>
        <div>
          <p class="text-xs text-[var(--text-muted)]">{{ t('You paid') }}</p>
          <MoneyAmount :amount="dashboard.myPaid" :currency="dashboard.currency" size="sm" />
        </div>
      </section>
      <section v-if="points.length > 0" class="surface-card mb-4 p-4">
        <div class="mb-3 flex items-baseline justify-between gap-2">
          <h2 class="min-w-0 truncate text-sm font-medium text-[var(--text-muted)]">{{ t('Spending over time') }}
          </h2>
          <p
            v-if="selected"
            data-testid="bar-readout"
            class="flex shrink-0 items-baseline gap-2 text-sm"
          >
            <span class="text-[var(--text-muted)]">{{ bucketRange(selected.bucket) }}</span>
            <span class="font-semibold tabular-nums">
              {{ formatMoney(selected.amount, dashboard.currency) }}
            </span>
          </p>
        </div>
        <div class="relative">
          <ul
            class="flex h-32 items-end"
            :class="chartGap"
            data-testid="spend-chart"
            role="group"
            :aria-label="chartDescription"
          >
            <li
              v-for="point in points"
              :key="point.bucket"
              class="flex h-full flex-1 items-end"
            >
              <button
                type="button"
                data-testid="bar"
                class="flex h-full w-full cursor-pointer items-end transition-opacity"
                :class="selectedBucket && selectedBucket !== point.bucket ? 'opacity-40' : ''"
                :aria-pressed="selectedBucket === point.bucket"
                :aria-label="bucketTitle(point)"
                @mouseenter="look(point.bucket)"
                @mouseleave="lookAway"
                @focus="look(point.bucket)"
                @blur="lookAway"
                @click="pin(point.bucket)"
              >
                <span
                  v-if="point.amount <= 0"
                  data-testid="bar-empty"
                  class="block h-0.5 w-full rounded-full"
                  style="background: var(--border)"
                />
                <span
                  v-else
                  data-testid="bar-fill"
                  class="flex w-full flex-col-reverse overflow-hidden rounded-t"
                  :style="{ height: `${barHeightPercent(point.amount)}%` }"
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
              </button>
            </li>
          </ul>
          <svg
            v-if="tracedLines.length > 0"
            data-testid="overlay-line"
            class="pointer-events-none absolute inset-0 h-full w-full overflow-visible"
            style="filter: drop-shadow(0 1px 2px rgb(0 0 0 / 0.55))"
            viewBox="0 0 100 100"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <polyline
              v-for="line in tracedLines"
              :key="line.key"
              :points="line.points"
              :data-category="line.key"
              fill="none"
              :stroke="line.colour"
              stroke-width="2"
              stroke-linejoin="round"
              stroke-linecap="round"
              vector-effect="non-scaling-stroke"
            />
          </svg>
        </div>
        <div
          data-testid="chart-dates"
          class="mt-1 flex justify-between text-[10px] text-[var(--text-muted)]"
        >
          <span>{{ bucketLabel(points[0].bucket) }}</span>
          <span>{{ bucketLabel(points[points.length - 1].bucket) }}</span>
        </div>
        <ul
          v-if="chartPeople.length > 0"
          data-testid="chart-key"
          class="mt-3 flex flex-wrap gap-x-3 gap-y-1 text-xs"
        >
          <li
            v-for="person in chartPeople"
            :key="person.memberId"
            class="flex items-center gap-1.5"
            :class="selected && paidIn(selected, person.memberId) === 0 ? 'opacity-40' : ''"
          >
            <span
              class="h-2 w-2 shrink-0 rounded-full"
              :style="{ backgroundColor: colourOf(person.memberId) }"
              aria-hidden="true"
            />
            <span class="text-[var(--text-muted)]">{{ person.memberName }}</span>
            <span v-if="selected" data-testid="key-amount" class="tabular-nums">
              {{ formatMoney(paidIn(selected, person.memberId), dashboard.currency) }}
              <span class="text-[var(--text-muted)]">
                {{ shareIn(selected, person.memberId) }}
              </span>
            </span>
          </li>
        </ul>
        <ul
          v-if="tracedLines.length > 0"
          data-testid="chart-categories"
          class="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-xs"
        >
          <li
            v-for="line in tracedLines"
            :key="line.key"
            data-testid="category-key"
            :data-category="line.key"
            class="flex items-center gap-1.5"
            :class="selected && amountIn(selected, line.key) === 0 ? 'opacity-40' : ''"
          >
            <span
              class="h-0.5 w-3 shrink-0 rounded-full"
              :style="{ backgroundColor: line.colour }"
              aria-hidden="true"
            />
            <span class="text-[var(--text-muted)]">{{ line.name }}</span>
            <span v-if="selected" data-testid="category-key-amount" class="tabular-nums">
              {{ formatMoney(amountIn(selected, line.key), dashboard.currency) }}
              <span class="text-[var(--text-muted)]">
                {{ shareOfCategory(selected, line.key) }}
              </span>
            </span>
          </li>
        </ul>
      </section>
      <section v-if="spendByCategory.length > 0" class="surface-card mb-4 p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">{{ t('Where it went') }}</h2>
        <ul class="flex flex-col gap-2.5">
          <li
            v-for="row in spendByCategory"
            :key="row.key"
            data-testid="category-row"
            :data-category="row.key"
            class="flex flex-col gap-1"
          >
            <span class="flex items-baseline justify-between gap-3 text-sm">
              <span class="flex min-w-0 items-center gap-2">
                <FontAwesomeIcon
                  :icon="row.icon.definition"
                  class="h-3.5 w-3.5 shrink-0"
                  :style="{ color: row.colour }"
                  aria-hidden="true"
                />
                <span class="truncate">{{ row.name }}</span>
                <span class="shrink-0 text-xs text-[var(--text-muted)]">{{ row.expenseCount }}</span>
              </span>
              <span class="shrink-0 tabular-nums">
                {{ formatMoney(row.amount, dashboard.currency) }}
              </span>
            </span>
            <span class="h-1.5 w-full overflow-hidden rounded-full" style="background: var(--surface-sunken)">
              <span
                class="block h-full rounded-full"
                :style="{ width: `${Math.max(row.share * 100, 2)}%`, backgroundColor: row.colour }"
                aria-hidden="true"
              />
            </span>
          </li>
        </ul>
      </section>
      <section v-if="dashboard.byMember.length > 0" class="surface-card p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">{{ t('Who owes whom') }}</h2>
        <ul class="flex flex-col gap-2 text-sm">
          <li v-for="member in dashboard.byMember" :key="member.memberId" class="flex justify-between">
            <span>{{ member.memberName }}</span>
            <MoneyAmount :amount="member.net" :currency="dashboard.currency" signed size="sm" />
          </li>
        </ul>
      </section>
    </template>
    <p v-else-if="isLoading" class="py-12 text-center text-sm text-[var(--text-muted)]">{{ t('Loading stats') }}
    </p>
    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      {{ t('Nothing to add up yet. Add an expense and this fills in.') }}
    </p>
    <AcrossGroups />
  </AppShell>
</template>
