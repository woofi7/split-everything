<script setup lang="ts" generic="T extends string | null">
import { t } from '@/i18n'
import { computed, nextTick, onBeforeUnmount, ref, useTemplateRef, watch } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { fuzzySearch } from '@/domain/fuzzySearch'
import type { SelectOption } from '@/components/ui/selectOption'

const SEARCH_FROM = 8
const DESKTOP_ROOM = 320

const props = withDefaults(
  defineProps<{
    modelValue: T
    options: readonly SelectOption<T>[]
    title: string
    testid?: string
    placeholder?: string
    searchable?: boolean | null
    creatable?: boolean
    disabled?: boolean
    size?: 'md' | 'sm'
  }>(),
  { testid: undefined, placeholder: '', searchable: null, creatable: false, disabled: false, size: 'md' },
)

const emit = defineEmits<{
  'update:modelValue': [value: T]
  create: [name: string]
}>()

defineOptions({ inheritAttrs: false })

const isOpen = ref(false)
const query = ref('')
const activeIndex = ref(0)
const placement = ref<Record<string, string>>({})
const triggerWrap = useTemplateRef<HTMLElement>('triggerWrap')
const search = useTemplateRef<HTMLInputElement>('search')
const menu = useTemplateRef<HTMLElement>('menu')

const chosen = computed(() => props.options.find((option) => option.value === props.modelValue) ?? null)

const isSearchable = computed(() => props.searchable ?? (props.creatable || props.options.length >= SEARCH_FROM))

const trimmed = computed(() => query.value.trim())

const choices = computed(() => props.options.filter((option) => !option.disabled))

const results = computed(() =>
  trimmed.value.length === 0
    ? choices.value.map((item) => ({ item, indices: [] as number[], fieldIndex: 0 }))
    : fuzzySearch(trimmed.value, choices.value, fields),
)

function fields(option: SelectOption<T>): readonly string[] {
  const keywords = option.keywords ?? []
  const words = keywords.flatMap((keyword) => keyword.split(/\s+/)).filter((word) => !keywords.includes(word))

  return [option.label, option.hint ?? '', ...keywords, ...words]
}

const canCreate = computed(
  () =>
    props.creatable &&
    trimmed.value.length > 0 &&
    !props.options.some((option) => option.label.toLowerCase() === trimmed.value.toLowerCase()),
)

const rowCount = computed(() => results.value.length + (canCreate.value ? 1 : 0))

watch([results, canCreate], () => {
  activeIndex.value = 0
})

function place(): void {
  const rect = triggerWrap.value?.getBoundingClientRect()
  if (!rect) return

  const width = Math.max(rect.width, 256)
  const left = Math.max(8, Math.min(rect.right - width, window.innerWidth - width - 8))
  const below = window.innerHeight - rect.bottom
  const opensUp = below < DESKTOP_ROOM && rect.top > below

  placement.value = {
    '--menu-left': `${left}px`,
    '--menu-width': `${width}px`,
    '--menu-top': opensUp ? 'auto' : `${rect.bottom + 4}px`,
    '--menu-bottom': opensUp ? `${window.innerHeight - rect.top + 4}px` : 'auto',
    '--menu-room': `${Math.max(160, (opensUp ? rect.top : below) - 16)}px`,
  }
}

async function open(): Promise<void> {
  if (props.disabled) return

  place()
  isOpen.value = true
  query.value = ''
  activeIndex.value =
    props.modelValue === null
      ? 0
      : Math.max(0, results.value.findIndex((result) => result.item.value === props.modelValue))
  window.addEventListener('resize', place)

  await nextTick()
  if (isSearchable.value) search.value?.focus()
  else menu.value?.focus()
  scrollActiveIntoView()
}

function close(): void {
  if (!isOpen.value) return

  isOpen.value = false
  query.value = ''
  window.removeEventListener('resize', place)
  triggerWrap.value?.querySelector<HTMLElement>('button')?.focus()
}

onBeforeUnmount(() => window.removeEventListener('resize', place))

