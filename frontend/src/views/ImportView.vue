<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { useApi } from '@/api/provider'
import SettleUpWizard from '@/components/import/SettleUpWizard.vue'
import { StatementWorkerClient } from '@/import/statementWorkerClient'
import { StatementReviewSession, type RowAction } from '@/import/reviewSession'
import { computeFingerprint, normalizeMerchant } from '@/domain/fingerprint'

const auth = useAuthStore()
const groups = useGroupsStore()
const expenses = useExpensesStore()
const router = useRouter()

const worker = new StatementWorkerClient()
const session = ref<StatementReviewSession | null>(null)

/**
 * Which of the two ways in is being used, so the other stops being offered.
 *
 * The statement reader shows its own state as soon as it has a file; the export
 * wizard keeps its file to itself and says so.
 */
const settleUpActive = ref(false)
const statementActive = ref(false)
const progress = ref<{ stage: string; ratio: number } | null>(null)
const usedOcr = ref(false)
const error = ref<string | null>(null)
const message = ref<string | null>(null)
const isCommitting = ref(false)


onMounted(async () => {
  await groups.loadAll()

})

// Terminating the worker drops the parsed statement from memory. Combined with
// the session's own cleanup, nothing about the file survives leaving this screen.
onBeforeUnmount(async () => {
  worker.dispose()
  await session.value?.cancel()
})

const summary = computed(() => session.value?.summary() ?? null)

async function onFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return

  error.value = null
  message.value = null
  statementActive.value = true
  progress.value = { stage: 'Reading the file', ratio: 0 }

  try {
    const parsed = file.name.toLowerCase().endsWith('.pdf')
      ? await worker.parsePdf(await file.arrayBuffer(), new Date().getFullYear(), (p) => {
          progress.value = p
        })
      : await worker.parseCsv(await file.text(), (p) => {
          progress.value = p
        })

    usedOcr.value = parsed.usedOcr

    if (parsed.rows.length === 0) {
      error.value =
        'No transactions could be read from that file. Bank layouts vary a lot; try the CSV export instead.'
      // Nothing came of it, so the other way in is worth offering again.
      statementActive.value = false
      return
    }

    // Fingerprints are computed here, on the device, and only the hashes are sent
    // to ask whether the server already has these transactions.
    const fingerprints = await Promise.all(
      parsed.rows.map((row) =>
        row.date && row.amount !== null
          ? computeFingerprint(row.date, row.amount, row.currency ?? 'CAD', row.description)
          : Promise.resolve(null),
      ),
    )

    const duplicates = await checkDuplicates(fingerprints.filter((f): f is string => f !== null))
    const suggestions = await fetchSuggestions(parsed.rows.map((row) => row.description))

    const created = new StatementReviewSession(
      parsed.rows,
      { suggestions, duplicates, statementCurrency: 'CAD' },
      file.name,
    )

    parsed.rows.forEach((row, index) => {
      const fingerprint = fingerprints[index]
      if (fingerprint) created.setFingerprint(row.rowNumber, fingerprint)
    })

    session.value = created
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not read that file.'
    // A file that could not be read leaves nothing behind, so both ways in come
    // back rather than stranding someone on the one that just failed.
    statementActive.value = false
  } finally {
    progress.value = null
    // Clear the picker, so re-choosing the same file re-parses it.
    input.value = ''
  }
}

async function checkDuplicates(fingerprints: string[]) {
  if (fingerprints.length === 0) return []

  try {
    const result = await useApi().post<{ matches: Array<Record<string, never>> }>('/import/duplicates', {
      fingerprints,
      groupId: null,
    })
    return result.matches as never[]
  } catch {
    return []
  }
}

async function fetchSuggestions(descriptions: string[]) {
  const merchants = [...new Set(descriptions.map(normalizeMerchant).filter(Boolean))]
  if (merchants.length === 0) return []

  try {
    const result = await useApi().post<{ suggestions: never[] }>('/import/split-suggestions', {
      merchants,
    })
    return result.suggestions
  } catch {
    return []
  }
}

function assign(rowNumber: number, groupId: string): void {
  const group = groups.groups.find((candidate) => candidate.id === groupId)
  if (!group || !auth.user) return

  const payer = groups.myMemberId(groupId, auth.user.id) ?? group.members[0]?.id
  if (!payer) return

  session.value?.assignGroup(
    rowNumber,
    groupId,
    payer,
    group.members
      .filter((member) => member.status === 'Active')
      .map((member) => ({ memberId: member.id, value: null })),
  )
}

function setAction(rowNumber: number, action: RowAction): void {
  session.value?.setAction(rowNumber, action)
}

async function commit(): Promise<void> {
  if (!session.value) return

  error.value = null
  isCommitting.value = true

  try {
    const payload = await session.value.buildCommitPayload()

    if (payload.rows.length === 0) {
      error.value = 'Assign at least one transaction to a group first.'
      return
    }

    const result = await useApi().post<{ createdExpenses: number; skippedRows: number }>(
      '/import/statement/commit',
      payload,
    )

    await session.value.dispose()
    session.value = null

    await expenses.sync()
    message.value = `Imported ${result.createdExpenses} transactions.`
    await router.replace({ name: 'dashboard' })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not import those transactions.'
  } finally {
    isCommitting.value = false
  }
}

