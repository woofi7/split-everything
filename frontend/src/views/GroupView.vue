<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { formatMoney } from '@/domain/money'
import type { LocalGroup } from '@/offline/db'

const route = useRoute()
const groups = useGroupsStore()
const expenses = useExpensesStore()

const groupId = computed(() => String(route.params.groupId))
const group = ref<LocalGroup | undefined>()
const showSimplified = ref(true)

onMounted(async () => {
  group.value = await groups.get(groupId.value)
  await expenses.hydrate()
})

const memberName = (memberId: string) =>
  group.value?.members.find((member) => member.id === memberId)?.displayName ?? 'Unknown'

const groupExpenses = computed(() => expenses.forGroup(groupId.value))
const balances = computed(() => expenses.balanceFor(groupId.value))
const plan = computed(() =>
  showSimplified.value ? expenses.settleUpPlan(groupId.value) : expenses.rawDebts(groupId.value),
)
</script>

<template>
  <AppShell
    :title="group?.name ?? 'Group'"
    :subtitle="group ? `${group.members.length} members` : undefined"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="groups.isOffline"
    :is-syncing="expenses.isSyncing"
    :back-to="{ name: 'groups' }"
    back-label="Groups"
  >
    <template #header-action>
      <RouterLink
        v-if="group"
        :to="{ name: 'group-settings', params: { groupId } }"
        class="tap-target flex items-center text-sm text-[var(--text-muted)]"
      >
        Settings
      </RouterLink>
    </template>

    <section v-if="group" class="surface-card mb-4 p-4">
      <div class="flex items-baseline justify-between">
        <h2 class="text-sm font-medium text-[var(--text-muted)]">Balances</h2>
        <button
          type="button"
          class="text-xs text-brand-400 underline"
          @click="showSimplified = !showSimplified"
        >
          {{ showSimplified ? 'Show who owes whom' : 'Simplify' }}
        </button>
      </div>

      <ul class="mt-3 flex flex-col gap-2">
        <li
          v-for="balance in balances"
          :key="balance.memberId"
          class="flex items-center justify-between text-sm"
        >
          <span>{{ memberName(balance.memberId) }}</span>
          <MoneyAmount :amount="balance.net" :currency="group.baseCurrency" signed size="sm" />
        </li>
      </ul>

      <div v-if="plan.length > 0" class="mt-4 border-t pt-3" style="border-color: var(--border)">
        <h3 class="text-sm font-medium text-[var(--text-muted)]">
          {{ showSimplified ? 'Settle up in ' + plan.length + ' transfer' + (plan.length === 1 ? '' : 's') : 'Who owes whom' }}
        </h3>
        <ul class="mt-2 flex flex-col gap-2">
          <li
            v-for="transfer in plan"
            :key="`${transfer.fromMemberId}-${transfer.toMemberId}`"
            class="flex items-center justify-between gap-2 text-sm"
          >
            <span class="truncate">
              {{ memberName(transfer.fromMemberId) }} pays {{ memberName(transfer.toMemberId) }}
            </span>
            <RouterLink
              :to="{
                name: 'settle',
                params: { groupId },
                query: { from: transfer.fromMemberId, to: transfer.toMemberId, amount: transfer.amount },
              }"
              class="shrink-0 rounded-lg border px-2 py-1 text-xs"
              style="border-color: var(--border)"
            >
              {{ formatMoney(transfer.amount, group.baseCurrency) }}
            </RouterLink>
          </li>
        </ul>
      </div>

      <p v-else class="mt-4 text-sm text-[var(--text-muted)]">Everyone is settled up.</p>
    </section>

    <section v-if="group">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Expenses</h2>

      <ul v-if="groupExpenses.length > 0" class="flex flex-col gap-2">
        <li v-for="expense in groupExpenses" :key="expense.id">
          <RouterLink
            :to="{ name: 'expense', params: { groupId, expenseId: expense.id } }"
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
              <span class="block truncate text-xs text-[var(--text-muted)]">
                {{ memberName(expense.paidByMemberId) }} paid
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
  </AppShell>
</template>
