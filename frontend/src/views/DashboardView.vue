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
import { summariseMonths } from '@/domain/monthSummary'
import MonthRecap from '@/components/groups/MonthRecap.vue'
import { memberColor } from '@/domain/memberColors'
import { formatMoney } from '@/domain/money'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { checkForAppUpdate } from '@/native/appUpdate'
import type { LocalExpense } from '@/offline/db'

/**
 * The group the app is on.
 *
 * One group at a time, because that is how the app is used: a list of every group
 * meant a tap before anything useful, and it duplicated the group screen
 * underneath. The others are reachable through the picker in the header, which
 * stays available with a single group since that is also how you reach making the
 * next one.
 */

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()
const auth = useAuthStore()
const expenses = useExpensesStore()

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()

  // Reached by a group's own URL, from a link or an activity entry: that group
  // becomes the one the app is on, so every other screen follows it too.
  const requested = route.params.groupId
  if (typeof requested === 'string' && requested) groups.setMainGroup(requested)

  await loadMainGroup()
})

/**
 * The list endpoint counts members without naming them, and everything here is by
 * person, so the group needs a detail read. Watched as well, since changing group
 * is the point of the control in the header.
 */
async function loadMainGroup(): Promise<void> {
  if (groups.mainGroupId) await groups.get(groups.mainGroupId)
}

watch(() => groups.mainGroupId, (id) => {
  void loadMainGroup()

  // Keeps the address honest when the group changes under a group's own URL,
  // however it changed: from the picker, from a swipe. Without this, reloading a
  // page reached at /groups/A after moving on to B lands back on A, because a
  // group's URL is what makes it the group the app is on.
  const named = route.params.groupId
  if (id && typeof named === 'string' && named && named !== id) {
    void router.replace({ name: 'group', params: { groupId: id } })
  }
})

const group = computed(() => groups.mainGroup)
const currency = computed(() => group.value?.baseCurrency ?? 'CAD')

/**
 * How many expenses are on screen.
 *
 * The replica already holds them all, so this is not about fetching: it is about
 * not building a thousand cards for a list nobody has scrolled to the bottom of.
 * A group that has been running a year is the normal case, not the exceptional
 * one, and every card carries its own colour, date and money formatting.
 */
const EXPENSE_PAGE = 20
const visibleCount = ref(EXPENSE_PAGE)

const groupExpenses = computed(() =>
  group.value ? expenses.forGroup(group.value.id) : [],
)

const colours = computed(() =>
  group.value ? groups.colorsOf(group.value.id) : {},
)

const colourOf = (memberId: string) => colours.value[memberId] ?? memberColor(memberId)

/**
 * Whether an expense is one of the names this group asked to leave out.
 *
 * The rent, in practice. It stays in the list, in the balances and in what
 * everybody owes - it is money that moved - but it is kept out of the totals this
 * screen states, because a household spends fifteen hundred before it has bought
 * anything and a total carrying that barely moves from month to month. Every
 * figure here reads from this one test, so no two of them can disagree.
 */
const isLeftOut = (expense: LocalExpense): boolean =>
  matchesAnyNamePattern(expense.description, group.value?.ignoredNamePatterns ?? [])

const memberName = (memberId: string) =>
  group.value?.members.find((member) => member.id === memberId)?.displayName ?? 'Someone'

/**
 * Who paid, on one line of a card.
 *
 * Two names when two people paid: an expense shared between them is not one person's
 * to be listed under, and the card is the only place a reader would notice. More
 * than two and the names would push the amount off a phone, so it counts them.
 */
function paidByLine(expense: LocalExpense): string {
  const payers = [...(expense.payers ?? [])].sort((left, right) => right.amount - left.amount)

  if (payers.length <= 1) return t('{name} paid', { name: memberName(expense.paidByMemberId) })

  const names = payers.map((payer) => memberName(payer.memberId))
  if (names.length === 2) return t('{first} and {second} paid', { first: names[0], second: names[1] })

  return t('{name} and {count} others paid', { name: names[0], count: names.length - 1 })
}