async function onImported(result: {
  groupId: string
  createdExpenses: number
  createdSettlements: number
}): Promise<void> {
  await expenses.sync()

  const parts = [`${result.createdExpenses} expenses`]
  if (result.createdSettlements > 0) parts.push(`${result.createdSettlements} settlements`)
  message.value = `Imported ${parts.join(' and ')}.`

  await router.replace({ name: 'group', params: { groupId: result.groupId } })
}

async function cancel(): Promise<void> {
  await session.value?.cancel()
  session.value = null
  usedOcr.value = false
}
</script>

<template>
  <AppShell title="Import" :back-to="{ name: 'profile' }" back-label="Profile">
    <section v-if="!session" class="flex flex-col gap-4">
      <!--
        Two ways in, until one of them is being used. Leaving the other on screen
        offers a second question nobody asked and a second file input to pick by
        mistake, which is how an export ended up in the statement reader.
      -->
      <div v-if="!settleUpActive" data-testid="statement-import" class="surface-card p-4">
        <h2 class="font-medium">A bank or credit card statement</h2>
        <p class="mt-1 text-sm text-[var(--text-muted)]">
          The file is read on this device and never uploaded. Only the transactions you confirm are
          sent, and everything else is discarded when you leave this screen.
        </p>

        <label class="btn btn-press btn-secondary mt-3 w-full cursor-pointer"
               style="border-color: var(--border)">
          Choose a CSV or PDF
          <input type="file" accept=".csv,.pdf,text/csv,application/pdf" class="hidden" @change="onFile" />
        </label>
      </div>

      <SettleUpWizard
        v-show="!statementActive"
        data-testid="settleup-import"
        @imported="onImported"
        @cancel="() => undefined"
        @active="settleUpActive = $event"
      />

      <div v-if="progress" class="surface-card p-4" aria-live="polite">
        <p class="text-sm">{{ progress.stage }}</p>
        <div class="mt-2 h-1.5 rounded-full bg-[var(--surface-sunken)]">
          <span
            class="block h-full rounded-full bg-brand-500 transition-all"
            :style="{ width: `${Math.round(progress.ratio * 100)}%` }"
          />
        </div>
      </div>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>
      <p v-if="message" class="text-sm text-owed" role="status">{{ message }}</p>
    </section>

    <section v-else class="flex flex-col gap-4">
      <div v-if="usedOcr" class="surface-card p-3 text-xs text-[var(--text-muted)]">
        That statement had no text layer, so it was read from the images. Check the amounts before
        importing.
      </div>

      <div v-if="summary" class="surface-card p-3 text-sm">
        <p>
          {{ summary.toCommit }} to import, {{ summary.personal }} left personal,
          {{ summary.ignored }} ignored
          <template v-if="summary.duplicates > 0">, {{ summary.duplicates }} already recorded</template>
          <template v-if="summary.problems > 0">, {{ summary.problems }} need fixing</template>
        </p>
      </div>

      <ul class="flex flex-col gap-2">
        <li v-for="row in session.rows" :key="row.rowNumber" class="surface-card p-3">
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <p class="truncate text-sm font-medium">{{ row.description }}</p>
              <p class="text-xs text-[var(--text-muted)]">
                {{ row.date ? row.date.toLocaleDateString() : 'No date' }}
                <template v-if="row.isForeignCurrency"> - {{ row.currency }}</template>
              </p>
              <p v-if="row.problems.length > 0" class="text-xs text-owing">
                {{ row.problems.join('; ') }}
              </p>
              <p v-if="row.isDuplicate" class="text-xs text-[var(--text-muted)]">
                Already recorded in {{ row.duplicateOf?.groupName }}
              </p>
            </div>

            <MoneyAmount
              :amount="row.amount ?? 0"
              :currency="row.currency ?? 'CAD'"
              size="sm"
            />
          </div>

          <div class="mt-2 flex flex-wrap items-center gap-2">
            <select
              class="rounded-lg border bg-[var(--surface)] px-2 py-1 text-xs"
              style="border-color: var(--border)"
              :value="row.groupId ?? ''"
              @change="assign(row.rowNumber, ($event.target as HTMLSelectElement).value)"
            >
              <option value="">Personal, not split</option>
              <option v-for="group in groups.visibleGroups" :key="group.id" :value="group.id">
                Split in {{ group.name }}
              </option>
            </select>

            <button
              type="button"
              class="btn btn-press btn-secondary min-h-0 px-2 py-1 text-xs"
              :class="row.action === 'ignore' ? 'border-brand-500 text-brand-400' : ''"
              :style="row.action === 'ignore' ? undefined : 'border-color: var(--border)'"
              @click="setAction(row.rowNumber, row.action === 'ignore' ? 'personal' : 'ignore')"
            >
              {{ row.action === 'ignore' ? 'Ignored' : 'Ignore' }}
            </button>
          </div>
        </li>
      </ul>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

      <div class="flex gap-2">
        <button
          type="button"
          class="btn btn-press btn-secondary flex-1"
          style="border-color: var(--border)"
          @click="cancel"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-press btn-primary flex-1"
          :disabled="isCommitting || (summary?.toCommit ?? 0) === 0"
          @click="commit"
        >
          {{ isCommitting ? 'Importing' : `Import ${summary?.toCommit ?? 0}` }}
        </button>
      </div>
    </section>
  </AppShell>
</template>
