<script setup lang="ts">
import { t } from '@/i18n'
import { computed, ref, watch } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faChevronDown, faChevronUp, faXmark } from '@fortawesome/free-solid-svg-icons'
import { resolveIcon } from '@/domain/icons'
import type { Category } from '@/domain/categories'
import type { CategoryDraft } from '@/stores/groups'

/**
 * The list of things an expense can be filed under, edited.
 *
 * One component for both lists there are: a group's own, and the server's, which
 * every group starts from. They are the same list with a different owner, and two
 * editors would be two places to fix the next thing wrong with it.
 *
 * The keywords are the point. A category nobody has to choose is worth having; a
 * dropdown on every expense form is the thing that got categories deleted from
 * this app in the first place, so the words that fill it in sit right beside the
 * name rather than behind anything.
 */

const props = defineProps<{
  categories: Category[]
  /** Said above the list, because who this list belongs to is the whole question. */
  description: string
  isSaving?: boolean
}>()

const emit = defineEmits<{ save: [CategoryDraft[]]; revert: [] }>()

interface Row {
  key?: string
  name: string
  iconName: string
  colorHex: string
  keywords: string
}

const MOST = 30

const rows = ref<Row[]>([])

/** Reads the list into the form, which is also how Cancel works. */
function readFromProps(): void {
  rows.value = props.categories.map((category) => ({
    key: category.key,
    name: category.name,
    iconName: category.iconName,
    colorHex: category.colorHex,
    keywords: (category.keywords ?? []).join(', '),
  }))
}

watch(() => props.categories, readFromProps, { immediate: true, deep: true })

function add(): void {
  // No key: this one does not exist yet, and the server makes one from the name.
  rows.value = [...rows.value, { name: '', iconName: 'tag', colorHex: '#64748b', keywords: '' }]
}

function remove(index: number): void {
  rows.value = rows.value.filter((_, at) => at !== index)
}

/** Up and down rather than a drag: the order is the order they are shown in. */
function move(index: number, by: number): void {
  const next = index + by
  if (next < 0 || next >= rows.value.length) return

  const reordered = [...rows.value]
  const [row] = reordered.splice(index, 1)
  reordered.splice(next, 0, row)
  rows.value = reordered
}

const isDirty = computed(() => JSON.stringify(rows.value) !== JSON.stringify(asRows(props.categories)))

function asRows(categories: Category[]): Row[] {
  return categories.map((category) => ({
    key: category.key,
    name: category.name,
    iconName: category.iconName,
    colorHex: category.colorHex,
    keywords: (category.keywords ?? []).join(', '),
  }))
}

function save(): void {
  emit(
    'save',
    rows.value
      .filter((row) => row.name.trim().length > 0)
      .map((row) => ({
        key: row.key,
        name: row.name.trim(),
        iconName: row.iconName.trim() || 'tag',
        colorHex: row.colorHex,
        keywords: row.keywords
          .split(',')
          .map((word) => word.trim())
          .filter(Boolean),
      })),
  )
}

function revert(): void {
  readFromProps()
  emit('revert')
}
</script>

<template>
  <div>
    <p class="mb-3 text-xs text-[var(--text-muted)]">{{ description }}</p>

    <ul class="flex flex-col gap-3">
      <li
        v-for="(row, index) in rows"
        :key="row.key ?? `new-${index}`"
        data-testid="category-row"
        class="rounded-lg border p-3"
        style="border-color: var(--border)"
      >
        <div class="flex items-center gap-2">
          <span
            class="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-white"
            :style="{ backgroundColor: row.colorHex }"
            aria-hidden="true"
          >
            <FontAwesomeIcon :icon="resolveIcon(row.iconName).definition" class="h-3.5 w-3.5" />
          </span>

          <input
            v-model="row.name"
            type="text"
            data-testid="category-name"
            maxlength="60"
            :placeholder="t('Name')"
            class="tap-target min-w-0 flex-1 rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
            style="border-color: var(--border)"
          />

          <!--
            Drawn rather than typed. A text arrow at the size of the label beside
            it is a mark on the screen, not a control: these are pressed with a
            thumb, so they are icons in a square somebody can see the edge of.
          -->
          <button
            type="button"
            data-testid="category-up"
            class="btn-press flex h-11 w-9 shrink-0 items-center justify-center rounded-lg border text-[var(--text-muted)]"
            style="border-color: var(--border)"
            :aria-label="t('Move up')"
            :title="t('Move up')"
            @click="move(index, -1)"
          >
            <FontAwesomeIcon :icon="faChevronUp" class="h-4 w-4" />
          </button>
          <button
            type="button"
            data-testid="category-down"
            class="btn-press flex h-11 w-9 shrink-0 items-center justify-center rounded-lg border text-[var(--text-muted)]"
            style="border-color: var(--border)"
            :aria-label="t('Move down')"
            :title="t('Move down')"
            @click="move(index, 1)"
          >
            <FontAwesomeIcon :icon="faChevronDown" class="h-4 w-4" />
          </button>
          <button
            type="button"
            data-testid="category-remove"
            class="btn-press flex h-11 w-9 shrink-0 items-center justify-center rounded-lg border text-owing"
            style="border-color: var(--border)"
            :aria-label="t('Remove')"
            :title="t('Remove')"
            @click="remove(index)"
          >
            <FontAwesomeIcon :icon="faXmark" class="h-4 w-4" />
          </button>
        </div>

        <div class="mt-2 flex items-center gap-2">
          <input
            v-model="row.iconName"
            type="text"
            data-testid="category-icon"
            maxlength="48"
            placeholder="cart-shopping"
            class="tap-target w-32 shrink-0 rounded-lg border bg-[var(--surface-raised)] px-2 text-xs"
            style="border-color: var(--border)"
          />
          <input
            v-model="row.colorHex"
            type="color"
            data-testid="category-colour"
            class="tap-target h-9 w-12 shrink-0 rounded-lg border bg-[var(--surface-raised)]"
            style="border-color: var(--border)"
            :aria-label="t('Colour')"
          />
        </div>

        <!--
          The words that file an expense here on their own. Longest wins, so
          "uber eats" beats "uber" and a takeaway is not filed as a taxi.
        -->
        <label class="mt-2 flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Words that file an expense here') }}</span>
          <input
            v-model="row.keywords"
            type="text"
            data-testid="category-keywords"
            :placeholder="t('metro, iga, epicerie')"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
            style="border-color: var(--border)"
          />
        </label>
      </li>
    </ul>

    <button
      v-if="rows.length < MOST"
      type="button"
      data-testid="add-category"
      class="btn btn-press btn-secondary mt-3 min-h-0 px-3 py-1.5 text-xs"
      style="border-color: var(--border)"
      @click="add"
    >{{ t('Add a category') }}
    </button>

    <div v-if="isDirty" class="mt-3 flex gap-2">
      <button
        type="button"
        data-testid="cancel-categories"
        class="btn btn-press btn-secondary flex-1"
        style="border-color: var(--border)"
        :disabled="isSaving"
        @click="revert"
      >{{ t('Cancel') }}
      </button>
      <button
        type="button"
        data-testid="save-categories"
        class="btn btn-press btn-primary flex-1"
        :disabled="isSaving"
        @click="save"
      >
        {{ isSaving ? t('Saving') : t('Save changes') }}
      </button>
    </div>
  </div>
</template>
