<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { calculateSplit, splitValuesFor, type SplitType } from '@/domain/splitting'
import { parseAmountInput } from '@/domain/money'
import { memberColor, memberColors } from '@/domain/memberColors'

const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

/**
 * One form, two jobs.
 *
 * Editing asks exactly the same questions as adding, so this view does both rather
 * than a second copy of the split logic drifting from this one the first time
 * either changed. The route decides: an expense id in the path means edit.
 *
 * Six questions, deliberately: amount, date, what it was, which group, who paid,
 * and who it is between. The form has to fit one screen on a phone, and every
 * field costs a row, so a seventh has to earn its place.
 */
const editingId = computed(() => {
  const id = route.params.expenseId
  return typeof id === 'string' && id ? id : null
})

const isEditing = computed(() => editingId.value !== null)

const groupId = ref(String(route.params.groupId ?? route.query.groupId ?? ''))
const description = ref('')
const amountInput = ref('')
const spentAt = ref(new Date().toISOString().slice(0, 10))
const splitType = ref<SplitType>('Equal')
const paidByMemberId = ref('')
const participantIds = ref<string[]>([])
const splitValues = ref<Record<string, number>>({})
const error = ref<string | null>(null)
const isSaving = ref(false)
const makeDefault = ref(false)

/** Short, so all four fit one row on a phone without wrapping. */
const splitTypes: Array<{ value: SplitType; label: string }> = [
  { value: 'Equal', label: t('Equally') },
  { value: 'Percentage', label: t('Percent') },
  { value: 'Shares', label: t('Shares') },
  { value: 'ExactAmount', label: t('Exact') },
]

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()

  // The main group, then whatever the route asked for, then anything at all. The
  // route wins when it is there, because it means someone arrived from a group.
  await selectGroup(groupId.value || groups.mainGroupId || groups.visibleGroups[0]?.id || '')

  // After the group, because selecting one resets who is involved.
  if (isEditing.value) prefillFromExpense()
})

/**
 * Fills the form from the expense being edited.
 *
 * The split values come back out of the stored shares rather than being
 * recomputed, so an uneven split someone set by hand is what they see when they
 * open it again.
 */
function prefillFromExpense(): void {
  const existing = expenses.expenses.find((candidate) => candidate.id === editingId.value)
  if (!existing) {
    error.value = t('That expense is not on this device.')
    return
  }

  description.value = existing.description
  amountInput.value = existing.amount.toFixed(2)
  spentAt.value = existing.spentAt.slice(0, 10)
  splitType.value = existing.splitType
  paidByMemberId.value = existing.paidByMemberId
  participantIds.value = existing.splits.map((split) => split.memberId)

  splitValues.value = Object.fromEntries(
    existing.splits
      .filter((split) => split.inputValue !== null)
      .map((split) => [split.memberId, split.inputValue as number]),
  )
}

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const members = computed(() => group.value?.members.filter((m) => m.status === 'Active') ?? [])
const currency = computed(() => group.value?.baseCurrency ?? auth.user?.defaultCurrency ?? 'CAD')
const amount = computed(() => parseAmountInput(amountInput.value) ?? 0)

/**
 * Switching group resets who is involved and who paid, because member ids do not
 * carry across groups: reusing them would silently attach the expense to the
 * wrong people. Called explicitly rather than through a watcher so the order of
 * "load the group, then default the form" is visible here.
 */
async function selectGroup(nextGroupId: string): Promise<void> {
  groupId.value = nextGroupId
  if (!nextGroupId) return

  // Defaults are taken from what the refresh returned rather than re-read through
  // a computed: the computed depends on this same assignment, so reading it back
  // here would depend on reactivity flush order.
  const loaded = await groups.refresh(nextGroupId)
  const active = (loaded?.members ?? groups.membersOf(nextGroupId)).filter(
    (member) => member.status === 'Active',
  )

  participantIds.value = active.map((member) => member.id)

  const mine = active.find((member) => member.userId === auth.user?.id)?.id
  paidByMemberId.value = mine ?? active[0]?.id ?? ''

  applyGroupDefault(loaded?.defaultSplitType, loaded?.defaultSplitValues, active.map((m) => m.id))
}

