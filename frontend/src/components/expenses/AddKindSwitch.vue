<script setup lang="ts">
import { t } from '@/i18n'
import { RouterLink } from 'vue-router'

/**
 * What the plus button is about to record.
 *
 * Two things move money in a shared household and only one of them is an expense.
 * "Emma gave me fifty" is not something the group bought: nobody owes a share of
 * it, it belongs in no month's spending, and typed in as an expense it inflates
 * every total on the group screen and settles nothing. So the add screen asks
 * which, in one tap, before anything else is typed.
 *
 * Two links rather than a field on one form: the two forms ask different
 * questions, and a person who taps the wrong one loses nothing but the tap.
 */
defineProps<{ current: 'expense' | 'payment' }>()
</script>

<template>
  <div
    class="mb-3 grid grid-cols-2 gap-1 rounded-xl p-1"
    style="background: var(--surface-sunken)"
    role="group"
    :aria-label="t('What to record')"
  >
    <RouterLink
      :to="{ name: 'add-expense' }"
      data-testid="record-expense"
      class="tap-target flex items-center justify-center rounded-lg text-sm font-medium"
      :class="current === 'expense' ? 'bg-[var(--surface-raised)] text-accent' : 'text-[var(--text-muted)]'"
      :aria-current="current === 'expense' ? 'page' : undefined"
    >{{ t('Expense') }}
    </RouterLink>

    <RouterLink
      :to="{ name: 'add-payment' }"
      data-testid="record-payment"
      class="tap-target flex items-center justify-center rounded-lg text-sm font-medium"
      :class="current === 'payment' ? 'bg-[var(--surface-raised)] text-accent' : 'text-[var(--text-muted)]'"
      :aria-current="current === 'payment' ? 'page' : undefined"
    >{{ t('Payment') }}
    </RouterLink>
  </div>
</template>
