<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { fuzzySearch } from '@/domain/fuzzySearch'
import type { AddableUser } from '@/api/types'

/**
 * Adding someone to a group.
 *
 * Two ways in, and this field is the first: pick a person who already has an
 * account. The second is an invite link, for someone who has never opened the
 * app. Anyone who is neither can still be added as a placeholder, which is what
 * a CSV import produces and what a name typed here used to produce
 * unconditionally, whether or not that person was already a user.
 *
 * Matching is fuzzy over name and email, so finding someone does not depend on
 * guessing how their name is spelled or capitalised.
 */

const props = defineProps<{
  candidates: readonly AddableUser[]
  label?: string
}>()

const emit = defineEmits<{
  pick: [person: AddableUser]
  addPlaceholder: [displayName: string]
}>()

const RESULT_LIMIT = 8

const query = ref('')
const activeIndex = ref(0)

const trimmed = computed(() => query.value.trim())

// Nothing until something is typed: a bare directory listing is noise, and on a
// phone it pushes the rest of the form off screen.
const results = computed(() =>
  trimmed.value.length === 0
    ? []
    : fuzzySearch(trimmed.value, props.candidates, searchFields, RESULT_LIMIT),
)

/** Name first, so a name match outranks an email match on the same person. */
function searchFields(person: AddableUser): readonly string[] {
  return [person.displayName, person.email]
}

const isOpen = computed(() => results.value.length > 0)

/** Offered only when nobody matches, so it never competes with a real person. */
const canAddPlaceholder = computed(
  () => trimmed.value.length > 0 && results.value.length === 0,
)

watch(results, () => {
  activeIndex.value = 0
})

function move(offset: number): void {
  if (results.value.length === 0) return

  const next = activeIndex.value + offset
  activeIndex.value = Math.max(0, Math.min(results.value.length - 1, next))
}

function pick(person: AddableUser): void {
  emit('pick', person)
  reset()
}

function addPlaceholder(): void {
  emit('addPlaceholder', trimmed.value)
  reset()
}

/** Cleared after either action, ready for the next person. */
function reset(): void {
  query.value = ''
  activeIndex.value = 0
}

function onEnter(): void {
  const active = results.value[activeIndex.value]
  if (active) pick(active.item)
  else if (canAddPlaceholder.value) addPlaceholder()
}

/** Splits a value around the matched positions, so a fuzzy hit is legible. */
function segments(value: string, indices: number[]): Array<{ text: string; matched: boolean }> {
  const matched = new Set(indices)
  const parts: Array<{ text: string; matched: boolean }> = []

  for (let i = 0; i < value.length; i++) {
    const isMatch = matched.has(i)
    const last = parts[parts.length - 1]

    if (last && last.matched === isMatch) last.text += value[i]
    else parts.push({ text: value[i], matched: isMatch })
  }

  return parts
}

function nameSegments(result: { item: AddableUser; indices: number[]; fieldIndex: number }) {
  return result.fieldIndex === 0
    ? segments(result.item.displayName, result.indices)
    : [{ text: result.item.displayName, matched: false }]
}

function emailSegments(result: { item: AddableUser; indices: number[]; fieldIndex: number }) {
  return result.fieldIndex === 1
    ? segments(result.item.email, result.indices)
    : [{ text: result.item.email, matched: false }]
}
</script>

<template>
  <div class="relative">
    <input
      v-model="query"
      type="search"
      role="combobox"
      :aria-label="props.label ?? 'Find someone to add'"
      aria-controls="person-picker-results"
      :aria-expanded="isOpen ? 'true' : 'false'"
      placeholder="Search by name or email"
      class="tap-target w-full rounded-lg border bg-[var(--surface)] px-3 text-sm"
      style="border-color: var(--border)"
      @keydown.down.prevent="move(1)"
      @keydown.up.prevent="move(-1)"
      @keydown.enter.prevent="onEnter"
      @keydown.esc.prevent="reset"
    />

    <ul
      v-if="isOpen"
      id="person-picker-results"
      role="listbox"
      class="mt-2 flex flex-col overflow-hidden rounded-lg border"
      style="border-color: var(--border)"
    >
      <li
        v-for="(result, index) in results"
        :key="result.item.id"
        data-testid="candidate"
        role="option"
        :aria-selected="index === activeIndex"
        class="tap-target flex cursor-pointer flex-col justify-center px-3 py-2 text-left"
        :class="index === activeIndex ? 'bg-brand-600/15' : ''"
        @click="pick(result.item)"
        @mousemove="activeIndex = index"
      >
        <span class="truncate text-sm">
          <template v-for="(part, i) in nameSegments(result)" :key="i">
            <mark v-if="part.matched" class="bg-transparent font-semibold text-brand-400">{{ part.text }}</mark>
            <template v-else>{{ part.text }}</template>
          </template>
        </span>
        <span class="truncate text-xs text-[var(--text-muted)]">
          <template v-for="(part, i) in emailSegments(result)" :key="i">
            <mark v-if="part.matched" class="bg-transparent font-semibold text-brand-400">{{ part.text }}</mark>
            <template v-else>{{ part.text }}</template>
          </template>
        </span>
      </li>
    </ul>

    <button
      v-if="canAddPlaceholder"
      type="button"
      data-testid="add-placeholder"
      class="btn btn-press btn-secondary mt-2 w-full justify-start"
      style="border-color: var(--border)"
      @click="addPlaceholder"
    >
      Add "{{ trimmed }}" as someone without an account
    </button>
  </div>
</template>
