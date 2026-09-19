<script setup lang="ts">
import { t, intlLocale } from '@/i18n'
import { computed, nextTick, onMounted, onUnmounted, ref, useTemplateRef, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import PullToRefresh from '@/components/ui/PullToRefresh.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SpendPie from '@/components/ui/SpendPie.vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faChevronRight } from '@fortawesome/free-solid-svg-icons'
import { bucketOf, formatMonthHeading } from '@/domain/buckets'
import { matchesAnyNamePattern } from '@/domain/namePatterns'
import { categoryFor } from '@/domain/categories'
import { resolveIcon } from '@/domain/icons'
import { summariseMonths } from '@/domain/monthSummary'
import MonthRecap from '@/components/groups/MonthRecap.vue'
import { memberColor } from '@/domain/memberColors'
import { formatMoney } from '@/domain/money'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { checkForAppUpdate } from '@/native/appUpdate'
import type { LocalExpense } from '@/offline/db'

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()
const auth = useAuthStore()
const expenses = useExpensesStore()

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()

  const requested = route.params.groupId
  if (typeof requested === 'string' && requested) groups.setMainGroup(requested)

  await loadMainGroup()
})

async function loadMainGroup(): Promise<void> {
  if (groups.mainGroupId) await groups.get(groups.mainGroupId)
}

watch(() => groups.mainGroupId, (id) => {
  void loadMainGroup()

  const named = route.params.groupId
  if (id && typeof named === 'string' && named && named !== id) {
    void router.replace({ name: 'group', params: { groupId: id } })
  }
})

const group = computed(() => groups.mainGroup)
const currency = computed(() => group.value?.baseCurrency ?? 'CAD')

const EXPENSE_PAGE = 20
const visibleCount = ref(EXPENSE_PAGE)

const groupExpenses = computed(() =>
  group.value ? expenses.forGroup(group.value.id) : [],
)

const colours = computed(() =>
  group.value ? groups.colorsOf(group.value.id) : {},
)

const colourOf = (memberId: string) => colours.value[memberId] ?? memberColor(memberId)

const isLeftOut = (expense: LocalExpense): boolean =>
  matchesAnyNamePattern(expense.description, group.value?.ignoredNamePatterns ?? [])

const memberName = (memberId: string) =>
  group.value?.members.find((member) => member.id === memberId)?.displayName ?? 'Someone'

function paidByLine(expense: LocalExpense): string {
  const payers = [...(expense.payers ?? [])].sort((left, right) => right.amount - left.amount)

  if (payers.length <= 1) return t('{name} paid', { name: memberName(expense.paidByMemberId) })

  const names = payers.map((payer) => memberName(payer.memberId))
  if (names.length === 2) return t('{first} and {second} paid', { first: names[0], second: names[1] })

  return t('{name} and {count} others paid', { name: names[0], count: names.length - 1 })
}

interface ExpenseMonth {
  key: string
  label: string
  total: number
  count: number
  leftOut: { total: number; count: number } | null
  expenses: LocalExpense[]
  everyday: LocalExpense[]
}

const expenseMonths = computed<ExpenseMonth[]>(() => {
  const byMonth = new Map<string, LocalExpense[]>()

  for (const expense of groupExpenses.value) {
    const key = bucketOf(expense.spentAt, 'month')
    const found = byMonth.get(key)
    if (found) found.push(expense)
    else byMonth.set(key, [expense])
  }

  return [...byMonth].map(([key, list]) => {
    const everyday = list.filter((expense) => !isLeftOut(expense))
    const skipped = list.filter((expense) => isLeftOut(expense))

    return {
      key,
      label: formatMonthHeading(key),
      total: everyday.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
      count: everyday.length,
      leftOut: skipped.length === 0
        ? null
        : {
            total: skipped.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
            count: skipped.length,
          },
      expenses: list,
      everyday,
    }
  })
})

const openMonths = ref<Set<string> | null>(null)

const addedMonth = computed(() => {
  const asked = route.query.month
  return typeof asked === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(asked) ? asked : null
})

