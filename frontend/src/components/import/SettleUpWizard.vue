<script setup lang="ts">
import { computed, ref } from 'vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useApi } from '@/api/provider'
import { useGroupsStore } from '@/stores/groups'

/**
 * The Settle Up export import.
 *
 * The server has parsed these for a while with nothing to reach it. The shape of
 * this screen comes from a real export rather than from the API: the purpose of a
 * row is the thing worth reading, the people are names rather than accounts, and
 * a row can be a transfer, which is a settlement rather than money spent.
 *
 * The file never leaves the browser between steps, but it does go to the API on
 * each one: preview and commit both re-parse it server-side, so it is held here
 * rather than uploaded once and stored.
 */

const emit = defineEmits<{
  imported: [result: { groupId: string; createdExpenses: number; createdSettlements: number }]
  cancel: []
}>()

interface Analysis {
  analysisId: string
  headers: string[]
  sampleRows: string[][]
  suggestedMapping: Record<string, number>
  detectedMemberNames: string[]
  detectedDelimiter: string
  detectedCurrency: string | null
  rowCount: number
}

interface PreviewRow {
  rowNumber: number
  spentAt: string | null
  description: string
  amount: number | null
  currency: string | null
  paidByName: string | null
  participantNames: string[]
  isDuplicate: boolean
  problems: string[]
  splitAmounts: number[] | null
  isSettlement: boolean
}

interface Preview {
  rows: PreviewRow[]
  committableCount: number
  problemCount: number
  duplicateCount: number
  unmappedMemberNames: string[]
}

interface CommitResult {
  groupId: string
  createdExpenses: number
  createdSettlements: number
  skippedRows: number
  warnings: string[]
}

const groups = useGroupsStore()

const file = ref<File | null>(null)
const analysis = ref<Analysis | null>(null)
const preview = ref<Preview | null>(null)
const busy = ref<string | null>(null)
const error = ref<string | null>(null)

const target = ref<'new' | 'existing'>('new')
const newGroupName = ref('')
const existingGroupId = ref('')

/**
 * The destination as one value, for the dropdown over the rows.
 *
 * The step above asks it as a radio pair plus a name, which is the right shape
 * when the answer is still being composed. Over the rows it is one question with
 * one answer, so it is one control, reading and writing the same two refs rather
 * than keeping a third.
 */
const destination = computed(() =>
  target.value === 'new' ? 'new' : existingGroupId.value,
)

function chooseDestination(value: string): void {
  if (value === 'new') {
    target.value = 'new'
    return
  }

  target.value = 'existing'
  existingGroupId.value = value
}

/** Exported name -> member id, or null to create someone for it. */
const nameMapping = ref<Record<string, string | null>>({})
const skipped = ref<Set<number>>(new Set())

const currency = computed(() => analysis.value?.detectedCurrency ?? 'CAD')

const members = computed(() => {
  if (target.value !== 'existing') return []
  return groups.membersOf(existingGroupId.value).filter((member) => member.status === 'Active')
})

const toImport = computed(() =>
  (preview.value?.rows ?? []).filter((row) => !skipped.value.has(row.rowNumber)).length,
)

function reset(): void {
  file.value = null
  analysis.value = null
  preview.value = null
  skipped.value = new Set()
  nameMapping.value = {}
  error.value = null
}

async function onFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const chosen = input.files?.[0]
  if (!chosen) return

  reset()
  file.value = chosen
  busy.value = 'Reading the export'

  try {
    const result = await useApi().upload<Analysis>('/import/csv/analyze', { file: chosen })
    analysis.value = result

    // The file name is the group name in every export the app produces.
    newGroupName.value = chosen.name.replace(/\.csv$/i, '').trim()
    existingGroupId.value = groups.visibleGroups[0]?.id ?? ''
    nameMapping.value = Object.fromEntries(result.detectedMemberNames.map((name) => [name, null]))
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not read that export.'
  } finally {
    busy.value = null
    // Cleared so choosing the same file again re-reads it.
    input.value = ''
  }
}

