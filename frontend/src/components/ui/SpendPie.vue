<script setup lang="ts">
import { t } from '@/i18n'
import { computed, ref } from 'vue'
import { formatMoney } from '@/domain/money'

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

const hoveredId = ref<string | null>(null)
const pinnedId = ref<string | null>(null)

const selectedId = computed(() => hoveredId.value ?? pinnedId.value)

const selected = computed(() => wedges.value.find((wedge) => wedge.id === selectedId.value) ?? null)

function select(id: string): void {
  hoveredId.value = id
}

function clear(): void {
  hoveredId.value = null
}

function toggle(id: string): void {
  pinnedId.value = pinnedId.value === id ? null : id
  hoveredId.value = pinnedId.value === null ? null : id
}

const description = computed(() =>
  wedges.value.map((wedge) => `${wedge.label} ${percent(wedge.share)}`).join(', '),
)
</script>
<template>
  <div class="flex items-start gap-4">
    <div class="min-w-0 flex-1">
      <p v-if="$slots.heading" class="mb-3 text-sm text-[var(--text-muted)]">
        <slot name="heading" />
      </p>
      <p v-if="wedges.length === 0" data-testid="pie-empty" class="text-sm text-[var(--text-muted)]">
        <slot name="empty">{{ t('Nothing spent yet.') }}</slot>
      </p>
      <ul v-else class="flex min-w-0 flex-col gap-1 text-sm">
        <li v-for="wedge in wedges" :key="wedge.id">
          <button
            type="button"
            data-testid="legend-row"
            class="flex w-full items-center gap-2 rounded-md px-1 py-0.5 text-left transition-colors"
            :class="selectedId === wedge.id ? 'bg-[var(--surface-sunken)]' : ''"
            :aria-pressed="selectedId === wedge.id"
            @mouseenter="select(wedge.id)"
            @mouseleave="clear"
            @focus="select(wedge.id)"
            @blur="clear"
            @click="toggle(wedge.id)"
          >
            <span
              class="h-2.5 w-2.5 shrink-0 rounded-full"
              :style="{ backgroundColor: wedge.colorHex }"
              aria-hidden="true"
            />
            <span class="min-w-0 flex-1 truncate">{{ wedge.label }}</span>
            <span
              v-if="selectedId === wedge.id"
              data-testid="legend-amount"
              class="shrink-0 tabular-nums"
            >
              {{ formatMoney(wedge.amount, props.currency) }}
            </span>
            <span class="shrink-0 tabular-nums text-[var(--text-muted)]">
              {{ percent(wedge.share) }}
            </span>
          </button>
        </li>
      </ul>
    </div>
    <svg
      v-if="wedges.length > 0"
      viewBox="0 0 140 140"
      class="h-32 w-32 shrink-0"
      role="group"
      :aria-label="`Spending by group: ${description}`"
    >
      <circle
        v-if="single"
        data-testid="whole"
        role="button"
        tabindex="-1"
        :aria-label="`${single.label}: ${formatMoney(single.amount, props.currency)}, 100%`"
        :cx="CENTRE"
        :cy="CENTRE"
        :r="RADIUS"
        :fill="single.colorHex"
        class="wedge cursor-pointer"
        @mouseenter="select(single.id)"
        @mouseleave="clear"
        @click="toggle(single.id)"
      />
      <path
        v-for="wedge in single ? [] : wedges"
        :key="wedge.id"
        data-testid="wedge"
        role="button"
        tabindex="-1"
        :aria-label="`${wedge.label}: ${formatMoney(wedge.amount, props.currency)}, ${percent(wedge.share)}`"
        :d="wedge.path"
        :fill="wedge.colorHex"
        :opacity="selectedId && selectedId !== wedge.id ? 0.35 : 1"
        class="wedge cursor-pointer transition-opacity"
        @mouseenter="select(wedge.id)"
        @mouseleave="clear"
        @click="toggle(wedge.id)"
      />
      <circle :cx="CENTRE" :cy="CENTRE" r="34" fill="var(--surface-raised)" />
      <template v-if="selected">
        <text
          :x="CENTRE"
          :y="CENTRE - 4"
          text-anchor="middle"
          data-testid="centre-amount"
          class="fill-[var(--text)] text-[12px] font-semibold"
        >
          {{ formatMoney(selected.amount, props.currency) }}
        </text>
        <text
          :x="CENTRE"
          :y="CENTRE + 12"
          text-anchor="middle"
          data-testid="centre-share"
          class="fill-[var(--text-muted)] text-[10px]"
        >
          {{ percent(selected.share) }}
        </text>
      </template>
      <text
        v-else
        :x="CENTRE"
        :y="CENTRE + 5"
        text-anchor="middle"
        data-testid="centre-total"
        class="fill-[var(--text)] text-[13px] font-semibold"
      >
        {{ formatMoney(total, props.currency) }}
      </text>
    </svg>
  </div>
</template>
<style scoped>
.wedge:focus {
  outline: none;
}

.wedge {
  -webkit-tap-highlight-color: transparent;
}
</style>
