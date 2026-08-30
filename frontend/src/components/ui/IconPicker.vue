<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useTemplateRef, watch } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { ICONS, iconSearchFields, resolveIcon, type IconChoice } from '@/domain/icons'
import { fuzzySearch } from '@/domain/fuzzySearch'

const props = defineProps<{
  open: boolean
  /** Currently chosen Font Awesome name, if any. */
  modelValue?: string | null
  title?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [name: string | null]
  close: []
}>()

const query = ref('')
const activeIndex = ref(0)

const searchInput = useTemplateRef<HTMLInputElement>('searchInput')
const dialog = useTemplateRef<HTMLElement>('dialog')

/** The element that had focus before opening, so it can be given it back. */
let previouslyFocused: HTMLElement | null = null

const results = computed(() =>
  fuzzySearch(query.value, ICONS, iconSearchFields).map((result) => ({
    icon: result.item,
    indices: result.indices,
    fieldIndex: result.fieldIndex,
  })),
)

/**
 * Sections, but only when nothing has been typed.
 *
 * Once there is a query the order is relevance, and grouping would fight it by
 * pushing a strong match below a weaker one in an earlier section.
 */
const sections = computed(() => {
  if (query.value.trim().length > 0) {
    return [{ heading: null as string | null, icons: results.value }]
  }

  const grouped: Array<{ heading: string | null; icons: typeof results.value }> = []
  for (const result of results.value) {
    const last = grouped[grouped.length - 1]
    if (last && last.heading === result.icon.group) last.icons.push(result)
    else grouped.push({ heading: result.icon.group, icons: [result] })
  }
  return grouped
})

/** Flat order, which is what the arrow keys walk. */
const flat = computed(() => sections.value.flatMap((section) => section.icons))

const activeIcon = computed(() => flat.value[activeIndex.value]?.icon ?? null)

watch(
  () => props.open,
  async (isOpen) => {
    if (!isOpen) {
      restoreFocus()
      return
    }

    previouslyFocused = document.activeElement as HTMLElement | null
    query.value = ''
    activeIndex.value = Math.max(
      0,
      ICONS.findIndex((icon) => icon.name === props.modelValue),
    )

    // Focus the search box, not the grid: typing is the fast path.
    await nextTick()
    searchInput.value?.focus()
  },
  // immediate, so a picker mounted already open still highlights the current
  // selection and takes focus rather than sitting inert.
  { immediate: true },
)

// Retyping should not leave the highlight pointing at a row that scrolled away.
watch(query, () => {
  activeIndex.value = 0
})

onBeforeUnmount(restoreFocus)

function restoreFocus(): void {
  previouslyFocused?.focus?.()
  previouslyFocused = null
}

function close(): void {
  emit('close')
}

function choose(icon: IconChoice): void {
  emit('update:modelValue', icon.name)
  emit('close')
}

function clear(): void {
  emit('update:modelValue', null)
  emit('close')
}

function move(delta: number): void {
  const total = flat.value.length
  if (total === 0) return

  // Wraps, so holding an arrow key never dead-ends.
  activeIndex.value = (activeIndex.value + delta + total) % total
  scrollActiveIntoView()
}

async function scrollActiveIntoView(): Promise<void> {
  await nextTick()

  const active = dialog.value?.querySelector('[data-active="true"]')
  // Guarded because scrolling is a nicety: not every environment implements it,
  // and keyboard navigation must not depend on it working.
  if (active && typeof active.scrollIntoView === 'function') {
    active.scrollIntoView({ block: 'nearest' })
  }
}

/** Columns in the grid, so up and down move a row rather than one cell. */
const COLUMNS = 6

function onKeydown(event: KeyboardEvent): void {
  switch (event.key) {
    case 'Escape':
      event.preventDefault()
      close()
      return
    case 'ArrowRight':
      event.preventDefault()
      move(1)
      return
    case 'ArrowLeft':
      event.preventDefault()
      move(-1)
      return
    case 'ArrowDown':
      event.preventDefault()
      move(COLUMNS)
      return
    case 'ArrowUp':
      event.preventDefault()
      move(-COLUMNS)
      return
    case 'Enter':
      if (activeIcon.value) {
        event.preventDefault()
        choose(activeIcon.value)
      }
      return
    case 'Tab':
      trapFocus(event)
      return
    default:
      return
  }
}

/**
 * Keeps Tab inside the dialog.
 *
 * Without this, tabbing walks out into the page behind, which for a modal means
 * a keyboard user can operate controls they cannot see.
 */
function trapFocus(event: KeyboardEvent): void {
  const focusable = dialog.value?.querySelectorAll<HTMLElement>(
    'button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )
  if (!focusable || focusable.length === 0) return

  const first = focusable[0]
  const last = focusable[focusable.length - 1]

  if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  } else if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  }
}

/** Highlights the characters the search matched, so a hit is explainable. */
function highlight(text: string, indices: number[]): Array<{ text: string; match: boolean }> {
  if (indices.length === 0) return [{ text, match: false }]

  const parts: Array<{ text: string; match: boolean }> = []
  const matched = new Set(indices)
  let buffer = ''
  let bufferMatched = matched.has(0)

  for (let i = 0; i < text.length; i++) {
    const isMatch = matched.has(i)
    if (isMatch !== bufferMatched && buffer) {
      parts.push({ text: buffer, match: bufferMatched })
      buffer = ''
    }
    bufferMatched = isMatch
    buffer += text[i]
  }
  if (buffer) parts.push({ text: buffer, match: bufferMatched })

  return parts
}