const defaultMonth = computed(() => {
  const thisMonth = bucketOf(new Date(), 'month')
  const months = expenseMonths.value.map((month) => month.key)

  if (addedMonth.value && months.includes(addedMonth.value)) return addedMonth.value

  return months.includes(thisMonth) ? thisMonth : (months[0] ?? null)
})

const isMonthOpen = (key: string): boolean =>
  openMonths.value ? openMonths.value.has(key) : key === defaultMonth.value

function toggleMonth(key: string): void {
  const open = new Set(openMonths.value ?? (defaultMonth.value ? [defaultMonth.value] : []))

  if (open.has(key)) open.delete(key)
  else open.add(key)

  openMonths.value = open

  if (openMonths.value.has(key)) revealThrough(key)
}

function revealThrough(key: string): void {
  let needed = 0

  for (const month of expenseMonths.value) {
    if (!isMonthOpen(month.key)) continue
    needed += month.expenses.length
    if (month.key === key) break
  }

  visibleCount.value = Math.max(visibleCount.value, needed)
}

const addedExpenseId = computed(() => {
  const asked = route.query.added
  return typeof asked === 'string' && asked ? asked : null
})

const justAdded = ref<string | null>(null)
let clearHighlight: ReturnType<typeof setTimeout> | null = null

watch(
  [addedExpenseId, expenseMonths],
  async () => {
    const id = addedExpenseId.value
    if (!id) return

    const month = expenseMonths.value.find((entry) =>
      entry.expenses.some((expense) => expense.id === id),
    )
    if (!month) return

    revealThrough(month.key)
    justAdded.value = id

    await nextTick()

    const row = document.querySelector(`[data-expense-id="${id}"]`)
    if (row instanceof HTMLElement && typeof row.scrollIntoView === 'function') {
      row.scrollIntoView({ block: 'center', behavior: 'smooth' })
    }

    if (clearHighlight) clearTimeout(clearHighlight)
    clearHighlight = setTimeout(() => {
      justAdded.value = null
    }, 4000)
  },
  { immediate: true },
)

onUnmounted(() => {
  if (clearHighlight) clearTimeout(clearHighlight)
})

const openExpenses = computed(() =>
  expenseMonths.value.filter((month) => isMonthOpen(month.key)).flatMap((month) => month.expenses),
)

const visibleIds = computed(
  () => new Set(openExpenses.value.slice(0, visibleCount.value).map((expense) => expense.id)),
)

const remainingExpenses = computed(() => openExpenses.value.length - visibleIds.value.size)
const hasMoreExpenses = computed(() => remainingExpenses.value > 0)

function showMoreExpenses(): void {
  if (!hasMoreExpenses.value) return
  visibleCount.value += EXPENSE_PAGE
}

const sentinel = useTemplateRef<HTMLElement>('sentinel')
let observer: IntersectionObserver | null = null

watch(sentinel, (element) => {
  observer?.disconnect()
  observer = null

  if (!element || typeof IntersectionObserver === 'undefined') return

  observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting)) showMoreExpenses()
    },
    { root: element.closest('[data-app-page]'), rootMargin: '400px' },
  )
  observer.observe(element)
})

watch(() => group.value?.id, () => {
  visibleCount.value = EXPENSE_PAGE
  openMonths.value = null
})

onUnmounted(() => observer?.disconnect())

const groupTotal = computed(() =>
  groupExpenses.value
    .filter((expense) => !isLeftOut(expense))
    .reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
)

const hoveredTotal = ref<string | null>(null)
const pinnedTotal = ref<string | null>(null)
const closedUnderPointer = ref<string | null>(null)

const isRevealed = (key: string) =>
  pinnedTotal.value === key ||
  (hoveredTotal.value === key && closedUnderPointer.value !== key)

function pointAt(key: string): void {
  hoveredTotal.value = key
  closedUnderPointer.value = null
}

function pointAway(): void {
  hoveredTotal.value = null
  closedUnderPointer.value = null
}

function toggleTotal(key: string): void {
  if (pinnedTotal.value === key) {
    pinnedTotal.value = null
    closedUnderPointer.value = key
    return
  }

  pinnedTotal.value = key
  closedUnderPointer.value = null
}

const leftOutNames = computed(() => (group.value?.ignoredNamePatterns ?? []).join(', '))

