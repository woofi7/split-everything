<script setup lang="ts">
import { t, intlLocale } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { categoriseByKeywords, categoryFor, fold } from '@/domain/categories'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { notify, report } from '@/ui/toasts'
import type { LocalExpense } from '@/offline/db'

/**
 * Filing expenses by the handful.
 *
 * Categories arrived after the expenses did, so every group that was already
 * running has a backlog: hundreds of rows filed under nothing, and a form each to
 * fix them one at a time. Nobody does that. The same backlog appears in miniature
 * every time somebody adds a keyword, because keywords only ever file what comes
 * next.
 *
 * So this screen is about the handful rather than the one: narrow the list to
 * what you mean - unfiled, or everything already under the wrong heading - tick
 * them, and say where they go. And before any of that, one button for the rows
 * the keywords can already answer for, which on a real group is most of them.
 *
 * It never touches money. A category is a label on an expense: no split, no
 * amount, no balance moves, which is why any member may do it and why it is safe
 * to do four hundred at once.
 */

const route = useRoute()
const groups = useGroupsStore()
const expenses = useExpensesStore()

const groupId = computed(() => String(route.params.groupId))
const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const categories = computed(() => groups.categoriesOf(groupId.value))

onMounted(async () => {
  // loadAll brings the cached categories back with the groups, so the headings
  // here are right before anything reaches the network; get refreshes both.
  await groups.loadAll()
  await groups.get(groupId.value)
  await expenses.hydrate()
})

/** The filter's two standing choices. A category key is never either of these. */
const UNFILED = ''
const EVERYTHING = '*'

const shown = ref<string>(UNFILED)
const query = ref('')

const inGroup = computed(() => expenses.forGroup(groupId.value))

/**
 * What an expense reads as, which is not quite what it stores.
 *
 * A key the list no longer has - a category somebody deleted - shows as unfiled
 * everywhere else in the app, so it is unfiled here too. Otherwise the rows left
 * behind by a delete would be invisible on the one screen built to collect them.
 */
const filedAs = (expense: LocalExpense) => categoryFor(expense.categoryKey, categories.value)

const matching = computed(() => {
  const needle = fold(query.value.trim())

  return inGroup.value.filter((expense) => {
    if (shown.value === UNFILED && filedAs(expense)) return false
    if (shown.value !== UNFILED && shown.value !== EVERYTHING && expense.categoryKey !== shown.value) {
      return false
    }

    return !needle || fold(expense.description).includes(needle)
  })
})

const selected = ref<Set<string>>(new Set())

/**
 * The rows a press would actually change.
 *
 * The intersection rather than the ticked set, so narrowing the list narrows what
 * is about to happen. Anything else files expenses that are not on screen, which
 * is the one thing a bulk edit must never do quietly.
 */
const applying = computed(() => matching.value.filter((expense) => selected.value.has(expense.id)))

function toggle(expenseId: string): void {
  const next = new Set(selected.value)
  if (!next.delete(expenseId)) next.add(expenseId)
  selected.value = next
}

const allSelected = computed(
  () => matching.value.length > 0 && applying.value.length === matching.value.length,
)

function toggleAll(): void {
  selected.value = allSelected.value ? new Set() : new Set(matching.value.map((row) => row.id))
}

/** A page at a time: a group with a year behind it lists in the hundreds. */
const PAGE = 50
const visibleCount = ref(PAGE)

watch([shown, query], () => {
  visibleCount.value = PAGE
})

const rows = computed(() => matching.value.slice(0, visibleCount.value))
const remaining = computed(() => matching.value.length - rows.value.length)

/**
 * Where the ticked rows are going. Nothing until it is said.
 *
 * Not defaulted to a category, because the press is a bulk press and a default
 * would make it one tap from filing four hundred expenses under whatever happened
 * to be first. Not defaulted to "not filed" either, which reads as a choice and
 * does nothing on a list of expenses that are already unfiled.
 */
const target = ref<string | null>(null)
const isFiling = ref(false)

const targetName = computed(() => categoryFor(target.value, categories.value)?.name ?? t('Not filed'))

async function apply(): Promise<void> {
  const ids = applying.value.map((expense) => expense.id)
  isFiling.value = true

  try {
    const count = await expenses.refile(ids, target.value || null)
    selected.value = new Set()

    notify(
      count === 0
        ? t('Nothing to change: they were filed there already.')
        : t('{count} filed under {name}.', { count, name: targetName.value }),
      'done',
    )
  } catch (caught) {
    report(caught, t('Could not file those expenses.'))
  } finally {
    isFiling.value = false
  }
}

/**
 * What the group's own keywords can answer for on their own.
 *
 * Only rows nobody has filed, whatever the list is filtered to. A pass that could
 * overwrite a category somebody chose by hand would be a pass nobody dares press,
 * and the whole value of this button is that it is safe to press without reading
 * four hundred rows first.
 */
const guesses = computed(() => {
  const byCategory = new Map<string, string[]>()
  if (categories.value.length === 0) return byCategory

  for (const expense of inGroup.value) {
    if (filedAs(expense)) continue

    const key = categoriseByKeywords(expense.description, categories.value)
    if (!key) continue

    const bucket = byCategory.get(key)
    if (bucket) bucket.push(expense.id)
    else byCategory.set(key, [expense.id])
  }

  return byCategory
})

const guessCount = computed(() =>
  [...guesses.value.values()].reduce((sum, ids) => sum + ids.length, 0),
)

async function applyGuesses(): Promise<void> {
  isFiling.value = true

  try {
    let filed = 0
    for (const [key, ids] of guesses.value) filed += await expenses.refile(ids, key)

    notify(t('{count} filed by their names.', { count: filed }), 'done')
  } catch (caught) {
    report(caught, t('Could not file those expenses.'))
  } finally {
    isFiling.value = false
  }
}

