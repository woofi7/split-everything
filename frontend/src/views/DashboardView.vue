<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, useTemplateRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faGear } from '@fortawesome/free-solid-svg-icons'
import AppShell from '@/components/layout/AppShell.vue'
import GroupPicker from '@/components/groups/GroupPicker.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SpendPie from '@/components/ui/SpendPie.vue'
import { resolveIcon } from '@/domain/icons'
import { memberColor, memberColors } from '@/domain/memberColors'
import { formatMoney } from '@/domain/money'
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
const groups = useGroupsStore()
const expenses = useExpensesStore()

const isPickingGroup = ref(false)

/**
 * The two things that are about this group rather than about what is in it.
 *
 * Behind one icon, because neither is something you do often: changing group and
 * opening its settings were both competing for the corner with the page title.
 */
const isMenuOpen = ref(false)

function closeMenu(): void {
  isMenuOpen.value = false
}

function openGroupPicker(): void {
  closeMenu()
  isPickingGroup.value = true
}

// Anywhere else, and Escape. A menu that only closes by choosing something is a
// trap on a phone, where there is no obvious way to dismiss it.
onMounted(() => {
  window.addEventListener('click', onDocumentClick, true)
  window.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  window.removeEventListener('click', onDocumentClick, true)
  window.removeEventListener('keydown', onKeydown)
})

function onDocumentClick(event: MouseEvent): void {
  if (!isMenuOpen.value) return

  const target = event.target as HTMLElement | null
  if (target?.closest('[data-menu-root]')) return

  closeMenu()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') closeMenu()
}

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

watch(() => groups.mainGroupId, () => void loadMainGroup())

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
const icon = computed(() => resolveIcon(group.value?.iconName))

const groupExpenses = computed(() =>
  group.value ? expenses.forGroup(group.value.id) : [],
)

const colours = computed(() =>
  memberColors((group.value?.members ?? []).map((member) => member.id)),
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
    { rootMargin: '400px' },
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
  new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
</script>

<template>
  <AppShell
    :title="group?.name ?? 'Dashboard'"
    :subtitle="group ? `${group.members.length} people` : undefined"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="groups.isOffline"
    :is-syncing="expenses.isSyncing"
  >
    <template #header-action>
      <!--
        One corner, one icon. Both items are about the group rather than about
        anything in it, and neither is done often enough to earn a place on the
        title row of every visit.
      -->
      <div data-menu-root class="relative">
        <button
          type="button"
          data-testid="group-menu"
          class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
          style="border-color: var(--border)"
          aria-haspopup="menu"
          :aria-expanded="isMenuOpen"
          aria-label="Group options"
          title="Group options"
          @click="isMenuOpen = !isMenuOpen"
        >
          <FontAwesomeIcon :icon="faGear" class="h-4 w-4" />
        </button>

        <div
          v-if="isMenuOpen"
          data-testid="group-menu-items"
          role="menu"
          class="surface-card absolute right-0 z-40 mt-2 flex w-56 flex-col overflow-hidden p-1 shadow-lg"
        >
          <!-- Always here, even with one group: it is also the way to the next one. -->
          <button
            type="button"
            role="menuitem"
            data-testid="change-group"
            class="btn btn-press btn-quiet w-full justify-start gap-2 text-sm"
            @click="openGroupPicker"
          >
            <span
              class="flex h-5 w-5 items-center justify-center rounded-md text-white"
              :style="{ backgroundColor: group?.colorHex ?? '#4f46e5' }"
              aria-hidden="true"
            >
              <FontAwesomeIcon :icon="icon.definition" class="h-3 w-3" />
            </span>
            Change group
          </button>

          <RouterLink
            v-if="group"
            role="menuitem"
            data-testid="menu-group-settings"
            :to="{ name: 'group-settings', params: { groupId: group.id } }"
            class="btn btn-press btn-quiet w-full justify-start gap-2 text-sm"
            @click="closeMenu"
          >
            <span class="flex h-5 w-5 items-center justify-center" aria-hidden="true">
              <FontAwesomeIcon :icon="faGear" class="h-3 w-3" />
            </span>
            Group settings
          </RouterLink>
        </div>
      </div>
    </template>

    <template v-if="group">
      <!-- The shape of the group's spending, first: it is what the screen is for. -->
      <section class="surface-card mb-4 p-4">
        <p class="mb-3 text-sm text-[var(--text-muted)]">Who paid</p>
        <SpendPie :slices="paidByMember" :currency="currency" />
      </section>

      <!--
        One line: what you are owed or owe, and the way to act on it. The number
        and the button that answers it belong next to each other.
      -->
      <section
        data-testid="balance-line"
        class="surface-card mb-4 flex items-center justify-between gap-3 p-4"
      >
        <!--
          Label over number, and at least as tall as the button beside it so the
          row reads as one block rather than a number with a control floating next
          to it. Centred, so it stays level whichever of the two is taller.
        -->
        <span class="flex min-h-11 min-w-0 flex-col justify-center">
          <span class="text-xs text-[var(--text-muted)]">Your balance</span>
          <MoneyAmount :amount="group.myNetBalance" :currency="currency" signed size="lg" />
        </span>

        <RouterLink
          :to="{ name: 'settle', params: { groupId: group.id } }"
          data-testid="settle-up"
          class="btn btn-press btn-secondary shrink-0"
          style="border-color: var(--border)"
        >
          Settle up
        </RouterLink>
      </section>

      <section v-if="balances.length > 0" class="surface-card mb-4 p-4">
        <div class="flex items-baseline justify-between gap-2">
          <p class="text-sm text-[var(--text-muted)]">Balances</p>
          <button
            v-if="plan.length > 0"
            type="button"
            data-testid="toggle-simplify"
            class="btn btn-press btn-quiet min-h-0 px-2 py-1 text-xs text-brand-400"
            @click="showSimplified = !showSimplified"
          >
            {{ showSimplified ? 'Show who owes whom' : 'Simplify' }}
          </button>
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
            </span>
            <MoneyAmount :amount="member.net" :currency="currency" signed size="sm" />
          </li>
        </ul>

        <p v-if="plan.length === 0" class="mt-3 text-sm text-[var(--text-muted)]">
          Everyone is settled up.
        </p>

        <div v-else class="mt-4 border-t pt-3" style="border-color: var(--border)">
          <h3 class="text-sm font-medium text-[var(--text-muted)]">
            {{ showSimplified
              ? `Settle up in ${plan.length} transfer${plan.length === 1 ? '' : 's'}`
              : 'Who owes whom' }}
          </h3>
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
        <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Expenses</h2>

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
                    title="Saved on this device, waiting to sync"
                  >
                    Waiting
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

        <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
          No expenses yet. Add the first one with the button below.
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

    <p v-else-if="groups.isLoading" class="py-12 text-center text-[var(--text-muted)]">
      Loading your groups
    </p>

    <div v-else class="surface-card p-6 text-center">
      <p class="font-medium">No groups yet</p>
      <p class="mt-1 text-sm text-[var(--text-muted)]">
        Create one for the people you share costs with, or open an invite someone sent you.
      </p>
      <RouterLink :to="{ name: 'new-group' }" class="btn btn-press btn-primary mt-4">
        New group
      </RouterLink>
    </div>

    <GroupPicker :open="isPickingGroup" @close="isPickingGroup = false" />
  </AppShell>
</template>