function toggle(): void {
  if (isOpen.value) close()
  else void open()
}

function move(offset: number): void {
  activeIndex.value = Math.max(0, Math.min(rowCount.value - 1, activeIndex.value + offset))
  scrollActiveIntoView()
}

function scrollActiveIntoView(): void {
  void nextTick(() => {
    menu.value
      ?.querySelector(`[data-row="${activeIndex.value}"]`)
      ?.scrollIntoView?.({ block: 'nearest' })
  })
}

function choose(value: T): void {
  emit('update:modelValue', value)
  close()
}

function create(): void {
  emit('create', trimmed.value)
  close()
}

function onEnter(): void {
  const result = results.value[activeIndex.value]
  if (result) {
    choose(result.item.value)
    return
  }
  if (canCreate.value) create()
}

function segments(value: string, indices: readonly number[]): Array<{ text: string; matched: boolean }> {
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

function labelSegments(result: { item: SelectOption<T>; indices: readonly number[]; fieldIndex: number }) {
  return result.fieldIndex === 0
    ? segments(result.item.label, result.indices)
    : [{ text: result.item.label, matched: false }]
}

function hintOf(result: { item: SelectOption<T>; fieldIndex: number }): string | undefined {
  if (result.fieldIndex < 2) return result.item.hint

  const needle = trimmed.value.toLowerCase()
  return result.item.keywords?.find((word) => word.toLowerCase().includes(needle)) ?? result.item.hint
}

function groupHeading(index: number): string | null {
  if (trimmed.value.length > 0) return null

  const group = results.value[index].item.group
  if (!group) return null
  return index === 0 || results.value[index - 1].item.group !== group ? group : null
}

function optionTestid(option: SelectOption<T>): string | undefined {
  if (!props.testid) return undefined
  return option.value === null ? `${props.testid}-none` : `${props.testid}-option`
}

defineExpose({ open, close })
</script>
<template>
  <div ref="triggerWrap" v-bind="$attrs">
    <slot name="trigger" :open="isOpen" :chosen="chosen" :toggle="toggle">
      <button
        type="button"
        :data-testid="testid"
        :data-value="modelValue ?? ''"
        :aria-label="title"
        aria-haspopup="listbox"
        :aria-expanded="isOpen ? 'true' : 'false'"
        :disabled="disabled"
        class="flex w-full items-center gap-2 rounded-lg border text-left"
        :class="size === 'sm' ? 'px-2 py-1 text-xs' : 'tap-target px-2 text-sm'"
        style="border-color: var(--border); background: var(--surface-raised)"
        @click="toggle"
      >
        <FontAwesomeIcon
          v-if="chosen?.icon"
          :icon="chosen.icon"
          class="h-3.5 w-3.5 shrink-0"
          :style="{ color: chosen.iconColor }"
          aria-hidden="true"
        />
        <span class="min-w-0 flex-1 truncate" :class="chosen && chosen.value !== null ? '' : 'text-[var(--text-muted)]'">
          <slot name="label" :chosen="chosen">{{ chosen?.label ?? placeholder }}</slot>
        </span>
        <span class="shrink-0 text-xs text-[var(--text-muted)]" aria-hidden="true">&#9662;</span>
      </button>
    </slot>
  </div>
  <Teleport to="body">
    <template v-if="isOpen">
      <div class="fixed inset-0 z-40 hidden sm:block" aria-hidden="true" @click="close" />
      <div
        ref="menu"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        tabindex="-1"
        :data-testid="testid ? `${testid}-menu` : undefined"
        class="fixed inset-0 z-50 flex flex-col overflow-hidden bg-[var(--surface-raised)] pt-[env(safe-area-inset-top)] pb-[env(safe-area-inset-bottom)] outline-none sm:inset-auto sm:top-(--menu-top) sm:bottom-(--menu-bottom) sm:left-(--menu-left) sm:max-h-[min(20rem,var(--menu-room))] sm:w-(--menu-width) sm:rounded-lg sm:border sm:p-0 sm:shadow-lg"
        style="border-color: var(--border)"
        :style="placement"
        @keydown.down.prevent="move(1)"
        @keydown.up.prevent="move(-1)"
        @keydown.enter.prevent="onEnter"
        @keydown.esc.prevent="close"
      >
        <div class="flex items-center justify-between gap-3 border-b px-4 py-3 sm:hidden" style="border-color: var(--border)">
          <h2 class="truncate text-base font-semibold">{{ title }}</h2>
          <button type="button" class="btn btn-press btn-quiet min-h-0 px-2 py-1 text-xs" @click="close">{{ t('Close') }}
          </button>
        </div>
        <input
          v-if="isSearchable"
          ref="search"
          v-model="query"
          type="search"
          role="combobox"
          :aria-expanded="isOpen ? 'true' : 'false'"
          :data-testid="testid ? `${testid}-search` : undefined"
          :placeholder="creatable ? t('Search or add') : t('Search')"
          class="tap-target w-full shrink-0 border-b bg-transparent px-4 text-base sm:px-3 sm:text-sm"
          style="border-color: var(--border)"
        />
        <ul role="listbox" class="min-h-0 flex-1 overflow-y-auto">
          <template v-for="(result, index) in results" :key="result.item.value ?? '\u0000none'">
            <li
              v-if="groupHeading(index)"
              role="presentation"
              class="px-4 pt-3 pb-1 text-xs text-[var(--text-muted)] sm:px-3"
            >{{ groupHeading(index) }}
            </li>
            <li role="presentation">
              <button
                type="button"
                :data-testid="optionTestid(result.item)"
                :data-value="result.item.value ?? ''"
                :data-row="index"
                role="option"
                :aria-selected="result.item.value === modelValue"
                class="tap-target flex w-full items-center gap-3 px-4 py-2 text-left text-base sm:px-3 sm:text-sm"
                :class="[
                  index === activeIndex ? 'bg-brand-600/15' : '',
                  result.item.value === null ? 'text-[var(--text-muted)]' : '',
                ]"
                @click="choose(result.item.value)"
                @mousemove="activeIndex = index"
              >
                <FontAwesomeIcon
                  v-if="result.item.icon"
                  :icon="result.item.icon"
                  class="h-3.5 w-3.5 shrink-0"
                  :style="{ color: result.item.iconColor }"
                  aria-hidden="true"
                />
                <span class="min-w-0 flex-1 truncate" :class="result.item.value === modelValue ? 'font-medium text-accent' : ''">
                  <template v-for="(part, at) in labelSegments(result)" :key="at">
                    <mark v-if="part.matched" class="bg-transparent font-semibold text-accent">{{ part.text }}</mark>
                    <template v-else>{{ part.text }}</template>
                  </template>
                </span>
                <span v-if="hintOf(result)" class="shrink-0 truncate text-xs text-[var(--text-muted)]">
                  {{ hintOf(result) }}
                </span>
              </button>
            </li>
          </template>
          <li v-if="canCreate" role="presentation">
            <button
              type="button"
              :data-testid="testid ? `${testid}-create` : undefined"
              :data-row="results.length"
              role="option"
              :aria-selected="false"
              class="tap-target flex w-full items-center gap-2 border-t px-4 py-2 text-left text-base text-accent sm:px-3 sm:text-sm"
              style="border-color: var(--border)"
              :class="activeIndex === results.length ? 'bg-brand-600/15' : ''"
              @click="create"
              @mousemove="activeIndex = results.length"
            >
              <span aria-hidden="true">+</span>
              <span class="min-w-0 flex-1 truncate">{{ t('Add "{name}"', { name: trimmed }) }}</span>
            </button>
          </li>
          <li v-if="rowCount === 0" class="px-4 py-3 text-sm text-[var(--text-muted)] sm:px-3">{{ t('Nothing matches') }}
          </li>
        </ul>
      </div>
    </template>
  </Teleport>
</template>
