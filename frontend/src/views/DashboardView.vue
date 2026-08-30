<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import GroupCard from '@/components/groups/GroupCard.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SpendPie from '@/components/ui/SpendPie.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'

const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()
})

const currency = computed(() => auth.user?.defaultCurrency ?? 'CAD')

/**
 * Spend per group, for the pie. Archived groups are left out: the chart is about
 * where money is going, and a frozen group is not going anywhere.
 */
const spendByGroup = computed(() =>
  groups.visibleGroups.map((group) => ({
    id: group.id,
    label: group.name,
    amount: group.totalSpend,
    colorHex: group.colorHex,
  })),
)
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

    <!-- Where the money went, before the list of where to look. -->
    <section v-if="groups.visibleGroups.length > 0" class="surface-card mb-4 p-4">
      <p class="mb-3 text-sm text-[var(--text-muted)]">Spending by group</p>
      <SpendPie :slices="spendByGroup" :currency="currency" />
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
