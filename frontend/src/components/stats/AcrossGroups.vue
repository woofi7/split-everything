<script setup lang="ts">
import { t } from '@/i18n'
import { computed } from 'vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { formatMoney } from '@/domain/money'
import { matchesAnyNamePattern } from '@/domain/namePatterns'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import type { LocalExpense } from '@/offline/db'

const auth = useAuthStore()
const groups = useGroupsStore()
const expenses = useExpensesStore()

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

interface Combined {
  currency: string
  groups: number
  expenseCount: number
  spend: number
  paid: number
  share: number
  net: number
}

const combined = computed<Combined[]>(() => {
  const byCurrency = new Map<string, Combined>()

  for (const group of groups.groups) {
    const mine = auth.user ? groups.myMemberId(group.id, auth.user.id) : null
    const list = everyday(group)

    const found = byCurrency.get(group.baseCurrency) ?? {
      currency: group.baseCurrency,
      groups: 0,
      expenseCount: 0,
      spend: 0,
      paid: 0,
      share: 0,
      net: 0,
    }

    found.groups += 1
    found.expenseCount += list.length
    found.spend += list.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0)

    if (mine) {
      found.paid += list.reduce(
        (sum, expense) =>
          sum +
          payersOf(expense)
            .filter((payer) => payer.memberId === mine)
            .reduce((part, payer) => part + payer.amountInBaseCurrency, 0),
        0,
      )

      found.share += list.reduce(
        (sum, expense) =>
          sum +
          expense.splits
            .filter((split) => split.memberId === mine)
            .reduce((part, split) => part + split.amountInBaseCurrency, 0),
        0,
      )

      found.net += expenses.balanceFor(group.id).find((entry) => entry.memberId === mine)?.net ?? 0
    }

    byCurrency.set(group.baseCurrency, found)
  }

  return [...byCurrency.values()].sort((left, right) => right.spend - left.spend)
})

const manyCurrencies = computed(() => combined.value.length > 1)
</script>
<template>
  <template v-if="combined.length > 0">
    <div class="mt-8 mb-3 flex items-center gap-3">
      <span class="h-px flex-1" style="background: var(--border)" aria-hidden="true" />
      <h2 class="text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]">
        {{ t('Just you') }}
      </h2>
      <span class="h-px flex-1" style="background: var(--border)" aria-hidden="true" />
    </div>
    <section
      v-for="block in combined"
      :key="block.currency"
      data-testid="across-groups"
      class="surface-card mb-4 p-4"
    >
      <h2 class="text-sm font-medium text-[var(--text-muted)]">
        {{ manyCurrencies
          ? t('Across your groups in {currency}', { currency: block.currency })
          : t('Across your groups') }}
      </h2>
      <dl class="mt-3 flex flex-col gap-2 text-sm">
        <div class="flex items-baseline justify-between gap-3">
          <dt class="text-[var(--text-muted)]">{{ t('You paid') }}</dt>
          <dd data-testid="combined-paid" class="tabular-nums">
            {{ formatMoney(block.paid, block.currency) }}
          </dd>
        </div>
        <div class="flex items-baseline justify-between gap-3">
          <dt class="text-[var(--text-muted)]">{{ t('Your share') }}</dt>
          <dd data-testid="combined-share" class="tabular-nums">
            {{ formatMoney(block.share, block.currency) }}
          </dd>
        </div>
        <div
          class="flex items-baseline justify-between gap-3 border-t pt-2"
          style="border-color: var(--border)"
        >
          <dt class="text-[var(--text-muted)]">
            {{ block.net >= 0 ? t('Owed to you') : t('You owe') }}
          </dt>
          <dd data-testid="combined-net">
            <MoneyAmount :amount="Math.abs(block.net)" :currency="block.currency" size="sm" />
          </dd>
        </div>
      </dl>
      <p class="mt-3 text-xs text-[var(--text-muted)]">
        {{ t('{expenses} expenses across {groups} groups, {amount} in all.', {
          expenses: block.expenseCount,
          groups: block.groups,
          amount: formatMoney(block.spend, block.currency),
        }) }}
      </p>
    </section>
  </template>
</template>
