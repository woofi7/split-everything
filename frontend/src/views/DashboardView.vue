<script setup lang="ts">
import { t, intlLocale } from '@/i18n'
import { computed, onMounted, onUnmounted, ref, useTemplateRef, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupMark from '@/components/groups/GroupMark.vue'
import GroupSettingsButton from '@/components/groups/GroupSettingsButton.vue'
import GroupSwipe from '@/components/groups/GroupSwipe.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SpendPie from '@/components/ui/SpendPie.vue'
import { memberColor } from '@/domain/memberColors'
import { formatMoney } from '@/domain/money'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'

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

const memberName = (memberId: string) =>
  group.value?.members.find((member) => member.id === memberId)?.displayName ?? 'Someone'

/**
 * Who paid, not who owes. That is what spending means to whoever handed over the
 * card, and the balances below already say who owes what.
 */
/** The slice on screen, and whether there is more behind it. */
const visibleExpenses = computed(() => groupExpenses.value.slice(0, visibleCount.value))
const hasMoreExpenses = computed(() => groupExpenses.value.length > visibleCount.value)

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

// Back to the first page: the count belongs to the list being read, and the next
// group's list is a different list.
watch(() => group.value?.id, () => {
  visibleCount.value = EXPENSE_PAGE
})

onUnmounted(() => observer?.disconnect())

const paidByMember = computed(() => {
  if (!group.value) return []

  const paid = new Map<string, number>()
  for (const expense of groupExpenses.value) {
    paid.set(
      expense.paidByMemberId,
      (paid.get(expense.paidByMemberId) ?? 0) + expense.amountInBaseCurrency,
    )
  }

  return group.value.members
    .map((member) => ({
      id: member.id,
      label: member.displayName,
      amount: paid.get(member.id) ?? 0,
      colorHex: colourOf(member.id),
    }))
    .sort((left, right) => right.amount - left.amount)
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

    <template v-if="group">
      <!-- The shape of the group's spending, first: it is what the screen is for. -->
      <section class="surface-card mb-4 p-4">
        <SpendPie :slices="paidByMember" :currency="currency">
          <template #heading>{{ t('Who paid') }}</template>
        </SpendPie>
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
              class="btn btn-press btn-quiet min-h-0 shrink-0 px-2 py-1 text-xs text-brand-400"
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
        <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Expenses') }}</h2>

        <ul v-if="groupExpenses.length > 0" class="flex flex-col gap-2">
          <li v-for="expense in visibleExpenses" :key="expense.id">
            <RouterLink
              :to="{ name: 'expense', params: { groupId: group.id, expenseId: expense.id } }"
              data-testid="expense-card"
              class="tap-target flex items-center justify-between gap-3 rounded-xl border border-l-4 p-3"
              :style="cardStyle(expense.paidByMemberId)"
            >
              <span class="min-w-0">
                <span class="flex items-center gap-2">
                  <span class="truncate font-medium">{{ expense.description }}</span>
                  <span
                    v-if="expense.pending"
                    class="shrink-0 rounded-full bg-brand-600/20 px-1.5 py-0.5 text-[10px] text-brand-400"
                    :title="t('Saved on this device, waiting to sync')"
                  >{{ t('Waiting') }}
                  </span>
                </span>
                <span class="truncate text-xs text-[var(--text-muted)]">
                  {{ memberName(expense.paidByMemberId) }} paid
                  <span aria-hidden="true">-</span>
                  {{ spentOn(expense.spentAt) }}
                </span>
              </span>
              <MoneyAmount :amount="expense.amount" :currency="expense.currency" size="sm" />
            </RouterLink>
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
            Show more ({{ groupExpenses.length - visibleExpenses.length }} left)
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
