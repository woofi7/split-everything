<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import AppShell from '@/components/layout/AppShell.vue'
import GroupPicker from '@/components/groups/GroupPicker.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SpendPie from '@/components/ui/SpendPie.vue'
import { resolveIcon } from '@/domain/icons'
import { memberColor, memberColors } from '@/domain/memberColors'
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

const groups = useGroupsStore()
const expenses = useExpensesStore()

const isPickingGroup = ref(false)

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()
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
      <!-- Always here, even with one group: it is also the way to the next one. -->
      <button
        type="button"
        data-testid="change-group"
        class="btn btn-press btn-secondary min-h-0 gap-2 px-2 py-1 text-xs"
        aria-haspopup="dialog"
        @click="isPickingGroup = true"
      >
        <span
          class="flex h-5 w-5 items-center justify-center rounded-md text-white"
          :style="{ backgroundColor: group?.colorHex ?? '#4f46e5' }"
          aria-hidden="true"
        >
          <FontAwesomeIcon :icon="icon.definition" class="h-3 w-3" />
        </span>
        Change
      </button>
    </template>

    <template v-if="group">
      <section class="surface-card mb-4 p-4">
        <p class="text-sm text-[var(--text-muted)]">Your balance in this group</p>
        <MoneyAmount :amount="group.myNetBalance" :currency="currency" signed size="lg" />

        <div class="mt-3 flex gap-2">
          <RouterLink
            :to="{ name: 'settle', params: { groupId: group.id } }"
            class="btn btn-press btn-secondary flex-1"
          >
            Settle up
          </RouterLink>
          <RouterLink
            :to="{ name: 'group-settings', params: { groupId: group.id } }"
            class="btn btn-press btn-secondary flex-1"
          >
            Group settings
          </RouterLink>
        </div>
      </section>

      <section class="surface-card mb-4 p-4">
        <p class="mb-3 text-sm text-[var(--text-muted)]">Who paid</p>
        <SpendPie :slices="paidByMember" :currency="currency" />
      </section>

      <section v-if="balances.length > 0" class="surface-card mb-4 p-4">
        <p class="mb-2 text-sm text-[var(--text-muted)]">Balances</p>
        <ul class="flex flex-col gap-2 text-sm">
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
      </section>

      <section>
        <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Expenses</h2>

        <ul v-if="groupExpenses.length > 0" class="flex flex-col gap-2">
          <li v-for="expense in groupExpenses" :key="expense.id">
            <RouterLink
              :to="{ name: 'expense', params: { groupId: group.id, expenseId: expense.id } }"
              class="surface-card tap-target flex items-center justify-between gap-3 p-3"
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
                <span class="flex items-center gap-1.5 truncate text-xs text-[var(--text-muted)]">
                  <span
                    class="h-2 w-2 shrink-0 rounded-full"
                    :style="{ backgroundColor: colourOf(expense.paidByMemberId) }"
                    aria-hidden="true"
                  />
                  {{ memberName(expense.paidByMemberId) }} paid
                  <span aria-hidden="true">-</span>
                  <span class="shrink-0">{{ spentOn(expense.spentAt) }}</span>
                </span>
              </span>
              <MoneyAmount :amount="expense.amount" :currency="expense.currency" size="sm" />
            </RouterLink>
          </li>
        </ul>

        <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
          No expenses yet. Add the first one with the button below.
        </p>
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
