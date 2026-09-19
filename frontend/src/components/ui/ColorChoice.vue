<script setup lang="ts">
import { MEMBER_COLORS } from '@/domain/memberColors'

const props = defineProps<{
  value?: string | null
  taken?: readonly string[]
  disabled?: boolean
  label?: string
}>()

const emit = defineEmits<{ pick: [colorHex: string] }>()

const NAMES: Record<string, string> = {
  '#6366f1': 'Indigo',
  '#f97316': 'Orange',
  '#14b8a6': 'Teal',
  '#ec4899': 'Pink',
  '#84cc16': 'Lime',
  '#8b5cf6': 'Violet',
  '#f59e0b': 'Amber',
  '#06b6d4': 'Cyan',
  '#ef4444': 'Red',
  '#22c55e': 'Green',
  '#a855f7': 'Purple',
  '#eab308': 'Yellow',
}

const isChosen = (colour: string) =>
  (props.value ?? '').toLowerCase() === colour.toLowerCase()

const isTaken = (colour: string) =>
  !isChosen(colour) &&
  (props.taken ?? []).some((other) => other?.toLowerCase() === colour.toLowerCase())

const describe = (colour: string) =>
  isTaken(colour) ? `${NAMES[colour] ?? colour}, already taken` : (NAMES[colour] ?? colour)
</script>
<template>
  <div class="flex flex-wrap gap-2" role="group" :aria-label="label ?? 'Colour'">
    <button
      v-for="colour in MEMBER_COLORS"
      :key="colour"
      type="button"
      :data-testid="`colour-${colour.slice(1)}`"
      :disabled="disabled"
      :aria-pressed="isChosen(colour)"
      :aria-label="describe(colour)"
      :title="describe(colour)"
      class="h-7 w-7 rounded-full transition-transform active:scale-95 disabled:opacity-50"
      :class="isChosen(colour) ? 'ring-2 ring-offset-2' : ''"
      :style="{
        backgroundColor: colour,
        opacity: isTaken(colour) ? 0.35 : 1,
        '--tw-ring-color': 'var(--text)',
        '--tw-ring-offset-color': 'var(--surface-raised)',
      }"
      @click="emit('pick', colour)"
    />
  </div>
</template>
