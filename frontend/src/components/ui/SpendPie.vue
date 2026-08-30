<script setup lang="ts">
import { computed } from 'vue'
import { formatMoney } from '@/domain/money'

/**
 * Spend distribution, as a pie.
 *
 * A list of totals says what each group cost. A pie says how the spending is
 * spread, which is the question a dashboard answers at a glance. Plain SVG arcs:
 * a chart library is a large dependency for one figure, in an app that has to
 * work offline and start fast on a phone.
 */

export interface PieSlice {
  id: string
  label: string
  amount: number
  colorHex: string
}

const props = defineProps<{
  slices: readonly PieSlice[]
  currency: string
}>()

const RADIUS = 60
const CENTRE = 70

// Nothing spent is not a wedge of size zero, it is an absence. Dropping them also
// keeps a group that exists but has no expenses out of the legend.
const spending = computed(() => props.slices.filter((slice) => slice.amount > 0))

const total = computed(() => spending.value.reduce((sum, slice) => sum + slice.amount, 0))

const wedges = computed(() => {
  if (total.value <= 0) return []

  let angle = -Math.PI / 2
  return spending.value.map((slice) => {
    const share = slice.amount / total.value
    const sweep = share * Math.PI * 2
    const start = angle
    angle += sweep

    return {
      ...slice,
      share,
      path: arc(start, angle),
    }
  })
})

/**
 * A wedge from the centre. Not used for a single slice: an arc of a full turn
 * starts and ends at the same point, so it draws nothing at all.
 */
function arc(start: number, end: number): string {
  const x1 = CENTRE + RADIUS * Math.cos(start)
  const y1 = CENTRE + RADIUS * Math.sin(start)
  const x2 = CENTRE + RADIUS * Math.cos(end)
  const y2 = CENTRE + RADIUS * Math.sin(end)
  const large = end - start > Math.PI ? 1 : 0

  return `M ${CENTRE} ${CENTRE} L ${x1.toFixed(2)} ${y1.toFixed(2)} ` +
    `A ${RADIUS} ${RADIUS} 0 ${large} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`
}

const single = computed(() => (wedges.value.length === 1 ? wedges.value[0] : null))

const percent = (share: number) => `${Math.round(share * 100)}%`

const description = computed(() =>
  wedges.value.map((wedge) => `${wedge.label} ${percent(wedge.share)}`).join(', '),
)
</script>

<template>
  <!--
    Names on the left under the heading, chart on the right. The chart is beside
    the heading as well as the list, which is what lets it be the tallest thing in
    the card without the card growing: the words fill the space it needs anyway.
  -->
  <div class="flex items-center gap-4">
    <div class="min-w-0 flex-1">
      <p v-if="$slots.heading" class="mb-3 text-sm text-[var(--text-muted)]">
        <slot name="heading" />
      </p>

      <p v-if="wedges.length === 0" class="text-sm text-[var(--text-muted)]">
        Nothing spent yet.
      </p>

      <ul v-else class="flex min-w-0 flex-col gap-1 text-sm">
        <li v-for="wedge in wedges" :key="wedge.id" class="flex items-center gap-2">
          <span
            class="h-2.5 w-2.5 shrink-0 rounded-full"
            :style="{ backgroundColor: wedge.colorHex }"
            aria-hidden="true"
          />
          <span class="min-w-0 flex-1 truncate">{{ wedge.label }}</span>
          <span class="shrink-0 text-[var(--text-muted)]">{{ percent(wedge.share) }}</span>
        </li>
      </ul>
    </div>

    <svg
      v-if="wedges.length > 0"
      viewBox="0 0 140 140"
      class="h-32 w-32 shrink-0"
      role="img"
      :aria-label="`Spending by group: ${description}`"
    >
      <circle
        v-if="single"
        data-testid="whole"
        :cx="CENTRE"
        :cy="CENTRE"
        :r="RADIUS"
        :fill="single.colorHex"
      />
      <path
        v-for="wedge in single ? [] : wedges"
        :key="wedge.id"
        data-testid="wedge"
        :d="wedge.path"
        :fill="wedge.colorHex"
      />

      <!-- Punched out so the total can sit inside without fighting the wedges. -->
      <circle :cx="CENTRE" :cy="CENTRE" r="34" fill="var(--surface-raised)" />
      <text
        :x="CENTRE"
        :y="CENTRE + 5"
        text-anchor="middle"
        class="fill-[var(--text)] text-[13px] font-semibold"
      >
        {{ formatMoney(total, props.currency) }}
      </text>
    </svg>
  </div>
</template>
