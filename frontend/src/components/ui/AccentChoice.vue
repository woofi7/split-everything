<script setup lang="ts">
import { ACCENT_THEMES, type AccentTheme } from '@/domain/themes'

/**
 * The accent the whole application wears.
 *
 * Eight of them, drawn from their own shades so a swatch is the answer to what
 * the app will look like rather than a label promising it. Each one shows its
 * light tint and its fill together, because both are on screen everywhere: the
 * fill on buttons and the tab you are on, the tint on links and small marks.
 */

const props = defineProps<{
  /** The theme on now, by name. */
  value: string
  label: string
}>()

const emit = defineEmits<{ pick: [name: string] }>()

const isCurrent = (theme: AccentTheme) => theme.name === props.value
</script>

<template>
  <ul class="grid grid-cols-4 gap-2" :aria-label="props.label">
    <li v-for="theme in ACCENT_THEMES" :key="theme.name">
      <button
        type="button"
        :data-testid="`accent-${theme.name}`"
        class="btn-press flex w-full flex-col items-center gap-1.5 rounded-xl border p-2 text-xs"
        :style="{
          borderColor: isCurrent(theme) ? theme.shades[1] : 'var(--border)',
          background: isCurrent(theme)
            ? `color-mix(in oklab, ${theme.shades[1]} 14%, transparent)`
            : 'transparent',
        }"
        :aria-pressed="isCurrent(theme)"
        :aria-label="theme.label"
        @click="emit('pick', theme.name)"
      >
        <span
          class="h-7 w-7 rounded-full"
          :style="{
            background: `linear-gradient(140deg, ${theme.shades[0]}, ${theme.shades[2]})`,
          }"
          aria-hidden="true"
        />
        <span :class="isCurrent(theme) ? '' : 'text-[var(--text-muted)]'">
          {{ theme.label }}
        </span>
      </button>
    </li>
  </ul>
</template>