/** The text to show under an icon: whichever field the search actually matched. */
function captionFor(result: { icon: IconChoice; fieldIndex: number; indices: number[] }) {
  const fields = iconSearchFields(result.icon)
  const text = fields[result.fieldIndex] ?? result.icon.label

  return { parts: highlight(text, result.indices), isKeyword: result.fieldIndex > 0 }
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-end justify-center bg-black/60 p-0 sm:items-center sm:p-4"
      @keydown="onKeydown"
    >
      <!-- The backdrop closes, which is what tapping outside a sheet should do. -->
      <div class="absolute inset-0" aria-hidden="true" @click="close" />

      <div
        ref="dialog"
        class="relative flex max-h-[85vh] w-full max-w-lg flex-col rounded-t-2xl border bg-[var(--surface-raised)] sm:rounded-2xl"
        style="border-color: var(--border)"
        role="dialog"
        aria-modal="true"
        aria-labelledby="icon-picker-title"
      >
        <header class="flex items-center justify-between gap-3 border-b p-4" style="border-color: var(--border)">
          <h2 id="icon-picker-title" class="text-base font-semibold">
            {{ title ?? 'Choose an icon' }}
          </h2>
          <button
            type="button"
            class="tap-target rounded-lg px-2 text-sm text-[var(--text-muted)]"
            aria-label="Close"
            @click="close"
          >
            Close
          </button>
        </header>

        <div class="p-4 pb-2">
          <label class="sr-only" for="icon-search">Search icons</label>
          <input
            id="icon-search"
            ref="searchInput"
            v-model="query"
            type="search"
            autocomplete="off"
            placeholder="Search: rent, groceries, uber, wifi"
            class="tap-target w-full rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
            role="combobox"
            aria-controls="icon-results"
            aria-expanded="true"
            :aria-activedescendant="activeIcon ? `icon-option-${activeIcon.name}` : undefined"
          />
          <p class="mt-2 text-xs text-[var(--text-muted)]" aria-live="polite">
            {{ results.length }} {{ results.length === 1 ? 'icon' : 'icons' }}
          </p>
        </div>

        <div id="icon-results" class="flex-1 overflow-y-auto px-4 pb-2" role="listbox" aria-label="Icons">
          <template v-for="section in sections" :key="section.heading ?? 'results'">
            <h3
              v-if="section.heading"
              class="sticky top-0 bg-[var(--surface-raised)] py-2 text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]"
            >
              {{ section.heading }}
            </h3>

            <ul class="mb-2 grid grid-cols-6 gap-1">
              <li v-for="result in section.icons" :key="result.icon.name">
                <button
                  :id="`icon-option-${result.icon.name}`"
                  type="button"
                  role="option"
                  :aria-selected="result.icon.name === modelValue"
                  :data-active="result.icon.name === activeIcon?.name ? 'true' : 'false'"
                  :data-icon="result.icon.name"
                  :title="`${result.icon.label} (${result.icon.name})`"
                  class="tap-target flex w-full flex-col items-center gap-1 rounded-lg border p-2 text-[10px] leading-tight"
                  :class="
                    result.icon.name === modelValue
                      ? 'border-brand-500 text-brand-400'
                      : result.icon.name === activeIcon?.name
                        ? 'border-[var(--color-ink-500)]'
                        : 'border-transparent text-[var(--text-muted)]'
                  "
                  @click="choose(result.icon)"
                  @mouseenter="activeIndex = flat.indexOf(result)"
                >
                  <FontAwesomeIcon :icon="result.icon.definition" class="h-5 w-5" aria-hidden="true" />
                  <span class="w-full truncate">
                    <span v-for="(part, index) in captionFor(result).parts" :key="index">
                      <mark v-if="part.match" class="bg-transparent font-semibold text-brand-400">{{ part.text }}</mark>
                      <template v-else>{{ part.text }}</template>
                    </span>
                  </span>
                </button>
              </li>
            </ul>
          </template>

          <p v-if="results.length === 0" class="py-8 text-center text-sm text-[var(--text-muted)]">
            No icon matches that. Try a plainer word, like food or travel.
          </p>
        </div>

        <footer
          class="flex items-center justify-between gap-3 border-t p-4 pb-[max(1rem,env(safe-area-inset-bottom))]"
          style="border-color: var(--border)"
        >
          <span class="flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <FontAwesomeIcon :icon="resolveIcon(modelValue).definition" class="h-4 w-4" aria-hidden="true" />
            {{ modelValue ? resolveIcon(modelValue).label : 'No icon' }}
          </span>
          <button
            type="button"
            class="btn btn-press btn-secondary"
            style="border-color: var(--border)"
            :disabled="!modelValue"
            @click="clear"
          >
            Remove icon
          </button>
        </footer>
      </div>
    </div>
  </Teleport>
</template>
