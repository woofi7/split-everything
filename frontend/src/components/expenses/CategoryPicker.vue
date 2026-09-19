<script setup lang="ts">
import { t } from '@/i18n'
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { fuzzySearch } from '@/domain/fuzzySearch'
import { resolveIcon } from '@/domain/icons'
import { categoryFor, type Category } from '@/domain/categories'

const props = withDefaults(
  defineProps<{
    modelValue: string | null
    categories: Category[]
    isGuess?: boolean
    isCreating?: boolean
  }>(),
  { isGuess: false, isCreating: false },
)

const emit = defineEmits<{
  'update:modelValue': [key: string | null]
  create: [name: string]
}>()

const RESULTS = 8

const isOpen = ref(false)
const query = ref('')
const activeIndex = ref(0)
const search = useTemplateRef<HTMLInputElement>('search')
const trigger = useTemplateRef<HTMLButtonElement>('trigger')

const chosen = computed(() => categoryFor(props.modelValue, props.categories))

const label = computed(() => chosen.value?.name ?? props.modelValue ?? t('Not filed'))

const trimmed = computed(() => query.value.trim())

const results = computed(() =>
  trimmed.value.length === 0
    ? props.categories.slice(0, RESULTS).map((item) => ({ item, indices: [] as number[], fieldIndex: 0 }))
    : fuzzySearch(trimmed.value, props.categories, fields, RESULTS),
)

function fields(category: Category): readonly string[] {
  return [category.name, (category.keywords ?? []).join(' ')]
}

const canCreate = computed(
  () =>
    trimmed.value.length > 0 &&
    !props.categories.some(
      (category) => category.name.toLowerCase() === trimmed.value.toLowerCase(),
    ),
)

const rowCount = computed(() => results.value.length + (canCreate.value ? 1 : 0) + 1)

watch([results, canCreate], () => {
  activeIndex.value = 0
})

async function open(): Promise<void> {
  isOpen.value = true
  query.value = ''
  activeIndex.value = 0

  await nextTick()
  search.value?.focus()
}

function close(): void {
  isOpen.value = false
  query.value = ''
  trigger.value?.focus()
}

function move(offset: number): void {
  const next = activeIndex.value + offset
  activeIndex.value = Math.max(0, Math.min(rowCount.value - 1, next))
}

function choose(key: string | null): void {
  emit('update:modelValue', key)
  close()
}

function create(): void {
  emit('create', trimmed.value)
  close()
}

function onEnter(): void {
  if (activeIndex.value < results.value.length) {
    choose(results.value[activeIndex.value].item.key)
    return
  }

  if (canCreate.value && activeIndex.value === results.value.length) {
    create()
    return
  }

  choose(null)
}

function segments(value: string, indices: number[]): Array<{ text: string; matched: boolean }> {
  const matched = new Set(indices)
  const parts: Array<{ text: string; matched: boolean }> = []

  for (let index = 0; index < value.length; index++) {
    const isMatch = matched.has(index)
    const last = parts[parts.length - 1]

    if (last && last.matched === isMatch) last.text += value[index]
    else parts.push({ text: value[index], matched: isMatch })
  }

  return parts
}

function nameSegments(result: { item: Category; indices: number[]; fieldIndex: number }) {
  return result.fieldIndex === 0
    ? segments(result.item.name, result.indices)
    : [{ text: result.item.name, matched: false }]
}

