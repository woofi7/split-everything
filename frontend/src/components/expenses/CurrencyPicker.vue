<script setup lang="ts">
import { intlLocale, t } from '@/i18n'
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faCoins } from '@fortawesome/free-solid-svg-icons'
import { fuzzySearch } from '@/domain/fuzzySearch'

const props = defineProps<{
  modelValue: string
  currencies: string[]
  baseCurrency: string
}>()

const emit = defineEmits<{
  'update:modelValue': [code: string]
}>()

const isOpen = ref(false)
const query = ref('')
const activeIndex = ref(0)
const search = useTemplateRef<HTMLInputElement>('search')
const trigger = useTemplateRef<HTMLButtonElement>('trigger')

const isForeign = computed(() => props.modelValue !== props.baseCurrency)

const names = computed(() => {
  try {
    const display = new Intl.DisplayNames([intlLocale.value], { type: 'currency' })
    return Object.fromEntries(props.currencies.map((code) => [code, display.of(code) ?? code]))
  } catch {
    return Object.fromEntries(props.currencies.map((code) => [code, code]))
  }
})

const trimmed = computed(() => query.value.trim())

const results = computed(() =>
  trimmed.value.length === 0
    ? props.currencies.map((item) => ({ item, indices: [] as number[], fieldIndex: 0 }))
    : fuzzySearch(trimmed.value, props.currencies, (code) => [code, names.value[code] ?? code]),
)

watch(results, () => {
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
  activeIndex.value = Math.max(0, Math.min(results.value.length - 1, next))
}

function choose(code: string): void {
  emit('update:modelValue', code)
  close()
}

function onEnter(): void {
  const result = results.value[activeIndex.value]
  if (result) choose(result.item)
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

function codeSegments(result: { item: string; indices: number[]; fieldIndex: number }) {
  return result.fieldIndex === 0 ? segments(result.item, result.indices) : [{ text: result.item, matched: false }]
}

function nameSegments(result: { item: string; indices: number[]; fieldIndex: number }) {
  const name = names.value[result.item] ?? result.item
  return result.fieldIndex === 1 ? segments(name, result.indices) : [{ text: name, matched: false }]
}
</script>
<template>
  <div class="absolute inset-y-1 right-1">
    <button
      ref="trigger"
      type="button"
      data-testid="currency"
      :data-currency="modelValue"
      :aria-label="t('Currency')"
      class="flex h-full items-center gap-1.5 rounded-md px-2 text-sm font-medium"
      :class="isForeign ? 'bg-[var(--surface-sunken)] text-accent' : 'text-[var(--text-muted)]'"
      @click="isOpen ? close() : open()"
    >
      <FontAwesomeIcon :icon="faCoins" class="h-3.5 w-3.5" aria-hidden="true" />
      {{ modelValue }}
      <span class="text-xs" aria-hidden="true">&#9662;</span>
    </button>
    <div
      v-if="isOpen"
      class="absolute right-0 z-20 mt-2 w-64 overflow-hidden rounded-lg border shadow-lg"
      style="background: var(--surface-raised); border-color: var(--border)"
    >
      <input
        ref="search"
        v-model="query"
        type="search"
        role="combobox"
        aria-controls="currency-picker-results"
        :aria-expanded="isOpen ? 'true' : 'false'"
        data-testid="currency-search"
        :placeholder="t('Search')"
        class="tap-target w-full border-b bg-transparent px-3 text-sm"
        style="border-color: var(--border)"
        @keydown.down.prevent="move(1)"
        @keydown.up.prevent="move(-1)"
        @keydown.enter.prevent="onEnter"
        @keydown.esc.prevent="close"
      />
      <ul id="currency-picker-results" role="listbox" class="max-h-64 overflow-y-auto">
        <li v-for="(result, index) in results" :key="result.item" role="presentation">
          <button
            type="button"
            data-testid="currency-option"
            :data-currency="result.item"
            role="option"
            :aria-selected="index === activeIndex"
            class="tap-target flex w-full items-center gap-3 px-3 py-2 text-left text-sm"
            :class="index === activeIndex ? 'bg-brand-600/15' : ''"
            @click="choose(result.item)"
            @mousemove="activeIndex = index"
          >
            <span class="w-9 shrink-0 font-medium tabular-nums" :class="result.item === modelValue ? 'text-accent' : ''">
              <template v-for="(part, at) in codeSegments(result)" :key="at">
                <mark v-if="part.matched" class="bg-transparent font-semibold text-accent">{{ part.text }}</mark>
                <template v-else>{{ part.text }}</template>
              </template>
            </span>
            <span class="min-w-0 flex-1 truncate text-[var(--text-muted)]">
              <template v-for="(part, at) in nameSegments(result)" :key="at">
                <mark v-if="part.matched" class="bg-transparent font-semibold text-accent">{{ part.text }}</mark>
                <template v-else>{{ part.text }}</template>
              </template>
            </span>
            <span
              v-if="result.item === baseCurrency"
              class="shrink-0 text-xs text-[var(--text-muted)]"
            >{{ t('Group') }}
            </span>
          </button>
        </li>
      </ul>
    </div>
    <div v-if="isOpen" class="fixed inset-0 z-10" aria-hidden="true" @click="close" />
  </div>
</template>