const spentOn = (iso: string) =>
  new Date(iso).toLocaleDateString(intlLocale.value, { day: 'numeric', month: 'short' })
</script>

<template>
  <AppShell
    :title="t('File expenses')"
    :subtitle="group?.name"
    :back-to="{ name: 'group-settings', params: { groupId } }"
  >
    <section class="surface-card mb-4 p-4">
      <p class="text-xs text-[var(--text-muted)]">{{ t('Tick the ones that belong together and say where they go. Nothing about the money changes: a category is a label, so balances and what everybody owes stay exactly as they are.') }}
      </p>

      <!--
        The one press that does most of the work. Above the list rather than in
        the bar at the foot, because it is not about what is ticked: it is the
        backlog the keywords can clear on their own, and it reads as the thing to
        try first.
      -->
      <button
        v-if="guessCount > 0"
        type="button"
        data-testid="file-by-keywords"
        class="btn btn-press btn-primary mt-3 w-full"
        :disabled="isFiling"
        @click="applyGuesses"
      >
        {{ t('File the {count} the words already know', { count: guessCount }) }}
      </button>

      <div class="mt-3 flex flex-col gap-2">
        <select
          v-model="shown"
          data-testid="filing-filter"
          class="w-full rounded-lg border bg-[var(--surface)] px-3 py-2 text-sm"
          style="border-color: var(--border)"
        >
          <option :value="UNFILED">{{ t('Not filed') }}</option>
          <option :value="EVERYTHING">{{ t('Everything') }}</option>
          <option v-for="category in categories" :key="category.key" :value="category.key">
            {{ category.name }}
          </option>
        </select>

        <input
          v-model="query"
          type="search"
          data-testid="filing-search"
          :placeholder="t('Search these expenses')"
          class="w-full rounded-lg border bg-[var(--surface)] px-3 py-2 text-sm"
          style="border-color: var(--border)"
        />
      </div>

      <div class="mt-3 flex items-center justify-between gap-2">
        <button
          type="button"
          data-testid="select-all"
          class="btn btn-press btn-quiet text-xs"
          :disabled="matching.length === 0"
          @click="toggleAll"
        >
          {{ allSelected ? t('Select none') : t('Select all {count}', { count: matching.length }) }}
        </button>

        <span data-testid="selected-count" class="text-xs text-[var(--text-muted)]">
          {{ t('{count} selected', { count: applying.length }) }}
        </span>
      </div>
    </section>

    <ul v-if="rows.length > 0" class="flex flex-col gap-2 pb-24">
      <li v-for="expense in rows" :key="expense.id">
        <label
          data-testid="filing-row"
          :data-expense-id="expense.id"
          class="tap-target flex items-center gap-3 rounded-xl border p-3"
          style="border-color: var(--border)"
        >
          <input
            type="checkbox"
            class="h-5 w-5 shrink-0"
            :checked="selected.has(expense.id)"
            @change="toggle(expense.id)"
          />

          <span class="min-w-0 flex-1">
            <span class="block truncate font-medium">{{ expense.description }}</span>
            <span class="block truncate text-xs text-[var(--text-muted)]">
              <FontAwesomeIcon
                v-if="filedAs(expense)"
                :icon="resolveIcon(filedAs(expense)!.iconName).definition"
                class="mr-1 h-3 w-3"
                :style="{ color: filedAs(expense)!.colorHex }"
                aria-hidden="true"
              />
              <span data-testid="row-filed-as">{{ filedAs(expense)?.name ?? t('Not filed') }}</span>
              <span aria-hidden="true"> - </span>{{ spentOn(expense.spentAt) }}
            </span>
          </span>

          <MoneyAmount :amount="expense.amount" :currency="expense.currency" size="sm" />
        </label>
      </li>
    </ul>

    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      {{ shown === UNFILED && !query ? t('Everything here is filed.') : t('Nothing matches that.') }}
    </p>

    <div v-if="remaining > 0" class="flex justify-center py-4 pb-24">
      <button
        type="button"
        data-testid="show-more-filing"
        class="btn btn-press btn-quiet text-xs"
        @click="visibleCount += PAGE"
      >
        {{ t('Show {count} more', { count: remaining }) }}
      </button>
    </div>

    <!--
      Where the ticked rows go. Fixed at the foot rather than at the top of a list
      four hundred rows long: the last thing you tick is at the bottom of the
      screen, and that is where the press has to be.
    -->
    <div
      v-if="applying.length > 0"
      data-testid="filing-bar"
      class="fixed right-4 left-4 z-40 flex items-center gap-2 rounded-xl border p-2 shadow-lg"
      style="bottom: calc(6rem + env(safe-area-inset-bottom)); background: var(--surface-raised); border-color: var(--border)"
    >
      <select
        v-model="target"
        data-testid="filing-target"
        class="min-w-0 flex-1 rounded-lg border bg-[var(--surface)] px-3 py-2 text-sm"
        style="border-color: var(--border)"
      >
        <option :value="null" disabled>{{ t('Where do they go?') }}</option>
        <option value="">{{ t('Not filed') }}</option>
        <option v-for="category in categories" :key="category.key" :value="category.key">
          {{ category.name }}
        </option>
      </select>

      <button
        type="button"
        data-testid="file-selected"
        class="btn btn-press btn-primary shrink-0"
        :disabled="isFiling || target === null"
        @click="apply"
      >
        {{ isFiling ? t('Filing') : t('File {count}', { count: applying.length }) }}
      </button>
    </div>
  </AppShell>
</template>