function matchedOn(result: { item: Category; fieldIndex: number }): string | null {
  if (result.fieldIndex !== 1) return null

  const needle = trimmed.value.toLowerCase()
  return (result.item.keywords ?? []).find((word) => word.includes(needle)) ?? null
}
</script>
<template>
  <div class="relative">
    <button
      ref="trigger"
      type="button"
      data-testid="category"
      :data-category="modelValue ?? ''"
      :aria-label="t('Category')"
      class="tap-target flex w-full items-center gap-2 rounded-lg border bg-[var(--surface-raised)] px-2 text-left text-sm"
      style="border-color: var(--border)"
      :disabled="isCreating"
      @click="isOpen ? close() : open()"
    >
      <FontAwesomeIcon
        v-if="chosen"
        :icon="resolveIcon(chosen.iconName).definition"
        class="h-3.5 w-3.5 shrink-0"
        :style="{ color: chosen.colorHex }"
        aria-hidden="true"
      />
      <span class="min-w-0 flex-1 truncate" :class="chosen ? '' : 'text-[var(--text-muted)]'">
        {{ isCreating ? t('Adding') : label }}
      </span>
      <span class="shrink-0 text-xs text-[var(--text-muted)]" aria-hidden="true">&#9662;</span>
    </button>
    <div
      v-if="isOpen"
      class="absolute inset-x-0 z-20 mt-1 overflow-hidden rounded-lg border shadow-lg"
      style="background: var(--surface-raised); border-color: var(--border)"
    >
      <input
        ref="search"
        v-model="query"
        type="search"
        role="combobox"
        aria-controls="category-picker-results"
        :aria-expanded="isOpen ? 'true' : 'false'"
        data-testid="category-search"
        :placeholder="t('Search or add')"
        class="tap-target w-full border-b bg-transparent px-3 text-sm"
        style="border-color: var(--border)"
        @keydown.down.prevent="move(1)"
        @keydown.up.prevent="move(-1)"
        @keydown.enter.prevent="onEnter"
        @keydown.esc.prevent="close"
      />
      <ul id="category-picker-results" role="listbox" class="max-h-64 overflow-y-auto">
        <li v-for="(result, index) in results" :key="result.item.key" role="presentation">
          <button
            type="button"
            data-testid="category-option"
            :data-category="result.item.key"
            role="option"
            :aria-selected="index === activeIndex"
            class="tap-target flex w-full items-center gap-2 px-3 py-2 text-left text-sm"
            :class="index === activeIndex ? 'bg-brand-600/15' : ''"
            @click="choose(result.item.key)"
            @mousemove="activeIndex = index"
          >
            <FontAwesomeIcon
              :icon="resolveIcon(result.item.iconName).definition"
              class="h-3.5 w-3.5 shrink-0"
              :style="{ color: result.item.colorHex }"
              aria-hidden="true"
            />
            <span class="min-w-0 flex-1 truncate">
              <template v-for="(part, at) in nameSegments(result)" :key="at">
                <mark v-if="part.matched" class="bg-transparent font-semibold text-accent">{{ part.text }}</mark>
                <template v-else>{{ part.text }}</template>
              </template>
            </span>
            <span
              v-if="matchedOn(result)"
              class="shrink-0 truncate text-xs text-[var(--text-muted)]"
            >{{ matchedOn(result) }}
            </span>
          </button>
        </li>
        <li v-if="canCreate" role="presentation">
          <button
            type="button"
            data-testid="category-create"
            role="option"
            :aria-selected="activeIndex === results.length"
            class="tap-target flex w-full items-center gap-2 border-t px-3 py-2 text-left text-sm text-accent"
            style="border-color: var(--border)"
            :class="activeIndex === results.length ? 'bg-brand-600/15' : ''"
            @click="create"
            @mousemove="activeIndex = results.length"
          >
            <span aria-hidden="true">+</span>
            <span class="min-w-0 flex-1 truncate">{{ t('Add "{name}"', { name: trimmed }) }}</span>
          </button>
        </li>
        <li role="presentation">
          <button
            type="button"
            data-testid="category-none"
            role="option"
            :aria-selected="activeIndex === rowCount - 1"
            class="tap-target flex w-full items-center px-3 py-2 text-left text-sm text-[var(--text-muted)]"
            :class="activeIndex === rowCount - 1 ? 'bg-brand-600/15' : ''"
            @click="choose(null)"
            @mousemove="activeIndex = rowCount - 1"
          >{{ t('Not filed') }}
          </button>
        </li>
      </ul>
    </div>
    <div v-if="isOpen" class="fixed inset-0 z-10" aria-hidden="true" @click="close" />
  </div>
</template>
