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
  /** Who paid within this bucket, largest first, summing to amount. */
  byMember: SpendPointMember[]
  /** The same bucket by what it went on, which is what the line is drawn from. */
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
/** Starts on the main group, which is what the rest of the app is showing. */
const groupId = ref<string>('')
const granularity = ref<Granularity>('month')
const isLoading = ref(true)
const isOffline = ref(false)

/**
 * The names, icons and colours the breakdown is drawn with.
 *
 * Every other screen that shows a category reaches for the group first, which
 * brings its list along; this one only ever loads the list of groups. Opened cold
 * - a bookmark, a restored tab - the breakdown read out raw keys, and now the
 * control that traces one over the chart would be a list of them. Watched on the
 * group rather than asked for in load(), which also runs when the granularity
 * changes and would spend a request on a question that has not changed.
 */
watch(groupId, (id) => {
  if (id) void groups.loadCategories(id)
})


onMounted(async () => {
  await groups.loadAll()
  // The rows the local answer is computed from. Cheap when they are already in
  // memory, and the only reason this screen works offline at all.
  await expenses.hydrate()

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
  // Whatever was being asked about belongs to the chart being replaced.
  forget()

  /*
   * The replica first, then the server.
   *
   * Every number on this screen is arithmetic over rows this device already holds,
   * so it is worked out here and shown immediately - which is what makes the screen
   * work offline, and makes it instant online. The server's answer replaces it when
   * one arrives: it can convert between currencies, which this cannot, and it sees
   * anything the replica has not pulled yet.
   */
  dashboard.value = fromReplica()

  try {
    dashboard.value = await useApi().get<Dashboard>('/stats', {
      groupId: groupId.value || undefined,
      granularity: granularity.value,
    })
    isOffline.value = false
  } catch (caught) {
    // The local answer stands. Offline is a normal state here, not a failure -
    // and a refusal is not offline, whatever else it is.
    isOffline.value = looksOffline(caught)
  } finally {
    isLoading.value = false
  }
}

/**
 * The same dashboard, computed from what is stored on this device.
 *
 * One group is exact: every amount is already in that group's currency. Across all
 * groups it adds base-currency amounts together without converting, which is what
 * the cross-group total on the dashboard has always done; the server's answer
 * corrects it as soon as there is one.
 */
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

/**
 * The buckets the chart draws.
 *
 * Every one between the first and the last, including the ones nothing happened
 * in, because otherwise the axis is not time: two bars side by side could be a day
 * apart or a month, and a quiet fortnight looks like a busy one.
 */
const points = computed(() =>
  fillBuckets(dashboard.value?.spendOverTime ?? [], granularity.value, (bucket) => ({
    bucket,
    amount: 0,
    expenseCount: 0,
    byMember: [],
    byCategory: [],
  })),
)

/**
 * Everyone who paid anything in the window, in a stable order, for the key under
 * the chart. Taken across buckets rather than per bucket so the key does not
 * change as you switch granularity.
 */
const chartPeople = computed(() => {
  const seen = new Map<string, SpendPointMember>()

  for (const point of points.value) {
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
 * Where the money went, by category.
 *
 * A bar each rather than a pie: this is a ranking, and a ranking is read down a
 * column. What nobody filed is shown with the rest instead of being dropped - a
 * breakdown that quietly omits a third of the spending is worse than one that
 * admits to it.
 */
const spendByCategory = computed(() => {
  const rows = dashboard.value?.byCategory ?? []
  const largest = rows.reduce((most, row) => Math.max(most, row.amount), 0)
  const known = groupId.value ? groups.categoriesOf(groupId.value) : []

  return rows.map((row) => {
    const category = categoryFor(row.key, known)

    return {
      key: row.key ?? '',
      // A category the group has since removed keeps its expenses and loses its
      // name, which is the honest way round.
      name: category?.name ?? (row.key ? row.key : t('Not filed')),
      icon: resolveIcon(category?.iconName ?? null),
      colour: category?.colorHex ?? 'var(--text-muted)',
      amount: row.amount,
      expenseCount: row.expenseCount,
      share: largest > 0 ? row.amount / largest : 0,
    }
  })
})

/** What the figures above the rule are about: one group, or the lot. */
const scopeName = computed(() => {
  if (!groupId.value) return t('All groups')
  return groups.groups.find((group) => group.id === groupId.value)?.name ?? t('All groups')
})

/**
 * Each person's share of their own bucket, so the segments fill the bar whatever
 * the bar's height. A bucket the server sent without a breakdown falls back to one
 * whole segment rather than an empty bar.
 */
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

  // Lines drawn over the bars say nothing to anyone reading this by ear.
  for (const line of [...tracedLines.value].reverse()) {
    parts.unshift(
      `${line.name} ${formatMoney(amountIn(point, line.key), dashboard.value?.currency ?? 'CAD')}`,
    )
  }

  return parts.length > 0 ? `${when}: ${total} (${parts.join(', ')})` : `${when}: ${total}`
}

/**
 * A stack of coloured blocks says nothing to a screen reader without this.
 *
 * Only the buckets something happened in: reading out a hundred empty days is
 * worse than not reading the chart at all, and every bar is named in its own right
 * for anyone going through them one by one.
 */
const chartDescription = computed(() => {
  const busy = points.value.filter((point) => point.amount > 0)
  if (busy.length === 0) return 'Spending over time'

  return `Spending over time, by who paid: ${busy.map(bucketTitle).join('; ')}`
})

/**
 * The bar being asked about.
 *
 * A bar says how one stretch of time compares with the others and never says how
 * much, or when, or who. So it is asked: by hovering, tapping or focusing it, and
 * answered beside the heading and along the key underneath.
 *
 * Hovering and tapping are held apart for the same reason as on the pie: a click
 * always arrives after the pointer is already over the thing clicked, so treating
 * the two as one state made clicking a second bar read as clicking the one already
 * chosen, and it cleared instead of switching.
 */
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

/**
 * A tap, which is not a hover. The same one again puts the heading back, and that
 * has to clear the hover too, or on a phone the tap leaves behind a hover that
 * never ends and nothing appears to happen.
 */
function pin(bucket: string): void {
  pinnedBucket.value = pinnedBucket.value === bucket ? null : bucket
  hoveredBucket.value = pinnedBucket.value === null ? null : bucket
}

function forget(): void {
  hoveredBucket.value = null
  pinnedBucket.value = null
}

/** What one person paid in one bucket, and how much of it that was. */
function paidIn(point: SpendPoint, memberId: string): number {
  return point.byMember?.find((member) => member.memberId === memberId)?.amount ?? 0
}

function shareIn(point: SpendPoint, memberId: string): string {
  if (point.amount <= 0) return '0%'
  return `${Math.round((paidIn(point, memberId) / point.amount) * 100)}%`
}

/** Scaled against the largest bucket, so the bars are readable at any spend level. */
const peak = computed(() => Math.max(1, ...points.value.map((point) => point.amount)))

/**
 * Every category, traced across the same bars.
 *
 * Why lines on top rather than a chart of their own: a category is part of the
 * money the bar already draws, so on the same scale a line sits under the bar
 * tops and the gap between them reads as everything else. That answers the
 * question neither half of this screen could - "Where it went" gives one number
 * for the whole window and the bars give the months, and nothing said whether
 * groceries were creeping up while the total held steady.
 *
 * All of them, with nothing to switch on. A control would be one more thing to
 * find before the chart says anything, and the lines are told apart the way the
 * payers already are: by the key underneath, which names each one and says what
 * it came to in whichever bar is being asked about.
 */

/**
 * Whether the answer on screen can be traced at all.
 *
 * An older server answers without the per-bucket breakdown, and reading a missing
 * one as zero draws every line flat along the floor - which looks like a category
 * nobody spent anything on rather than an answer that never arrived. So the
 * control is not offered unless the data behind it is there.
 */
const canTrace = computed(() =>
  (dashboard.value?.spendOverTime ?? []).some((point) => point.byCategory !== undefined),
)

/** What one category came to in one bucket. Nothing in it is nothing, not a gap. */
function amountIn(point: SpendPoint, key: string): number {
  return (point.byCategory ?? []).find((row) => (row.key ?? '') === key)?.amount ?? 0
}

/**
 * One line, in the same box as the bars.
 *
 * Percentages against a stretched viewBox, so it lines up with flex columns
 * whatever the screen is; the stroke is kept off the stretch, or a narrow phone
 * would draw it as a smear. The x of a column is its middle, which the gaps
 * between bars put out by under a pixel and nothing can see.
 */
function lineFor(key: string): string {
  const amounts = points.value.map((point) => amountIn(point, key))
  const y = (amount: number) => 100 - Math.min(100, (amount / peak.value) * 100)

  // A single bucket has no line to draw between two points, so it gets a mark at
  // the right height instead of nothing at all.
  if (amounts.length === 1) return `25,${y(amounts[0])} 75,${y(amounts[0])}`

  return amounts
    .map((amount, index) => `${((index + 0.5) / amounts.length) * 100},${y(amount)}`)
    .join(' ')
}

/** Drawn in the order the breakdown ranks them, so the key reads top down. */
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

/** What one category was of one bucket, for the key under the chart. */
function shareOfCategory(point: SpendPoint, key: string): string {
  if (point.amount <= 0) return '0%'
  return `${Math.round((amountIn(point, key) / point.amount) * 100)}%`
}

/**
 * How much air between the bars.
 *
 * A daily chart of a quarter is a hundred bars, and 4px of gap between each of
 * them is more gap than chart. Sparse charts keep the gap they had.
 */
const chartGap = computed(() => {
  if (points.value.length > 40) return 'gap-px'
  if (points.value.length > 20) return 'gap-0.5'
  return 'gap-1'
})

/** What goes under a bar: a date, or the name of a month on its own. */
const bucketLabel = (bucket: string) => formatBucket(bucket, granularity.value)

/** What the bar covers, for whoever asks: a week is a stretch, not a date. */
const bucketRange = (bucket: string) => formatBucketRange(bucket, granularity.value)

/** Pulling down here means the numbers, and the queue behind them. */
const pull = useTemplateRef<{ done: () => void }>('pull')

async function refresh(): Promise<void> {
  try {
    await checkForAppUpdate()
    await expenses.sync()
  } catch {
    // Offline. The stats below are computed from this device anyway.
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

    <!--
      Renders nothing but a moment's confirmation: swiping across the screen moves
      to the next group, which is the navigation this app does most.
    -->
    <GroupSwipe />

    <!-- Pull down at the top to send what is queued and read the rest again. -->
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
      <!--
        Named, because there are two kinds of figure on this screen now: this half
        is the group, and the half under the rule is the person reading it. Without
        a name on each, two sets of "You paid" a screen apart read as a mistake.
      -->
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
        <!--
          The bar being asked about is answered here, where the eye already is for
          the heading, and along the key underneath. Nothing when nothing is asked:
          a line reporting the same total as the card above is furniture.
        -->
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
              <!--
              The whole column is the target, not just the bar: a daily chart of a
              busy month gives each bar a few pixels of width and none of its height
              until it is tall. Keyboard focus asks the same question a hover does.
            -->
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
                <!--
                A day with nothing in it is a line on the floor, not a small bar:
                a floor height in some colour would read as a small expense, and
                the whole point of drawing these is that they are empty.
              -->
                <span
                  v-if="point.amount <= 0"
                  data-testid="bar-empty"
                  class="block h-0.5 w-full rounded-full"
                  style="background: var(--border)"
                />

                <!--
                Stacked by whoever paid, in that person's colour. The total alone
                says how much a month cost; the split also says who carried it,
                which is the thing a shared account argues about.
              -->
                <span
                  v-else
                  data-testid="bar-fill"
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
              </button>
            </li>
          </ul>

          <!--
            The categories, over the bars and on their scale.

            Lifted off the bars by a shadow rather than outlined by a second
            stroke underneath, which is what an outline in the card's own colour
            looked like on a dark screen: a black border around every line. The
            shadow is a CSS filter and not an SVG one on purpose - a filter
            inside this viewBox would be stretched along with the coordinates and
            smear sideways on a narrow phone, where a CSS filter is applied after
            the stretch, in real pixels.

            It never takes the pointer: the bars underneath are the controls.
          -->
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

        <!-- Under the graph, against the bars they belong to, not under the names. -->
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

            <!-- What this person paid in the bar being asked about. -->
            <span v-if="selected" data-testid="key-amount" class="tabular-nums">
              {{ formatMoney(paidIn(selected, person.memberId), dashboard.currency) }}
              <span class="text-[var(--text-muted)]">
                {{ shareIn(selected, person.memberId) }}
              </span>
            </span>
          </li>
        </ul>

        <!--
          The lines, named. A dash rather than a dot, because these are lines over
          the bars and the dots above are the blocks inside them - two keys a row
          apart that looked alike would be read as one.
        -->
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

            <!-- What it went on in the bar being asked about. -->
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

    <!--
      Not "stats need a connection" any more: they are computed from this device.
      Nothing to compute means nothing to spend it on yet.
    -->
    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      {{ t('Nothing to add up yet. Add an expense and this fills in.') }}
    </p>

    <!--
      Under the group's own figures, because it is the other question: everything
      above is about one group, and this is about the person reading it, added up
      across every group they are in. Nothing answered that before - three groups
      meant three tabs and the arithmetic in your head.
    -->
    <AcrossGroups />
  </AppShell>
</template>
