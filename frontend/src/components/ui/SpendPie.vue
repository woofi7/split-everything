<script setup lang="ts">
import { t } from '@/i18n'
import { computed, ref } from 'vue'
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

/**
 * The slice being asked about.
 *
 * A pie says how spending is spread but never says how much, and a wedge is not
 * a label: on a phone there is no pointer to rest anywhere. So it is asked, by
 * hovering, tapping or focusing either the wedge or its name, and answered in the
 * middle of the chart where the total already sits.
 */
const hoveredId = ref<string | null>(null)
const pinnedId = ref<string | null>(null)

/** What is on show: whatever the pointer is over, else whatever was tapped. */
const selectedId = computed(() => hoveredId.value ?? pinnedId.value)

const selected = computed(() => wedges.value.find((wedge) => wedge.id === selectedId.value) ?? null)

function select(id: string): void {
  hoveredId.value = id
}

function clear(): void {
  hoveredId.value = null
}

/**
 * A tap, which is not a hover.
 *
 * Held apart from hovering because a click always arrives after the pointer is
 * already over the thing clicked: treating the two as one state made clicking a
 * second wedge look like clicking the one already chosen, so it cleared instead
 * of switching.
 *
 * The same one again puts the total back, and that has to clear the hover too, or
 * on a phone the tap emulates a hover that never ends and nothing appears to
 * happen.
 */
function toggle(id: string): void {
  pinnedId.value = pinnedId.value === id ? null : id
  hoveredId.value = pinnedId.value === null ? null : id
}

const description = computed(() =>
  wedges.value.map((wedge) => `${wedge.label} ${percent(wedge.share)}`).join(', '),
)
</script>

<template>
  <!--
    Names on the left under the heading, chart on the right. The chart is beside
    the heading as well as the list, which is what lets it be the tallest thing in
    the card without the card growing: the words fill the space it needs anyway.

    Both columns start at the top, so the heading sits where every other card's
    heading sits rather than drifting down to the middle of the chart.
  -->
  <div class="flex items-start gap-4">
    <div class="min-w-0 flex-1">
      <p v-if="$slots.heading" class="mb-3 text-sm text-[var(--text-muted)]">
        <slot name="heading" />
      </p>

      <!--
        An empty chart is not always an empty group. What "nothing here" means
        depends on what the chart counts, so the caller says it; the wording below
        is only the default.
      -->
      <p v-if="wedges.length === 0" data-testid="pie-empty" class="text-sm text-[var(--text-muted)]">
        <slot name="empty">{{ t('Nothing spent yet.') }}</slot>
      </p>

      <ul v-else class="flex min-w-0 flex-col gap-1 text-sm">
        <li v-for="wedge in wedges" :key="wedge.id">
          <!--
            A button, not a row of text. On a phone a wedge is a poor target and a
            hover does not exist, so the name is the way in: tap it and the middle
            of the chart answers. Keyboard focus does the same thing.
          -->
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

    <!--
      A group rather than an image, because the wedges inside it can be pressed:
      role="img" tells a screen reader to treat everything inside as one picture
      and hides them. The name still describes the whole chart, and each wedge
      carries its own.
    -->
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
      <!--
        Dimmed rather than moved when another is picked: a wedge that slides out
        takes the ones beside it with it, and the shape is what the chart is for.
      -->
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

      <!-- Punched out so the total can sit inside without fighting the wedges. -->
      <circle :cx="CENTRE" :cy="CENTRE" r="34" fill="var(--surface-raised)" />

      <!--
        The total, until something is picked. Then that slice, because the question
        a wedge raises is how much, and the middle is where the eye already is.
      -->
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
/*
 * A tap on a wedge must not draw a focus ring.
 *
 * Tapping one focuses it, and the ring a browser draws for a focused SVG path goes
 * around its bounding box - which for a wedge reaching the centre is a rectangle
 * over the whole chart. On a phone that is a five-pixel black and white square
 * around the pie, appearing every time somebody asks what a slice is worth.
 *
 * Nothing is lost by removing it: these carry tabindex="-1" and are not reachable
 * by keyboard. The legend buttons beside the chart do the same job, are properly
 * focusable, and keep their own visible ring.
 */
.wedge:focus {
  outline: none;
}

.wedge {
  /* The grey flash mobile browsers paint over a tapped element, on the same box. */
  -webkit-tap-highlight-color: transparent;
}
</style>