/** What the server needs to parse the rows the way this screen is set up. */
function mapping() {
  const suggested = analysis.value?.suggestedMapping ?? {}
  const at = (key: string, fallback: number | null = null) => {
    const value = suggested[key]
    return value === undefined || value < 0 ? fallback : value
  }

  return {
    dateColumn: at('date', 0) ?? 0,
    descriptionColumn: at('description', 1) ?? 1,
    amountColumn: at('amount', 2) ?? 2,
    currencyColumn: at('currency'),
    paidByColumn: at('paidBy'),
    participantColumns: null,
    dateFormat: null,
    decimalSeparator: null,
    participantsColumn: at('participants'),
    splitAmountsColumn: at('splitAmounts'),
    typeColumn: at('type'),
  }
}

function groupIdForRequest(): string | null {
  return target.value === 'existing' ? existingGroupId.value : null
}

function validateTarget(): boolean {
  if (target.value === 'new' && !newGroupName.value.trim()) {
    error.value = 'Name the group this import should create.'
    return false
  }
  if (target.value === 'existing' && !existingGroupId.value) {
    error.value = 'Choose the group to import into.'
    return false
  }
  return true
}

async function loadPreview(): Promise<void> {
  if (!file.value || !analysis.value) return

  error.value = null
  if (!validateTarget()) return

  busy.value = 'Reading the rows'

  try {
    preview.value = await useApi().upload<Preview>('/import/csv/preview', {
      file: file.value,
      request: JSON.stringify({
        groupId: groupIdForRequest(),
        mapping: mapping(),
        memberNameMapping: nameMapping.value,
        fallbackCurrency: currency.value,
      }),
    })

    // Anything already recorded starts off skipped: importing it again is the one
    // outcome nobody wants, and it is still one tap to put back.
    skipped.value = new Set(
      preview.value.rows.filter((row) => row.isDuplicate).map((row) => row.rowNumber),
    )
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not read those rows.'
  } finally {
    busy.value = null
  }
}

function toggleRow(rowNumber: number): void {
  const next = new Set(skipped.value)
  if (next.has(rowNumber)) next.delete(rowNumber)
  else next.add(rowNumber)
  skipped.value = next
}

async function commit(): Promise<void> {
  if (!file.value || !analysis.value) return

  error.value = null
  if (!validateTarget()) return

  busy.value = 'Importing'

  try {
    const result = await useApi().upload<CommitResult>('/import/csv/commit', {
      file: file.value,
      request: JSON.stringify({
        analysisId: analysis.value.analysisId,
        groupId: groupIdForRequest(),
        newGroupName: target.value === 'new' ? newGroupName.value.trim() : null,
        mapping: mapping(),
        memberNameMapping: nameMapping.value,
        skipRowNumbers: [...skipped.value].sort((a, b) => a - b),
        createMissingMembers: true,
        skipDuplicates: false,
        fallbackCurrency: currency.value,
        sourceLabel: file.value.name,
      }),
    })

    await groups.loadAll()
    emit('imported', result)
    reset()
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not import that export.'
  } finally {
    busy.value = null
  }
}

const dateOf = (value: string | null) =>
  value ? new Date(value).toLocaleDateString() : 'No date'
</script>