/**
 * The expenses by the month they were spent in.
 *
 * A dashboard is read from the top and the top is this month; everything before it
 * is history, and history is what a long list buries. Newest month first, and the
 * order inside each one is the order the list already had.
 */
interface ExpenseMonth {
  key: string
  label: string
  /** The everyday spending: the month less whatever the group leaves out. */
  total: number
  /** How many expenses that total covers, which the list states beside it. */
  count: number
  /** What was left out of it, so the heading can say so and the two add back up. */
  leftOut: { total: number; count: number } | null
  /** Every expense of the month, left-out ones included: the list shows them all. */
  expenses: LocalExpense[]
  /** The ones the total is made of, for anything else that sums the month. */
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

/**
 * Which month is open, and which one that is before anybody has said.
 *
 * Null rather than a set seeded with the answer: the default depends on the
 * expenses, which arrive after this screen does, and a set filled in once would
 * hold whatever was true at that moment. This way the default follows the data
 * until somebody taps, and then their choice takes over entirely.
 *
 * The current month, or the most recent one there is: a group whose last expense
 * was in June opens on June rather than on a screen of closed headings.
 */
const openMonths = ref<Set<string> | null>(null)

/**
 * The month somebody has just added to, named in the URL by the form that saved it.
 *
 * Without it the screen opens on the current month whatever was added, so an
 * expense dated in August is filed correctly and invisibly: the person is looking
 * at September, sees nothing, and concludes it did not save. What you just did has
 * to be on the screen you land on.
 */
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

  // Opening a month shows that month. The window is one count across every month
  // that is open, so a month opened underneath another one landed entirely behind
  // the "show more": August had thirty-two expenses and the ten oldest, which were
  // the ten somebody had just typed in, were on the next page. Tapping a heading
  // and finding nothing under it is indistinguishable from the expenses not being
  // there, and that is how a whole evening's entry looked lost.
  if (openMonths.value.has(key)) revealThrough(key)
}

/** Grows the window until every open month down to this one is rendered in full. */
function revealThrough(key: string): void {
  let needed = 0

  for (const month of expenseMonths.value) {
    if (!isMonthOpen(month.key)) continue
    needed += month.expenses.length
    if (month.key === key) break
  }

  visibleCount.value = Math.max(visibleCount.value, needed)
}

/**
 * The expense somebody has just added, named in the URL by the form that saved it.
 *
 * Opening its month is not enough on its own. A stack of old receipts lands at the
 * bottom of an old month, behind a page of expenses nobody scrolled, so the app
 * would open August and still show no sign of the thing that was just typed. This
 * grows the window until it is rendered, brings it into view, and marks it for a
 * few seconds so the eye has somewhere to land.
 */
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

    // Asked for rather than assumed: this runs under a DOM that has no scrolling
    // of its own in the tests, and a missing method here would throw inside a
    // watcher, where nothing would report it.
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

/** The slice on screen, and whether there is more behind it. */
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

/**
 * Loads the next page when the foot of the list comes into view.
 *
 * Ahead of the edge on purpose: the margin means the next cards are already there
 * by the time the last one is read, so scrolling never stops on a spinner.
 */
const sentinel = useTemplateRef<HTMLElement>('sentinel')
let observer: IntersectionObserver | null = null

watch(sentinel, (element) => {
  observer?.disconnect()
  observer = null

  // Absent in older browsers and in a test environment with no layout. The list
  // still works; it just shows the first page.
  if (!element || typeof IntersectionObserver === 'undefined') return

  observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting)) showMoreExpenses()
    },
    // Against the page rather than the window, because the page is what scrolls.
    // Watching the window instead, the margin below would buy nothing: the foot of
    // the list is clipped by the page long before the window has an opinion, so
    // the next cards would only be built once the last one was already read.
    { root: element.closest('[data-app-page]'), rootMargin: '400px' },
  )
  observer.observe(element)
})

