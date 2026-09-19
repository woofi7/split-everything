<script setup lang="ts">
import { t } from '@/i18n'
import { RouterLink } from 'vue-router'

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
      replace
      data-testid="record-expense"
      class="tap-target flex items-center justify-center rounded-lg text-sm font-medium"
      :class="current === 'expense' ? 'bg-[var(--surface-raised)] text-accent' : 'text-[var(--text-muted)]'"
      :aria-current="current === 'expense' ? 'page' : undefined"
    >{{ t('Expense') }}
    </RouterLink>
    <RouterLink
      :to="{ name: 'add-payment' }"
      replace
      data-testid="record-payment"
      class="tap-target flex items-center justify-center rounded-lg text-sm font-medium"
      :class="current === 'payment' ? 'bg-[var(--surface-raised)] text-accent' : 'text-[var(--text-muted)]'"
      :aria-current="current === 'payment' ? 'page' : undefined"
    >{{ t('Payment') }}
    </RouterLink>
  </div>
</template>