const inAll = (total: number, leftOut: { total: number } | null) =>
  total + (leftOut?.total ?? 0)

const groupLeftOut = computed(() => {
  const skipped = groupExpenses.value.filter(isLeftOut)
  if (skipped.length === 0) return null

  return {
    total: skipped.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
    count: skipped.length,
  }
})

const myMemberId = computed(() =>
  group.value && auth.user ? groups.myMemberId(group.value.id, auth.user.id) : null,
)

const balances = computed(() => {
  if (!group.value) return []

  const byMember = new Map(expenses.balanceFor(group.value.id).map((b) => [b.memberId, b.net]))

  return group.value.members
    .filter((member) => member.status === 'Active')
    .map((member) => ({
      id: member.id,
      name: member.displayName,
      net: byMember.get(member.id) ?? 0,
      colour: colourOf(member.id),
    }))
})

const monthSpending = computed(() => {
  const month = expenseMonths.value.find((entry) => entry.key === currentMonth.value)
  if (!month || !group.value) return []

  const paid = new Map<string, number>()
  for (const expense of month.everyday) {
    const payers =
      expense.payers && expense.payers.length > 0
        ? expense.payers
        : [{ memberId: expense.paidByMemberId, amountInBaseCurrency: expense.amountInBaseCurrency }]

    for (const payer of payers) {
      paid.set(payer.memberId, (paid.get(payer.memberId) ?? 0) + payer.amountInBaseCurrency)
    }
  }

  return [...paid]
    .map(([memberId, amount]) => ({
      id: memberId,
      label: memberName(memberId),
      amount,
      colorHex: colourOf(memberId),
    }))
    .sort((left, right) => right.amount - left.amount)
})

const currentMonth = computed(() => bucketOf(new Date(), 'month'))

const currentMonthLeftOut = computed(
  () => expenseMonths.value.find((entry) => entry.key === currentMonth.value)?.leftOut ?? null,
)

const recaps = computed(() => {
  const summaries = summariseMonths(
    groupExpenses.value.map((expense) => ({
      key: bucketOf(expense.spentAt, 'month'),
      amountInBaseCurrency: expense.amountInBaseCurrency,
      description: expense.description,
      payers:
        expense.payers && expense.payers.length > 0
          ? expense.payers
          : [
              {
                memberId: expense.paidByMemberId,
                amountInBaseCurrency: expense.amountInBaseCurrency,
              },
            ],
    })),
    currency.value,
    new Date(),
    group.value?.ignoredNamePatterns ?? [],
  )

  return new Map(summaries.map((summary) => [summary.key, summary]))
})

const showSimplified = ref(true)

const plan = computed(() => {
  if (!group.value) return []
  return showSimplified.value
    ? expenses.settleUpPlan(group.value.id)
    : expenses.rawDebts(group.value.id)
})

function cardStyle(memberId: string) {
  const colour = colourOf(memberId)

  return {
    backgroundColor: `color-mix(in oklab, ${colour} 16%, var(--surface-raised))`,
    borderColor: `color-mix(in oklab, ${colour} 35%, transparent)`,
    borderLeftColor: colour,
  }
}

const categoryOf = (expense: LocalExpense) =>
  categoryFor(expense.categoryKey, group.value ? groups.categoriesOf(group.value.id) : [])

const spentOn = (iso: string) =>
  new Date(iso).toLocaleDateString(intlLocale.value, { day: 'numeric', month: 'short' })

const pull = useTemplateRef<{ done: () => void }>('pull')