<template>
  <section class="flex flex-col gap-4">
    <!-- Outside the steps: a file that cannot be read fails before there is a
         step two to report it in. -->
    <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
    <p v-if="busy" class="text-sm text-[var(--text-muted)]" aria-live="polite">{{ busy }}</p>

    <!-- Step one: the file. -->
    <div v-if="!analysis" class="surface-card p-4">
      <h2 class="font-medium">A Settle Up export</h2>
      <p class="mt-1 text-sm text-[var(--text-muted)]">
        Export a group from Settle Up and choose the file here. Nothing is imported
        until you have seen the rows.
      </p>

      <label
        class="btn btn-press btn-secondary mt-3 w-full cursor-pointer"
        style="border-color: var(--border)"
      >
        Choose the CSV
        <input type="file" accept=".csv,text/csv" class="hidden" @change="onFile" />
      </label>
    </div>

    <template v-else>
      <div class="surface-card p-4">
        <div class="flex items-baseline justify-between gap-3">
          <h2 class="truncate font-medium">{{ file?.name }}</h2>
          <span class="shrink-0 text-xs text-[var(--text-muted)]">
            {{ analysis.rowCount }} rows
          </span>
        </div>
        <p class="mt-1 text-xs text-[var(--text-muted)]">
          Columns found: {{ Object.keys(analysis.suggestedMapping).length }} of
          {{ analysis.headers.length }}. Currency {{ currency }}.
        </p>
      </div>

      <!-- Step two: where it goes. -->
      <div class="surface-card flex flex-col gap-3 p-4">
        <h2 class="text-sm font-medium text-[var(--text-muted)]">Import into</h2>

        <label class="flex items-center gap-2 text-sm">
          <input v-model="target" type="radio" value="new" />
          A new group
        </label>

        <input
          v-if="target === 'new'"
          v-model="newGroupName"
          data-testid="new-group-name"
          type="text"
          maxlength="120"
          placeholder="Group name"
          class="tap-target rounded-lg border bg-[var(--surface)] px-3 text-sm"
          style="border-color: var(--border)"
        />

        <label class="flex items-center gap-2 text-sm">
          <input v-model="target" type="radio" value="existing" />
          An existing group
        </label>

        <select
          v-if="target === 'existing'"
          v-model="existingGroupId"
          data-testid="existing-group"
          class="tap-target rounded-lg border bg-[var(--surface)] px-3 text-sm"
          style="border-color: var(--border)"
        >
          <option v-for="group in groups.visibleGroups" :key="group.id" :value="group.id">
            {{ group.name }}
          </option>
        </select>
      </div>

      <!-- Step three: who is who. -->
      <div class="surface-card flex flex-col gap-3 p-4">
        <h2 class="text-sm font-medium text-[var(--text-muted)]">People in the export</h2>
        <p class="text-xs text-[var(--text-muted)]">
          Settle Up exports names, not accounts. Anyone left unmatched is added to the
          group under that name, and can claim it later from an invite.
        </p>

        <div
          v-for="name in analysis.detectedMemberNames"
          :key="name"
          data-testid="name-map"
          class="flex items-center justify-between gap-3"
        >
          <span class="truncate text-sm">{{ name }}</span>

          <select
            v-model="nameMapping[name]"
            class="tap-target max-w-[55%] rounded-lg border bg-[var(--surface)] px-2 text-xs"
            style="border-color: var(--border)"
            :aria-label="`Who is ${name}`"
          >
            <option :value="null">Add as a new person</option>
            <option v-for="member in members" :key="member.id" :value="member.id">
              {{ member.displayName }}
            </option>
          </select>
        </div>
      </div>

      <!-- Step four: the rows. -->
      <template v-if="preview">
        <div class="surface-card flex flex-col gap-3 p-3">
          <p class="text-sm">
            {{ toImport }} to import, {{ skipped.size }} ignored
            <template v-if="preview.duplicateCount > 0">
              , {{ preview.duplicateCount }} already recorded
            </template>
            <template v-if="preview.problemCount > 0">
              , {{ preview.problemCount }} need fixing
            </template>
          </p>

          <!--
            Where the lot is going, next to the count of it. It is asked earlier as
            well, but this is where the rows are actually read, and reading them
            without knowing which group they are about to join is reading half the
            question.
          -->
          <label class="flex flex-col gap-1">
            <span class="text-xs text-[var(--text-muted)]">Import all of these into</span>
            <select
              :value="destination"
              data-testid="destination"
              class="tap-target rounded-lg border bg-[var(--surface)] px-3 text-sm"
              style="border-color: var(--border)"
              @change="chooseDestination(($event.target as HTMLSelectElement).value)"
            >
              <option value="new">A new group: {{ newGroupName || 'unnamed' }}</option>
              <option v-for="group in groups.visibleGroups" :key="group.id" :value="group.id">
                {{ group.name }}
              </option>
            </select>
          </label>
        </div>

        <ul class="flex flex-col gap-2">
          <li
            v-for="row in preview.rows"
            :key="row.rowNumber"
            data-testid="row"
            :data-ignored="skipped.has(row.rowNumber) ? 'true' : 'false'"
            class="surface-card p-3 transition-opacity"
            :class="skipped.has(row.rowNumber) ? 'opacity-25' : ''"
          >
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <!-- The purpose first: it is the only part that says what this was. -->
                <p
                  class="truncate text-sm font-medium"
                  :class="skipped.has(row.rowNumber) ? 'line-through' : ''"
                >
                  {{ row.description }}
                </p>

                <!--
                  Who it came from and who it was for, said rather than implied.
                  These were a tail on the date line, which is where the two things
                  a person checks a row against were hardest to read.

                  A transfer names one person on each side; an expense names the
                  payer and everybody who shared it.
                -->
                <dl class="mt-1 grid grid-cols-[2.5rem_1fr] gap-x-2 text-xs">
                  <dt class="text-[var(--text-muted)]">From</dt>
                  <dd data-testid="row-from" class="truncate">
                    {{ row.paidByName ?? 'Not named' }}
                  </dd>

                  <dt class="text-[var(--text-muted)]">To</dt>
                  <dd data-testid="row-to" class="truncate">
                    {{ row.participantNames.length > 0 ? row.participantNames.join(', ') : 'Not named' }}
                  </dd>
                </dl>

                <p class="mt-1 text-xs text-[var(--text-muted)]">
                  {{ dateOf(row.spentAt) }}
                </p>

                <p v-if="row.isSettlement" class="text-xs text-brand-400">
                  Settlement, not an expense
                </p>
                <p v-if="row.isDuplicate" class="text-xs text-[var(--text-muted)]">
                  Already recorded
                </p>
                <p v-if="row.problems.length > 0" class="text-xs text-owing">
                  {{ row.problems.join('; ') }}
                </p>
              </div>

              <div class="flex shrink-0 flex-col items-end gap-2">
                <MoneyAmount
                  :amount="row.amount ?? 0"
                  :currency="row.currency ?? currency"
                  size="sm"
                  :class="skipped.has(row.rowNumber) ? 'line-through' : ''"
                />

                <button
                  type="button"
                  data-testid="toggle-row"
                  class="btn btn-press btn-secondary min-h-0 px-2 py-1 text-xs"
                  :class="skipped.has(row.rowNumber) ? 'border-brand-500 text-brand-400' : ''"
                  :style="skipped.has(row.rowNumber) ? undefined : 'border-color: var(--border)'"
                  :aria-pressed="skipped.has(row.rowNumber)"
                  @click="toggleRow(row.rowNumber)"
                >
                  {{ skipped.has(row.rowNumber) ? 'Restore' : 'Ignore' }}
                </button>
              </div>
            </div>
          </li>
        </ul>
      </template>

      <div class="flex gap-2">
        <button
          type="button"
          class="btn btn-press btn-secondary flex-1"
          style="border-color: var(--border)"
          @click="reset(); emit('cancel')"
        >
          Cancel
        </button>

        <button
          v-if="!preview"
          type="button"
          data-testid="to-preview"
          class="btn btn-press btn-primary flex-1"
          :disabled="busy !== null"
          @click="loadPreview"
        >
          See the rows
        </button>

        <button
          v-else
          type="button"
          data-testid="commit"
          class="btn btn-press btn-primary flex-1"
          :disabled="busy !== null || toImport === 0"
          @click="commit"
        >
          Import {{ toImport }}
        </button>
      </div>
    </template>
  </section>
</template>
