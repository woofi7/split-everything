<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupCard from '@/components/groups/GroupCard.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SpendPie from '@/components/ui/SpendPie.vue'
import GroupSwitcher from '@/components/groups/GroupSwitcher.vue'
import { memberColors } from '@/domain/memberColors'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'

const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()
  await loadMainGroup()
})

/**
 * The list endpoint counts members but does not name them, and the pie is by
 * person, so the main group needs a detail read. Watched as well as loaded, since
 * switching group is the whole point of the control above it.
 */
async function loadMainGroup(): Promise<void> {
  if (groups.mainGroupId) await groups.get(groups.mainGroupId)
}

watch(() => groups.mainGroupId, () => void loadMainGroup())

const currency = computed(() => groups.mainGroup?.baseCurrency ?? auth.user?.defaultCurrency ?? 'CAD')

/**
 * Who spent what in the main group.
 *
 * By person rather than by group: a dashboard about one group answers "who has
 * been paying", which is the question a shared account actually has. Totals per
 * group are on each card already, and the group balance is above.
 *
 * Money paid, not shares owed, because that is what "spending" means to the person
 * who handed over the card.
 */
const spendByMember = computed(() => {
  const group = groups.mainGroup
  if (!group) return []

  const paid = new Map<string, number>()
  for (const expense of expenses.forGroup(group.id)) {
    paid.set(
      expense.paidByMemberId,
      (paid.get(expense.paidByMemberId) ?? 0) + expense.amountInBaseCurrency,
    )
  }

  const colours = memberColors(group.members.map((member) => member.id))

  return group.members
    .map((member) => ({
      id: member.id,
      label: member.displayName,
      amount: paid.get(member.id) ?? 0,
      colorHex: colours[member.id],
    }))
    .sort((left, right) => right.amount - left.amount)
})
</script>

<template>
  <AppShell
    title="Dashboard"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="groups.isOffline"
    :is-syncing="expenses.isSyncing"
  >
    <template #header-action>
      <RouterLink
        :to="{ name: 'new-group' }"
        class="btn btn-press btn-primary"
      >
        New
      </RouterLink>
    </template>

    <section class="surface-card mb-4 p-4">
      <p class="text-sm text-[var(--text-muted)]">Across all groups</p>
      <MoneyAmount
        :amount="groups.netAcrossGroups"
        :currency="currency"
        signed
        size="lg"
      />
    </section>

    <!-- Which group everything else on this screen is about. -->
    <section v-if="groups.visibleGroups.length > 1" class="mb-4">
      <GroupSwitcher />
    </section>

    <!-- Who has been paying, before the list of where to look. -->
    <section v-if="groups.mainGroup" class="surface-card mb-4 p-4">
      <p class="mb-3 text-sm text-[var(--text-muted)]">
        Who paid in {{ groups.mainGroup.name }}
      </p>
      <SpendPie :slices="spendByMember" :currency="currency" />
    </section>

    <div v-if="groups.visibleGroups.length > 0" class="flex flex-col gap-3">
      <GroupCard v-for="group in groups.visibleGroups" :key="group.id" :group="group" />
    </div>

    <p v-else-if="groups.isLoading" class="py-12 text-center text-[var(--text-muted)]">
      Loading your groups
    </p>

    <div v-else class="surface-card p-6 text-center">
      <p class="font-medium">No groups yet</p>
      <p class="mt-1 text-sm text-[var(--text-muted)]">
        Create one for the people you share costs with, or open an invite someone sent you.
      </p>
      <RouterLink
        :to="{ name: 'new-group' }"
        class="btn btn-press btn-primary mt-4"
      >
        Create a group
      </RouterLink>
    </div>

    <button
      v-if="groups.groups.some((group) => group.isArchived)"
      type="button"
      class="tap-target mt-4 w-full text-sm text-[var(--text-muted)] underline"
      @click="groups.includeArchived = !groups.includeArchived"
    >
      {{ groups.includeArchived ? 'Hide archived groups' : 'Show archived groups' }}
    </button>
  </AppShell>
</template>
