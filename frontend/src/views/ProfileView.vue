<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faGear } from '@fortawesome/free-solid-svg-icons'
import { resolveIcon } from '@/domain/icons'
import { groupColor } from '@/domain/themes'
import { formatMoney } from '@/domain/money'
import { matchesAnyNamePattern } from '@/domain/namePatterns'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import type { LocalExpense } from '@/offline/db'

const auth = useAuthStore()
const groups = useGroupsStore()
const expenses = useExpensesStore()

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()
})

function everyday(group: { id: string; ignoredNamePatterns?: string[] | null }): LocalExpense[] {
  return expenses
    .forGroup(group.id)
    .filter((expense) => !matchesAnyNamePattern(expense.description, group.ignoredNamePatterns ?? []))
}

function payersOf(expense: LocalExpense) {
  return expense.payers && expense.payers.length > 0
    ? expense.payers
    : [{ memberId: expense.paidByMemberId, amountInBaseCurrency: expense.amountInBaseCurrency }]
}

interface GroupRow {
  id: string
  name: string
  colour: string
  icon: ReturnType<typeof resolveIcon>
  currency: string
  isArchived: boolean
  people: number
  expenseCount: number
  spend: number
  paid: number
  share: number
  net: number
}

const rows = computed<GroupRow[]>(() =>
  groups.groups.map((group) => {
    const mine = auth.user ? groups.myMemberId(group.id, auth.user.id) : null
    const list = everyday(group)

    const paid = mine
      ? list.reduce(
          (sum, expense) =>
            sum +
            payersOf(expense)
              .filter((payer) => payer.memberId === mine)
              .reduce((part, payer) => part + payer.amountInBaseCurrency, 0),
          0,
        )
      : 0

    const share = mine
      ? list.reduce(
          (sum, expense) =>
            sum +
            expense.splits
              .filter((split) => split.memberId === mine)
              .reduce((part, split) => part + split.amountInBaseCurrency, 0),
          0,
        )
      : 0

    const net = mine
      ? (expenses.balanceFor(group.id).find((entry) => entry.memberId === mine)?.net ?? 0)
      : 0

    return {
      id: group.id,
      name: group.name,
      colour: groupColor(group),
      icon: resolveIcon(group.iconName ?? null),
      currency: group.baseCurrency,
      isArchived: group.isArchived,
      people: group.members.filter((member) => member.status === 'Active').length,
      expenseCount: list.length,
      spend: list.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
      paid,
      share,
      net,
    }
  }),
)

const liveGroups = computed(() => rows.value.filter((row) => !row.isArchived))
const archivedGroups = computed(() => rows.value.filter((row) => row.isArchived))

</script>
<template>
  <AppShell
    width="wide"
    :title="auth.user?.displayName ?? t('Profile')"
    :subtitle="auth.user?.email"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-syncing="expenses.isSyncing"
  >
    <template #header-action>
      <RouterLink
        :to="{ name: 'profile-settings' }"
        data-testid="profile-settings-link"
        class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
        style="border-color: var(--border)"
        :aria-label="t('Settings')"
        :title="t('Settings')"
      >
        <FontAwesomeIcon :icon="faGear" class="h-4 w-4" />
      </RouterLink>
    </template>
    <section class="mb-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Your groups') }}</h2>
      <ul v-if="liveGroups.length > 0" class="flex flex-col gap-2 lg:grid lg:grid-cols-2">
        <li v-for="row in liveGroups" :key="row.id">
          <RouterLink
            :to="{ name: 'group', params: { groupId: row.id } }"
            data-testid="group-row"
            :data-group-id="row.id"
            class="tap-target flex items-center gap-3 rounded-xl border p-3"
            style="border-color: var(--border); background: var(--surface-raised)"
          >
            <span
              class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-white"
              :style="{ backgroundColor: row.colour }"
              aria-hidden="true"
            >
              <FontAwesomeIcon :icon="row.icon.definition" class="h-4 w-4" />
            </span>
            <span class="flex min-w-0 flex-1 flex-col">
              <span class="truncate font-medium">{{ row.name }}</span>
              <span class="truncate text-xs text-[var(--text-muted)]">
                {{ t('{count} people', { count: row.people }) }}
                <span aria-hidden="true">-</span>
                {{ t('{amount} spent', { amount: formatMoney(row.spend, row.currency) }) }}
              </span>
            </span>
            <MoneyAmount :amount="row.net" :currency="row.currency" signed size="sm" />
          </RouterLink>
        </li>
      </ul>
      <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
        {{ t('No groups yet') }}
      </p>
    </section>
    <section v-if="archivedGroups.length > 0" class="mb-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Archived') }}</h2>
      <ul class="flex flex-col gap-2 lg:grid lg:grid-cols-2">
        <li v-for="row in archivedGroups" :key="row.id">
          <RouterLink
            :to="{ name: 'group', params: { groupId: row.id } }"
            data-testid="archived-group-row"
            :data-group-id="row.id"
            class="tap-target flex items-center gap-3 rounded-xl border p-3 opacity-70"
            style="border-color: var(--border)"
          >
            <span
              class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-white"
              :style="{ backgroundColor: row.colour }"
              aria-hidden="true"
            >
              <FontAwesomeIcon :icon="row.icon.definition" class="h-4 w-4" />
            </span>
            <span class="flex min-w-0 flex-1 flex-col">
              <span class="truncate font-medium">{{ row.name }}</span>
              <span class="truncate text-xs text-[var(--text-muted)]">
                {{ t('{amount} spent', { amount: formatMoney(row.spend, row.currency) }) }}
              </span>
            </span>
            <MoneyAmount :amount="row.net" :currency="row.currency" signed size="sm" />
          </RouterLink>
        </li>
      </ul>
    </section>
  </AppShell>
</template>