/**
 * Starts the form from how this group splits.
 *
 * A household that always divides rent sixty forty had to say so on every
 * expense. Stored values are filtered to people who are still in the group: a
 * weight for someone who has left would make the split refuse to add up, and the
 * person would have no idea why.
 */
function applyGroupDefault(
  type: SplitType | undefined,
  values: Record<string, number> | null | undefined,
  activeIds: readonly string[],
): void {
  splitType.value = type ?? 'Equal'

  if (!values || splitType.value === 'Equal') {
    splitValues.value = {}
    return
  }

  splitValues.value = Object.fromEntries(
    Object.entries(values).filter(([memberId]) => activeIds.includes(memberId)),
  )
}

/**
 * The live preview.
 *
 * Computed with the same algorithm the server uses, so what the person sees here
 * is exactly what gets stored, including who receives a leftover cent.
 */
const preview = computed(() => {
  if (amount.value <= 0 || participantIds.value.length === 0) return []

  try {
    return calculateSplit(
      amount.value,
      currency.value,
      splitType.value,
      participantIds.value.map((memberId) => ({
        memberId,
        value: splitValues.value[memberId] ?? null,
      })),
    )
  } catch {
    // An incomplete percentage or exact split is a normal in-progress state, not
    // something to shout about while they are still typing.
    return []
  }
})

const previewProblem = computed(() => {
  if (amount.value <= 0 || participantIds.value.length === 0) return null

  try {
    calculateSplit(
      amount.value,
      currency.value,
      splitType.value,
      participantIds.value.map((memberId) => ({
        memberId,
        value: splitValues.value[memberId] ?? null,
      })),
    )
    return null
  } catch (caught) {
    return caught instanceof Error ? caught.message : t('That split does not add up.')
  }
})

/**
 * Only when editing. Adding is a tab, and the tab is already lit: a back button
 * there would compete with it.
 */
/**
 * Offered only when it would change something. Ticking it to record the split the
 * group already uses is a control that does nothing.
 */
const canSetDefault = computed(() => {
  if (!group.value) return false

  const current = group.value.defaultSplitType ?? 'Equal'
  if (current !== splitType.value) return true
  if (splitType.value === 'Equal') return false

  const stored = group.value.defaultSplitValues ?? {}
  return participantIds.value.some(
    (memberId) => (stored[memberId] ?? null) !== (splitValues.value[memberId] ?? null),
  )
})

const backTarget = computed(() =>
  isEditing.value && groupId.value
    ? { name: 'expense', params: { groupId: groupId.value, expenseId: editingId.value } }
    : undefined,
)

const colours = computed(() => memberColors(members.value.map((member) => member.id)))

const colourOf = (memberId: string) => colours.value[memberId] ?? memberColor(memberId)

/** What this person owes as the form stands, or null when there is nothing to show. */
function shareOf(memberId: string): number | null {
  if (!participantIds.value.includes(memberId)) return null
  return preview.value.find((share) => share.memberId === memberId)?.amount ?? null
}

/** Selected chips carry the person's colour, the way their expenses do. */
function chipStyle(memberId: string) {
  if (!participantIds.value.includes(memberId)) {
    return { borderColor: 'var(--border)' }
  }

  const colour = colourOf(memberId)

  return {
    backgroundColor: `color-mix(in oklab, ${colour} 16%, var(--surface-raised))`,
    borderColor: `color-mix(in oklab, ${colour} 45%, transparent)`,
  }
}