// Back to the first page, and back to the default month: both belong to the list
// being read, and the next group's list is a different list.
watch(() => group.value?.id, () => {
  visibleCount.value = EXPENSE_PAGE
  openMonths.value = null
})

onUnmounted(() => observer?.disconnect())

/**
 * What the group has spent, however it was settled: the sum of the month totals
 * under it, which means the same names left out of it as out of each of those.
 */
const groupTotal = computed(() =>
  groupExpenses.value
    .filter((expense) => !isLeftOut(expense))
    .reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
)

/**
 * Which total is showing what it leaves out.
 *
 * A star rather than a second figure on every heading: the everyday total is the
 * one being read, and a column of "+ 1,500" beside a column of totals is a screen
 * of arithmetic homework. The star says there is more to this number, and asking -
 * by pointing at it, or tapping on a phone, where pointing does not exist - says
 * what the month really came to.
 *
 * Hover and tap are held apart, as they are on the pie: a tap arrives after the
 * pointer is already over the thing tapped, so treating them as one state makes
 * tapping a second total read as tapping the one already open.
 *
 * Which leaves the third state, and it is the one that made a press do nothing: a
 * second click closes it, but on a mouse the pointer is still sitting on the thing
 * that was just closed, and hover opened it straight back up. So closing it also
 * says "not while the pointer stays here", and moving away clears that - because
 * arriving again is a fresh question rather than the same one.
 */
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

/**
 * The names doing the leaving out, so the figure can say whose doing it is.
 *
 * "3,957.88 in all" answers how much and leaves why hanging; "excluding Loyer,
 * Rent" is the half that tells somebody where to go and change it.
 */
const leftOutNames = computed(() => (group.value?.ignoredNamePatterns ?? []).join(', '))

/** What the month really came to: the everyday total plus what it leaves out. */
const inAll = (total: number, leftOut: { total: number } | null) =>
  total + (leftOut?.total ?? 0)

/** And what that leaves out, said beside it rather than quietly dropped. */
const groupLeftOut = computed(() => {
  const skipped = groupExpenses.value.filter(isLeftOut)
  if (skipped.length === 0) return null

  return {
    total: skipped.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
    count: skipped.length,
  }
})

/**
 * Which row in the balances is the person reading it.
 *
 * The card that stated your own balance is gone, so the list has to say which of
 * these numbers is yours. By membership rather than by name: a group can hold two
 * people with the same one.
 */
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

/**
 * This month's spending, by whoever paid it.
 *
 * The month rather than all time, which is a figure that only grows and says
 * nothing about now: a group two years old looked exactly as busy as one that
 * started last week. What is owed is a different question, and the balances below
 * answer it.
 *
 * Every payer counts for what they put in, so an expense two people paid for shows
 * up in both their slices rather than all under whoever's name is on it.
 */
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

/** The month the chart is about, which is this one whether or not it has anything in it. */
const currentMonth = computed(() => bucketOf(new Date(), 'month'))

/** What this month's chart leaves out, which it says underneath itself. */
const currentMonthLeftOut = computed(
  () => expenseMonths.value.find((entry) => entry.key === currentMonth.value)?.leftOut ?? null,
)

/**
 * How each finished month went, by month.
 *
 * Only complete months: the one running has a total on its heading like the rest,
 * and nothing useful to compare it with until it ends.
 */
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

/**
 * Who should pay whom.
 *
 * Simplified by default: the fewest transfers that clear the group is what people
 * actually want to act on. The raw list is a tap away for anyone who wants to see
 * the debts as they were incurred rather than netted.
 */
const showSimplified = ref(true)

const plan = computed(() => {
  if (!group.value) return []
  return showSimplified.value
    ? expenses.settleUpPlan(group.value.id)
    : expenses.rawDebts(group.value.id)
})

