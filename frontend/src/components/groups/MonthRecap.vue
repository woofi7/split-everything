<script setup lang="ts">
import { t } from '@/i18n'
import { computed } from 'vue'
import { formatMoney } from '@/domain/money'
import { formatMonthHeading } from '@/domain/buckets'
import type { MonthSummary } from '@/domain/monthSummary'

const props = defineProps<{
  summary: MonthSummary
  currency: string
  nameOf: (memberId: string) => string
}>()

const comparison = computed(() => {
  const previous = props.summary.versusPrevious
  if (!previous || previous.difference === 0) return null

  const amount = formatMoney(Math.abs(previous.difference), props.currency)
  const month = formatMonthHeading(previous.label)

  return previous.difference > 0
    ? t('{amount} more than {month}', { amount, month })
    : t('{amount} less than {month}', { amount, month })
})

const againstAverage = computed(() => {
  const difference = props.summary.versusAverage
  if (difference === null || difference === 0) return null

  const amount = formatMoney(Math.abs(difference), props.currency)

  return difference > 0
    ? t('{amount} above the usual month', { amount })
    : t('{amount} below the usual month', { amount })
})
</script>
<template>
  <div
    data-testid="month-recap"
    class="mt-2 flex flex-col gap-2 rounded-lg px-3 py-2.5 text-sm"
    style="background: var(--surface-sunken)"
  >
    <ul class="flex flex-col gap-1">
      <li
        v-for="member in summary.byMember"
        :key="member.memberId"
        data-testid="recap-member"
        class="flex items-baseline justify-between gap-3"
      >
        <span class="min-w-0 truncate text-[var(--text-muted)]">{{ nameOf(member.memberId) }}</span>
        <span class="shrink-0 tabular-nums">{{ formatMoney(member.amount, currency) }}</span>
      </li>
    </ul>
    <p
      v-if="summary.ignored"
      data-testid="recap-ignored"
      class="flex items-baseline justify-between gap-3 text-xs text-[var(--text-muted)]"
    >
      <span class="min-w-0 truncate">
        {{ summary.ignored.count === 1
          ? t('1 expense left out of the total')
          : t('{count} expenses left out of the total', { count: summary.ignored.count }) }}
      </span>
      <span class="shrink-0 tabular-nums">
        {{ formatMoney(summary.ignored.total, currency) }}
      </span>
    </p>
    <p
      v-if="summary.biggest"
      data-testid="recap-biggest"
      class="flex items-baseline justify-between gap-3 border-t pt-2 text-[var(--text-muted)]"
      style="border-color: var(--border)"
    >
      <span class="min-w-0 truncate">
        {{ t('Biggest: {description}', { description: summary.biggest.description }) }}
      </span>
      <span class="shrink-0 tabular-nums">
        {{ formatMoney(summary.biggest.amount, currency) }}
      </span>
    </p>
    <p
      v-if="comparison || againstAverage"
      data-testid="recap-comparison"
      class="text-xs text-[var(--text-muted)]"
    >
      <span v-if="comparison">{{ comparison }}</span>
      <span v-if="comparison && againstAverage" aria-hidden="true"> &middot; </span>
      <span v-if="againstAverage">{{ againstAverage }}</span>
    </p>
  </div>
</template>