/**
 * Switches split type, carrying the division already on screen.
 *
 * The values mean something different under each type, so they used to be left as
 * they were: switching from equal to percentage emptied every box and the split
 * was invalid until all of them were typed again. Switching type is usually the
 * start of an adjustment, so the new numbers describe the same division, and the
 * person changes the one they wanted to change.
 *
 * Read from the preview rather than recomputed, so what carries over is exactly
 * what was on screen, including who had the leftover cent.
 */
function changeSplitType(next: SplitType): void {
  if (next === splitType.value) return

  const current = preview.value
  splitType.value = next
  splitValues.value = splitValuesFor(next, current, amount.value)
}

function toggleParticipant(memberId: string): void {
  const index = participantIds.value.indexOf(memberId)
  if (index >= 0) participantIds.value.splice(index, 1)
  else participantIds.value.push(memberId)
}

async function save(): Promise<void> {
  error.value = null

  if (!group.value) {
    error.value = t('Pick a group first.')
    return
  }

  isSaving.value = true

  const fields = {
    paidByMemberId: paidByMemberId.value,
    description: description.value,
    amount: amount.value,
    currency: currency.value,
    spentAt: new Date(`${spentAt.value}T12:00:00Z`),
    splitType: splitType.value,
    participantIds: participantIds.value,
    splitValues: splitValues.value,
  }

  // Before the expense, because the split it records is the one on screen now, and
  // a failure here should not cost the expense.
  if (makeDefault.value) {
    try {
      await groups.setDefaultSplit(
        group.value.id,
        splitType.value,
        splitType.value === 'Equal' ? null : { ...splitValues.value },
      )
    } catch {
      // Worth saying, but not worth refusing to save the expense over.
      error.value = t('Saved, but the group default could not be changed.')
    }
  }

  try {
    if (editingId.value) {
      await expenses.edit(editingId.value, fields)

      // Back to the expense, which is where the change is visible.
      await router.replace({
        name: 'expense',
        params: { groupId: group.value.id, expenseId: editingId.value },
      })
    } else {
      await expenses.add({ groupId: group.value.id, ...fields })

      // Queued locally, so this returns straight away whether online or not.
      await router.replace({ name: 'group', params: { groupId: group.value.id } })
    }
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not save the expense.')
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <AppShell
    :title="isEditing ? t('Edit expense') : t('Add expense')"
    :back-to="backTarget"
    :back-label="isEditing ? 'Expense' : 'Dashboard'"
  >
    <!--
      Built to fit one screen without scrolling.
      
      Adding an expense is the thing people open this app to do, usually standing
      up with one hand, so a form that scrolls hides half the decision. Three
      things bought the room: the amount and date share the top row, the split
      preview lives inside each person's chip rather than in a section of its own,
      and everyone in the group is a chip rather than a list row.
    -->
    <form class="flex flex-col gap-3" @submit.prevent="save">
      <!-- The amount leads: it is the one field nobody can leave blank. -->
      <div class="flex items-end gap-3">
        <label class="flex min-w-0 flex-1 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">
            {{ t('Amount ({currency})', { currency }) }}
          </span>
          <input
            v-model="amountInput"
            type="text"
            inputmode="decimal"
            required
            placeholder="0.00"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-3 text-2xl font-semibold tabular-nums"
            style="border-color: var(--border)"
          />
        </label>

        <label class="flex shrink-0 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Date') }}</span>
          <input
            v-model="spentAt"
            type="date"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
          />
        </label>
      </div>

      <label class="flex flex-col gap-1">
        <span class="text-xs text-[var(--text-muted)]">{{ t('What was it') }}</span>
        <input
          v-model="description"
          type="text"
          required
          maxlength="500"
          :placeholder="t('Groceries')"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        />
      </label>

      <div class="grid gap-3" :class="isEditing ? 'grid-cols-1' : 'grid-cols-2'">
        <!--
          Only when adding. Moving an existing expense between groups has to carry
          its history, comments and audit trail with it, which is the transfer
          feature; a dropdown here would look like it did that and would not.
        -->
        <label v-if="!isEditing" class="flex min-w-0 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Group') }}</span>
          <select
            :value="groupId"
            data-testid="group"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
            @change="selectGroup(($event.target as HTMLSelectElement).value)"
          >
            <option v-for="option in groups.visibleGroups" :key="option.id" :value="option.id">
              {{ option.name }}
            </option>
          </select>
        </label>

        <label class="flex min-w-0 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Who paid') }}</span>
          <select
            v-model="paidByMemberId"
            data-testid="paid-by"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
          >
            <option v-for="member in members" :key="member.id" :value="member.id">
              {{ member.displayName }}
            </option>
          </select>
        </label>
      </div>

      <fieldset class="flex flex-col gap-2">
        <legend class="text-xs text-[var(--text-muted)]">{{ t('Split') }}</legend>
        <div class="flex gap-1.5">
          <button
            v-for="option in splitTypes"
            :key="option.value"
            type="button"
            class="btn btn-press min-h-0 flex-1 px-1 py-1.5 text-xs"
            :class="splitType === option.value ? 'btn-primary' : 'btn-secondary'"
            @click="changeSplitType(option.value)"
          >
            {{ option.label }}
          </button>
        </div>
      </fieldset>

      <fieldset class="flex flex-col gap-2">
        <legend class="text-xs text-[var(--text-muted)]">{{ t('Between') }}</legend>

        <!--
          Each person carries their own share, so the preview that used to sit in a
          section below is here where the decision is made.
        -->
        <ul class="flex flex-wrap gap-2">
          <li
            v-for="member in members"
            :key="member.id"
            class="flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition-colors"
            :style="chipStyle(member.id)"
          >
            <label class="flex cursor-pointer items-center gap-2">
              <input
                type="checkbox"
                class="sr-only"
                :checked="participantIds.includes(member.id)"
                @change="toggleParticipant(member.id)"
              />
              <span
                class="h-2 w-2 shrink-0 rounded-full"
                :style="{ backgroundColor: participantIds.includes(member.id) ? colourOf(member.id) : 'var(--border)' }"
                aria-hidden="true"
              />
              <span :class="participantIds.includes(member.id) ? '' : 'text-[var(--text-muted)]'">
                {{ member.displayName }}
              </span>
            </label>

            <input
              v-if="splitType !== 'Equal' && participantIds.includes(member.id)"
              v-model.number="splitValues[member.id]"
              type="number"
              inputmode="decimal"
              step="0.01"
              class="w-14 rounded border bg-[var(--surface)] px-1 py-0.5 text-right text-xs tabular-nums"
              style="border-color: var(--border)"
              :aria-label="`${splitType} for ${member.displayName}`"
            />

            <!--
              Shown alongside the input, not instead of it. A percentage on its own
              does not say what anyone owes, and that is the number people check
              before saving.
            -->
            <MoneyAmount
              v-if="shareOf(member.id) !== null"
              :amount="shareOf(member.id)!"
              :currency="currency"
              size="sm"
            />
          </li>
        </ul>
      </fieldset>

      <label
        v-if="canSetDefault"
        class="flex cursor-pointer items-center gap-2 text-xs text-[var(--text-muted)]"
      >
        <input v-model="makeDefault" type="checkbox" data-testid="make-default" />
        Split every new expense in {{ group?.name }} this way
      </label>

      <p v-if="previewProblem" class="text-xs text-[var(--text-muted)]" aria-live="polite">
        {{ previewProblem }}
      </p>
      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

      <p class="text-center text-[11px] text-[var(--text-muted)]">{{ t('Saved on this device straight away, and synced when you are back online.') }}
      </p>

      <!-- Reachable whatever the group size does to the height above it. -->
      <button
        type="submit"
        class="btn btn-press btn-primary sticky bottom-2 mt-1 w-full"
        :disabled="isSaving || preview.length === 0"
      >
        {{ isSaving ? t('Saving') : isEditing ? t('Save changes') : t('Save expense') }}
      </button>
    </form>
  </AppShell>
</template>