/**
 * The payer's colour, as the card itself.
 *
 * Mixed with the surface rather than used raw: a full-strength colour behind the
 * text would be unreadable, and mixing with the token keeps it right in both
 * themes without a second palette. The left edge takes the colour undiluted, which
 * is what makes two adjacent cards by different people obvious at a glance.
 */
function cardStyle(memberId: string) {
  const colour = colourOf(memberId)

  return {
    backgroundColor: `color-mix(in oklab, ${colour} 16%, var(--surface-raised))`,
    borderColor: `color-mix(in oklab, ${colour} 35%, transparent)`,
    borderLeftColor: colour,
  }
}

const spentOn = (iso: string) =>
  new Date(iso).toLocaleDateString(intlLocale.value, { day: 'numeric', month: 'short' })

/**
 * What pulling down means here: send what is queued, pull what is new, and read the
 * group again. The same work the app does on its own when it comes back online.
 */
const pull = useTemplateRef<{ done: () => void }>('pull')

async function refresh(): Promise<void> {
  try {
    // Pulling down means "get me the latest", which includes the app itself.
    await checkForAppUpdate()
    await expenses.sync()
    await groups.loadAll()
    await loadMainGroup()
  } catch {
    // Offline, most likely. The indicator going away is the answer either way.
  } finally {
    pull.value?.done()
  }
}
</script>

<template>
  <AppShell
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

    <!--
      Renders nothing but a moment's confirmation: swiping across the screen moves
      to the next group, which is the navigation this app does most.
    -->
    <GroupSwipe />

    <!-- Pull down at the top to send what is queued and read the rest again. -->
    <PullToRefresh ref="pull" @refresh="refresh" />

    <template v-if="group">
      <!-- The shape of the group's spending, first: it is what the screen is for. -->
      <section class="surface-card mb-4 p-4">
        <SpendPie :slices="monthSpending" :currency="currency">
          <template #heading>{{ formatMonthHeading(currentMonth) }}</template>
          <template #empty>{{ t('Nothing spent this month yet.') }}</template>
        </SpendPie>

        <!--
          The chart counts the same expenses the month heading below it does, so it
          has to say the same thing about what it leaves out. A slice that quietly
          held the rent and a total that quietly did not would make a liar of one
          of them.
        -->
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

      <section v-if="balances.length > 0" class="surface-card mb-4 p-4">
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm text-[var(--text-muted)]">{{ t('Balances') }}</p>

          <!--
            Here rather than in a card of its own. A card holding one number and
            one button was a row of furniture above the list that says the same
            thing about everybody, including you.
          -->
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
            <!-- Beside the list it switches, rather than above the one it does not. -->
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

      <section>
        <!--
          The total sits beside the heading rather than in the pie, which now
          answers a different question. It belongs to the list underneath it: the
          sum of every month total there, not only the ones scrolled into view -
          which means it leaves out what they leave out, and says so underneath.
        -->
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

        <!--
          One section per month, closed but for the current one. A group that has
          been running a year is mostly history: the headings keep it reachable in
          a screen's worth of space, and a closed month builds none of its cards.
        -->
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

              <!--
              The total keeps to itself rather than living inside the heading
              button: once it answers a question of its own, it cannot also be
              part of the control that opens the month.
            -->
              <!--
                The pointer is on this, not on the figure: what the star reveals
                appears below it, and a zone that ended at the figure meant the
                line pushed the pointer off the very thing keeping it on screen -
                it appeared, the pointer left, it went, the pointer returned. A
                flicker, sixty times a second.
              -->
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

                <!--
                  What the month really came to, on asking. Floated rather than
                  inserted: a line that takes up room shuffles every heading below
                  it down the screen while somebody is reading them.
                -->
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

        <!--
          The foot of the list. Watched rather than tapped, so scrolling carries on
          by itself, and a button as well for anything that cannot watch.
        -->
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