async function refresh(): Promise<void> {
  try {
    await checkForAppUpdate()
    await expenses.sync()
    await groups.loadAll()
    await loadMainGroup()
  } catch {
  } finally {
    pull.value?.done()
  }
}
</script>
<template>
  <AppShell
    width="wide"
    :title="group?.name ?? 'Dashboard'"
    :subtitle="group ? t('Dashboard') : undefined"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="groups.isOffline"
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
    <template v-if="group">
      <div class="lg:grid lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start lg:gap-6">
        <aside
          class="lg:sticky lg:top-4 lg:col-start-2 lg:row-start-1 lg:flex lg:max-h-[calc(100dvh-2rem)] lg:flex-col lg:gap-4 lg:self-start lg:overflow-y-auto"
        >
          <section class="surface-card mb-4 p-4 lg:mb-0">
            <SpendPie :slices="monthSpending" :currency="currency">
              <template #heading>{{ formatMonthHeading(currentMonth) }}</template>
              <template #empty>{{ t('Nothing spent this month yet.') }}</template>
            </SpendPie>
            <p
              v-if="currentMonthLeftOut"
              data-testid="pie-left-out"
              class="mt-2 text-center text-xs text-[var(--text-muted)]"
            >
              {{ t('+ {amount} left out', {
                amount: formatMoney(currentMonthLeftOut.total, currency),
              }) }}
            </p>
          </section>
          <section v-if="balances.length > 0" class="surface-card mb-4 p-4 lg:mb-0">
            <div class="flex items-center justify-between gap-2">
              <p class="text-sm text-[var(--text-muted)]">{{ t('Balances') }}</p>
              <RouterLink
                :to="{ name: 'settle', params: { groupId: group.id } }"
                data-testid="settle-up"
                class="btn btn-press btn-secondary min-h-0 shrink-0 px-3 py-1.5 text-xs"
                style="border-color: var(--border)"
              >{{ t('Settle up') }}
              </RouterLink>
            </div>
            <ul class="mt-3 flex flex-col gap-2 text-sm">
              <li
                v-for="member in balances"
                :key="member.id"
                class="flex items-center justify-between gap-2"
              >
                <span class="flex min-w-0 items-center gap-2">
                  <span
                    class="h-2.5 w-2.5 shrink-0 rounded-full"
                    :style="{ backgroundColor: member.colour }"
                    aria-hidden="true"
                  />
                  <span class="truncate">{{ member.name }}</span>
                  <span
                    v-if="member.id === myMemberId"
                    data-testid="your-balance"
                    class="shrink-0 rounded-full px-1.5 py-0.5 text-[0.65rem] font-semibold uppercase tracking-wide"
                    style="background: var(--surface-sunken); color: var(--text-muted)"
                  >{{ t('You') }}
                  </span>
                </span>
                <MoneyAmount :amount="member.net" :currency="currency" signed size="sm" />
              </li>
            </ul>
            <p v-if="plan.length === 0" class="mt-3 text-sm text-[var(--text-muted)]">{{ t('Everyone is settled up.') }}
            </p>
            <div v-else class="mt-4 border-t pt-3" style="border-color: var(--border)">
              <div class="flex items-center justify-between gap-2">
                <h3 class="min-w-0 text-sm font-medium text-[var(--text-muted)]">
                  {{ showSimplified
                    ? plan.length === 1
                      ? t('Settle up in 1 transfer')
                      : t('Settle up in {count} transfers', { count: plan.length })
                    : t('Who owes whom') }}
                </h3>
                <button
                  type="button"
                  data-testid="toggle-simplify"
                  class="btn btn-press btn-quiet min-h-0 shrink-0 px-2 py-1 text-xs text-accent"
                  @click="showSimplified = !showSimplified"
                >
                  {{ showSimplified ? t('Show who owes whom') : t('Simplify') }}
                </button>
              </div>
              <ul class="mt-2 flex flex-col gap-2 text-sm">
                <li
                  v-for="transfer in plan"
                  :key="`${transfer.fromMemberId}-${transfer.toMemberId}`"
                  class="flex items-center justify-between gap-2"
                >
                  <span class="flex min-w-0 items-center gap-2 truncate">
                    <span
                      class="h-2 w-2 shrink-0 rounded-full"
                      :style="{ backgroundColor: colourOf(transfer.fromMemberId) }"
                      aria-hidden="true"
                    />
                    <span class="truncate">
                      {{ memberName(transfer.fromMemberId) }} pays
                      {{ memberName(transfer.toMemberId) }}
                    </span>
                  </span>
                  <RouterLink
                    :to="{
                      name: 'settle',
                      params: { groupId: group.id },
                      query: {
                        from: transfer.fromMemberId,
                        to: transfer.toMemberId,
                        amount: transfer.amount.toFixed(2),
                      },
                    }"
                    class="btn btn-press btn-secondary min-h-0 shrink-0 px-2 py-1 text-xs"
                  >
                    {{ formatMoney(transfer.amount, currency) }}
                  </RouterLink>
                </li>
              </ul>
            </div>
          </section>
        </aside>
        <section class="lg:col-start-1 lg:row-start-1">
          <div class="mb-2 flex items-baseline justify-between gap-3">
            <h2 class="text-sm font-medium text-[var(--text-muted)]">{{ t('Expenses') }}</h2>
            <p
              v-if="groupExpenses.length > 0"
              class="relative shrink-0 text-right"
              data-testid="group-total-zone"
              @mouseenter="pointAt('group')"
              @mouseleave="pointAway"
            >
              <button
                v-if="groupLeftOut"
                type="button"
                data-testid="group-total"
                class="block text-sm font-medium tabular-nums"
                :aria-label="t('{amount} in all, with what is left out', {
                  amount: formatMoney(inAll(groupTotal, groupLeftOut), currency),
                })"
                @click="toggleTotal('group')"
              >
                {{ formatMoney(groupTotal, currency) }}<span
                  class="text-accent"
                  aria-hidden="true"
                >*</span>
              </button>
              <span v-else data-testid="group-total" class="block text-sm font-medium tabular-nums">
                {{ formatMoney(groupTotal, currency) }}
              </span>
              <span
                v-if="groupLeftOut && isRevealed('group')"
                data-testid="group-left-out"
                class="absolute top-full right-0 z-10 mt-0.5 max-w-52 rounded-md border px-2 py-1 text-right text-[0.7rem] tabular-nums shadow-sm"
                style="background: var(--surface-raised); border-color: var(--border)"
              >
                <span class="block whitespace-nowrap">
                  {{ t('{amount} in all', {
                    amount: formatMoney(inAll(groupTotal, groupLeftOut), currency),
                  }) }}
                </span>
                <span v-if="leftOutNames" class="block text-[var(--text-muted)]">
                  {{ t('excluding {names}', { names: leftOutNames }) }}
                </span>
              </span>
            </p>
          </div>
          <ul v-if="expenseMonths.length > 0" class="flex flex-col gap-3">
            <li v-for="month in expenseMonths" :key="month.key">
              <div class="flex w-full items-center gap-2">
                <button
                  type="button"
                  data-testid="month-toggle"
                  class="flex min-w-0 flex-1 items-center gap-2 rounded-lg px-1 py-1.5 text-left"
                  :aria-expanded="isMonthOpen(month.key)"
                  @click="toggleMonth(month.key)"
                >
                  <FontAwesomeIcon
                    :icon="faChevronRight"
                    class="h-3 w-3 shrink-0 text-[var(--text-muted)] transition-transform"
                    :class="isMonthOpen(month.key) ? 'rotate-90' : ''"
                    aria-hidden="true"
                  />
                  <span class="min-w-0 flex-1 truncate text-sm font-medium">{{ month.label }}</span>
                  <span class="shrink-0 text-xs text-[var(--text-muted)]">{{ month.count }}</span>
                </button>
                <span
                  class="relative shrink-0 pr-1 text-right"
                  data-testid="month-total-zone"
                  @mouseenter="pointAt(month.key)"
                  @mouseleave="pointAway"
                >
                  <button
                    v-if="month.leftOut"
                    type="button"
                    data-testid="month-total"
                    class="block text-sm tabular-nums text-[var(--text-muted)]"
                    :aria-label="t('{amount} in all, with what is left out', {
                      amount: formatMoney(inAll(month.total, month.leftOut), currency),
                    })"
                    @click="toggleTotal(month.key)"
                  >
                    {{ formatMoney(month.total, currency) }}<span
                      class="text-accent"
                      aria-hidden="true"
                    >*</span>
                  </button>
                  <span
                    v-else
                    data-testid="month-total"
                    class="block text-sm tabular-nums text-[var(--text-muted)]"
                  >
                    {{ formatMoney(month.total, currency) }}
                  </span>
                  <span
                    v-if="month.leftOut && isRevealed(month.key)"
                    data-testid="month-left-out"
                    class="absolute top-full right-1 z-10 mt-0.5 max-w-52 rounded-md border px-2 py-1 text-right text-[0.7rem] tabular-nums shadow-sm"
                    style="background: var(--surface-raised); border-color: var(--border)"
                  >
                    <span class="block whitespace-nowrap">
                      {{ t('{amount} in all', {
                        amount: formatMoney(inAll(month.total, month.leftOut), currency),
                      }) }}
                    </span>
                    <span v-if="leftOutNames" class="block text-[var(--text-muted)]">
                      {{ t('excluding {names}', { names: leftOutNames }) }}
                    </span>
                  </span>
                </span>
              </div>
              <MonthRecap
                v-if="isMonthOpen(month.key) && recaps.get(month.key)"
                :summary="recaps.get(month.key)!"
                :currency="currency"
                :name-of="memberName"
              />
              <ul v-if="isMonthOpen(month.key)" class="mt-2 flex flex-col gap-2">
                <template v-for="expense in month.expenses" :key="expense.id">
                  <li v-if="visibleIds.has(expense.id)">
                    <RouterLink
                      :to="{ name: 'expense', params: { groupId: group.id, expenseId: expense.id } }"
                      data-testid="expense-card"
                      :data-expense-id="expense.id"
                      class="tap-target flex items-center justify-between gap-3 rounded-xl border border-l-4 p-3"
                      :class="justAdded === expense.id ? 'ring-2' : ''"
                      :style="{
                        ...cardStyle(expense.paidByMemberId),
                        ...(justAdded === expense.id ? { '--tw-ring-color': 'var(--accent-text)' } : {}),
                      }"
                    >
                      <span class="min-w-0">
                        <span class="flex items-center gap-2">
                          <span class="truncate font-medium">{{ expense.description }}</span>
                          <span
                            v-if="expense.pending"
                            class="shrink-0 rounded-full bg-brand-600/20 px-1.5 py-0.5 text-[10px] text-accent"
                            :title="t('Saved on this device, waiting to sync')"
                          >{{ t('Waiting') }}
                          </span>
                        </span>
                        <span class="truncate text-xs text-[var(--text-muted)]">
                          <FontAwesomeIcon
                            v-if="categoryOf(expense)"
                            :icon="resolveIcon(categoryOf(expense)!.iconName).definition"
                            data-testid="expense-category"
                            :data-category="expense.categoryKey"
                            class="mr-1 h-3 w-3"
                            :style="{ color: categoryOf(expense)!.colorHex }"
                            :title="categoryOf(expense)!.name"
                          />
                          {{ paidByLine(expense) }}
                          <span aria-hidden="true">-</span>
                          {{ spentOn(expense.spentAt) }}
                        </span>
                      </span>
                      <MoneyAmount :amount="expense.amount" :currency="expense.currency" size="sm" />
                    </RouterLink>
                  </li>
                </template>
              </ul>
            </li>
          </ul>
          <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">{{ t('No expenses yet. Add the first one with the button below.') }}
          </p>
          <div
            v-if="hasMoreExpenses"
            ref="sentinel"
            data-testid="expenses-sentinel"
            class="flex flex-col items-center gap-2 py-4"
          >
            <button
              type="button"
              data-testid="show-more-expenses"
              class="btn btn-press btn-quiet text-xs"
              @click="showMoreExpenses"
            >
              Show more ({{ remainingExpenses }} left)
            </button>
          </div>
        </section>
      </div>
    </template>
    <p v-else-if="groups.isLoading" class="py-12 text-center text-[var(--text-muted)]">{{ t('Loading your groups') }}
    </p>
    <div v-else class="surface-card p-6 text-center">
      <p class="font-medium">{{ t('No groups yet') }}</p>
      <p class="mt-1 text-sm text-[var(--text-muted)]">{{ t('Create one for the people you share costs with, or open an invite someone sent you.') }}
      </p>
      <RouterLink :to="{ name: 'new-group' }" class="btn btn-press btn-primary mt-4">{{ t('New group') }}
      </RouterLink>
    </div>
  </AppShell>
</template>
