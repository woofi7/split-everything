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

/**
 * How one person is doing, across every group they are in.
 *
 * The chart above is about a group - which group is the question the rest of the
 * screen answers. This is the other question, and nothing answered it: three
 * groups meant three tabs and the arithmetic in your head. What you put in, what
 * was yours, and where that leaves you when it is all added up.
 *
 * Computed from the local replica rather than asked for, like everything else on
 * this screen: it is arithmetic over rows this device already holds, so it works
 * on a train and it is there before a request could come back.
 */

const auth = useAuthStore()
const groups = useGroupsStore()
const expenses = useExpensesStore()

/** The same expenses a group screen totals: what a group leaves out, this leaves out. */
function everyday(group: { id: string; ignoredNamePatterns?: string[] | null }): LocalExpense[] {
  return expenses
    .forGroup(group.id)
    .filter((expense) => !matchesAnyNamePattern(expense.description, group.ignoredNamePatterns ?? []))
}

/** Who paid an expense, however the row was stored: an older one names one payer. */
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

/**
 * By currency, because adding them would be inventing a number: a group kept in
 * euros and one kept in dollars have no common total until somebody picks a rate,
 * and this is not the place to pick one. One block is the ordinary case and simply
 * says "across your groups"; a second appears only for somebody who keeps two.
 *
 * Archived groups count. What you paid into a group that is now closed is still
 * money you paid, and a balance left in one is still outstanding.
 */
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

      // The balance as the group screen states it, settlements and all, rather
      // than paid less share: money handed back is what that difference misses.
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
    <!--
      A rule across the screen, because what follows answers a different question
      from everything above it: that is the group, this is you, and the same words -
      "you paid", "your share" - mean different numbers on either side of it.
    -->
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
