<script setup lang="ts">
import { intlLocale, t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { calculateSplit, splitValuesFor, type SplitType } from '@/domain/splitting'
import { formatMoney, parseAmountInput, roundMoney } from '@/domain/money'
import { memberColor, memberColors } from '@/domain/memberColors'
import { lastExpenseDate, rememberExpenseDate, today } from '@/domain/lastExpenseDate'
import { bucketOf } from '@/domain/buckets'

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

/**
 * The date used last, when adding. Editing is prefilled from the expense itself a
 * moment later, and the remembered date would only flash on screen first.
 */
const spentAt = ref(isEditing.value ? today() : (lastExpenseDate() ?? today()))

/**
 * Whether the date on screen is today's.
 *
 * Watched rather than assumed, because the form can open on a date carried over
 * from the last expense: a form that quietly starts on last Saturday is a wrong
 * date nobody looks at twice, so when it is not today the field says so in words,
 * wears a border that asks to be read, and offers the one tap back.
 */
const isToday = computed(() => spentAt.value === today())

/** The date in words, for the times it is not today and that matters. */
const spentAtLabel = computed(() => {
  const [year, month, day] = spentAt.value.split('-').map(Number)
  if (!year || !month || !day) return spentAt.value

  return new Date(year, month - 1, day).toLocaleDateString(intlLocale.value, {
    day: 'numeric',
    month: 'long',
  })
})

function useToday(): void {
  spentAt.value = today()
}

const splitType = ref<SplitType>('Equal')
const paidByMemberId = ref('')

/**
 * Who paid, when it was not one person.
 *
 * Empty is the ordinary case and means the single payer above. Non-empty, the
 * amount of the expense is the sum of these rather than something typed: two
 * people putting in 40 and 25 have spent 65, and asking for the total as well
 * would be asking for a number the app can already work out - and one more thing
 * that can disagree with the rest of the screen.
 */
const payers = ref<Array<{ memberId: string; amount: string }>>([])
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

  payers.value =
    (existing.payers ?? []).length > 1
      ? existing.payers!.map((payer) => ({
          memberId: payer.memberId,
          amount: payer.amount.toFixed(2),
        }))
      : []

  splitValues.value = Object.fromEntries(
    existing.splits
      .filter((split) => split.inputValue !== null)
      .map((split) => [split.memberId, split.inputValue as number]),
  )
}

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const members = computed(() => group.value?.members.filter((m) => m.status === 'Active') ?? [])
const currency = computed(() => group.value?.baseCurrency ?? auth.user?.defaultCurrency ?? 'CAD')
/**
 * What the expense cost: the sum of the contributions when several people paid,
 * otherwise whatever was typed.
 */
const amount = computed(() =>
  isShared.value ? paidTotal.value : (parseAmountInput(amountInput.value) ?? 0),
)

const isShared = computed(() => payers.value.length > 0)

const paidTotal = computed(() =>
  roundMoney(
    payers.value.reduce((sum, payer) => sum + (parseAmountInput(payer.amount) ?? 0), 0),
    currency.value,
  ),
)

/** The members not already named as payers, so the second row cannot repeat the first. */
const availablePayers = computed(() =>
  members.value.filter(
    (member) => !payers.value.some((payer) => payer.memberId === member.id),
  ),
)

/**
 * Splitting the payment starts from what is on screen: whoever was going to pay,
 * for what was typed, plus an empty row for the next person. Nothing is lost by
 * turning it on and off again.
 */
function sharePayment(): void {
  const others = members.value.filter((member) => member.id !== paidByMemberId.value)

  payers.value = [
    { memberId: paidByMemberId.value, amount: amountInput.value.trim() || '' },
    { memberId: others[0]?.id ?? '', amount: '' },
  ].filter((payer) => payer.memberId !== '')
}

function stopSharing(): void {
  // The total stays: it is what the expense costs either way, and clearing it
  // would throw away a figure the person had already worked out.
  amountInput.value = paidTotal.value > 0 ? paidTotal.value.toFixed(2) : amountInput.value
  paidByMemberId.value = payers.value[0]?.memberId ?? paidByMemberId.value
  payers.value = []
}

function addPayer(): void {
  const next = availablePayers.value[0]
  if (next) payers.value.push({ memberId: next.id, amount: '' })
}

function removePayer(index: number): void {
  payers.value.splice(index, 1)
  if (payers.value.length < 2) stopSharing()
}

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

/**
 * Moving this expense to another group.
 *
 * A real move rather than a change of group on the form: the expense takes its
 * revisions, its comments and its place in both groups' history with it, and the
 * server does the lot in one go. Offered only when editing, because an expense
 * that has not been saved yet has nothing to carry and the group dropdown above
 * already says where it is going.
 */
const isMoveOpen = ref(false)
const moveTargetId = ref('')
/** Source member id to the person in the other group they are, where it is asked. */
const moveMapping = ref<Record<string, string>>({})
const isMoving = ref(false)
/** Kept apart from the form's error, so a refusal here is not reported up there. */
const moveError = ref<string | null>(null)

const editing = computed(() =>
  expenses.expenses.find((candidate) => candidate.id === editingId.value),
)

/**
 * The groups this one could go to.
 *
 * Same currency, because balances either side of the move are kept in the group's
 * own money and the server refuses the rest; not archived, because an archived
 * group takes no new writes.
 */
const moveTargets = computed(() =>
  groups.visibleGroups.filter(
    (candidate) =>
      candidate.id !== groupId.value &&
      !candidate.isArchived &&
      candidate.baseCurrency === currency.value,
  ),
)

const moveTarget = computed(() =>
  groups.groups.find((candidate) => candidate.id === moveTargetId.value),
)

/** Everyone the expense names: who paid, who put money in, and who it is between. */
const involvedMemberIds = computed(() => {
  const expense = editing.value
  if (!expense) return []

  return [
    ...new Set([
      expense.paidByMemberId,
      ...(expense.payers ?? []).map((payer) => payer.memberId),
      ...expense.splits.map((split) => split.memberId),
      ...expense.items.flatMap((item) => item.memberIds),
    ]),
  ].filter(Boolean)
})

const targetMembers = computed(() =>
  groups.membersOf(moveTargetId.value).filter((member) => member.status === 'Active'),
)

/**
 * The person in the other group this one already is, if the two groups agree.
 *
 * The same rule the server applies, so the form asks about exactly the people it
 * is going to be asked about: the same account, or failing that the same name.
 * Somebody with an account is never matched by name - two groups can hold two
 * different people called Alex, and handing one of them the other's debts is the
 * one mistake this must not make.
 */
function matchInTarget(memberId: string) {
  const source = groups.membersOf(groupId.value).find((member) => member.id === memberId)
  if (!source) return undefined

  return source.userId
    ? targetMembers.value.find((member) => member.userId === source.userId)
    : targetMembers.value.find(
        (member) => member.displayName.toLowerCase() === source.displayName.toLowerCase(),
      )
}

/** Who has to be pointed at somebody by hand before the move can go ahead. */
const unmatchedMembers = computed(() =>
  moveTargetId.value
    ? involvedMemberIds.value
        .filter((memberId) => !matchInTarget(memberId))
        .map((memberId) => ({
          id: memberId,
          name:
            members.value.find((member) => member.id === memberId)?.displayName ??
            groups.membersOf(groupId.value).find((member) => member.id === memberId)?.displayName ??
            t('Someone'),
        }))
    : [],
)

const isMoveReady = computed(
  () =>
    moveTargetId.value !== '' &&
    targetMembers.value.length > 0 &&
    unmatchedMembers.value.every((person) => moveMapping.value[person.id]),
)

/**
 * Reads who is in the group being moved to.
 *
 * The list of groups carries a count and no roster, so without this the form would
 * have nobody to match anyone against and would ask about every person in the
 * expense.
 */
async function chooseMoveTarget(nextGroupId: string): Promise<void> {
  moveTargetId.value = nextGroupId
  moveMapping.value = {}
  moveError.value = null

  if (!nextGroupId) return

  await groups.refresh(nextGroupId)

  if (targetMembers.value.length === 0) {
    moveError.value = t('Could not read who is in that group. Moving an expense needs a connection.')
  }
}

async function move(): Promise<void> {
  if (!isMoveReady.value || !editingId.value) return

  moveError.value = null
  isMoving.value = true

  try {
    await expenses.transfer(editingId.value, moveTargetId.value, { ...moveMapping.value })

    // To the expense where it now lives, which is the only place the move is
    // visible and the only group the old link would no longer find it in.
    await router.replace({
      name: 'expense',
      params: { groupId: moveTargetId.value, expenseId: editingId.value },
    })
  } catch (caught) {
    moveError.value = caught instanceof Error ? caught.message : t('Could not move the expense.')
  } finally {
    isMoving.value = false
  }
}

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

  // Rows with nothing in them are somebody part-way through typing, not a payer
  // of zero: dropped rather than refused.
  const contributions = payers.value
    .filter((payer) => payer.memberId && (parseAmountInput(payer.amount) ?? 0) > 0)
    .map((payer) => ({
      memberId: payer.memberId,
      amount: parseAmountInput(payer.amount) as number,
    }))

  if (isShared.value && contributions.length < 2) {
    error.value = t('Say what each person paid, or go back to a single payer.')
    isSaving.value = false
    return
  }

  const fields = {
    paidByMemberId: isShared.value ? contributions[0].memberId : paidByMemberId.value,
    payers: isShared.value ? contributions : undefined,
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
      const added = await expenses.add({ groupId: group.value.id, ...fields })

      // After it saved, so a refused expense does not move where the next one
      // starts. Adding only: an edit is about one expense from whenever it was.
      rememberExpenseDate(spentAt.value)

      // Queued locally, so this returns straight away whether online or not. The
      // month goes with it: the group screen opens on the current one, so an
      // expense dated in August was added, synced, and invisible - which reads as
      // "it did not save" and had somebody adding it four times over.
      await router.replace({
        name: 'group',
        params: { groupId: group.value.id },
        // The month, and which expense: a month is thirty rows and the one just
        // typed can be the last of them, which is no more visible than it was.
        query: { month: bucketOf(fields.spentAt, 'month'), added: added.id },
      })
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

          <!--
            Read rather than typed once several people are paying: the total is the
            sum of what each of them put in, and a field that can disagree with the
            numbers directly below it is a field that will.
          -->
          <p
            v-if="isShared"
            data-testid="shared-total"
            class="tap-target flex items-center rounded-lg border bg-[var(--surface-sunken)] px-3 text-2xl font-semibold tabular-nums"
            style="border-color: var(--border)"
          >
            {{ formatMoney(paidTotal, currency) }}
          </p>

          <input
            v-else
            v-model="amountInput"
            type="text"
            inputmode="decimal"
            required
            placeholder="0.00"
            data-testid="amount"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-3 text-2xl font-semibold tabular-nums"
            style="border-color: var(--border)"
          />
        </label>

        <label class="flex shrink-0 flex-col gap-1">
          <span class="flex items-baseline justify-between gap-2 text-xs text-[var(--text-muted)]">
            <!--
              Named by the date it holds once that is not today. A date input shows
              the day in figures, in whatever order the phone's locale puts them,
              and that is exactly the thing an eye slides over - which is how a
              week of expenses went into a month nobody meant.
            -->
            <span :class="isToday ? '' : 'text-accent'">
              {{ isToday ? t('Date') : spentAtLabel }}
            </span>
            <button
              v-if="!isEditing && !isToday"
              type="button"
              data-testid="use-today"
              class="text-accent underline"
              @click="useToday"
            >{{ t('Today') }}
            </button>
          </span>
          <input
            v-model="spentAt"
            type="date"
            data-testid="spent-at"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            :style="{ borderColor: isToday ? 'var(--border)' : 'var(--accent-text)' }"
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
          its history, comments and audit trail with it, which is the move at the
          foot of this screen; a dropdown here would look like it did that and
          would not.
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

        <label v-if="!isShared" class="flex min-w-0 flex-col gap-1">
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

      <!--
        More than one person paying for the same thing: two cards at the till, or
        somebody short of cash. What each of them put in is not the same question as
        what each of them owes, so it is asked here and split below as usual.
      -->
      <div v-if="isShared" class="flex flex-col gap-2">
        <div class="flex items-baseline justify-between gap-2">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Who paid') }}</span>
          <button
            type="button"
            data-testid="single-payer"
            class="text-xs text-accent"
            @click="stopSharing"
          >{{ t('One person paid') }}
          </button>
        </div>

        <div
          v-for="(payer, index) in payers"
          :key="index"
          data-testid="payer-row"
          class="flex items-center gap-2"
        >
          <select
            v-model="payer.memberId"
            data-testid="payer-member"
            class="tap-target min-w-0 flex-1 rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
          >
            <option v-for="member in members" :key="member.id" :value="member.id">
              {{ member.displayName }}
            </option>
          </select>

          <input
            v-model="payer.amount"
            type="text"
            inputmode="decimal"
            placeholder="0.00"
            data-testid="payer-amount"
            class="tap-target w-24 shrink-0 rounded-lg border bg-[var(--surface-raised)] px-2 text-right text-sm tabular-nums"
            style="border-color: var(--border)"
          />

          <button
            type="button"
            data-testid="remove-payer"
            class="tap-target shrink-0 px-2 text-sm text-[var(--text-muted)]"
            :aria-label="t('Remove')"
            @click="removePayer(index)"
          >
            <span aria-hidden="true">x</span>
          </button>
        </div>

        <button
          v-if="availablePayers.length > 0"
          type="button"
          data-testid="add-payer"
          class="btn btn-press btn-secondary min-h-0 self-start px-3 py-1.5 text-xs"
          style="border-color: var(--border)"
          @click="addPayer"
        >{{ t('Add someone who paid') }}
        </button>
      </div>

      <button
        v-else-if="members.length > 1"
        type="button"
        data-testid="share-payment"
        class="self-start text-xs text-accent"
        @click="sharePayment"
      >{{ t('Several people paid') }}
      </button>

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

    <!--
      Below the form, and outside it.

      Moving is not one of the six questions the form asks: it is a thing done to
      an expense that already exists, the way archiving is a thing done to a group.
      Inside the form it would be a second submit button beside the first, and the
      one that moved the expense somewhere else would be the one people pressed by
      accident.
    -->
    <section v-if="isEditing" class="surface-card mt-4 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Move to another group') }}</h2>

      <p v-if="moveTargets.length === 0" class="text-xs text-[var(--text-muted)]">{{ t('There is no other group in {currency} to move this to.', { currency }) }}
      </p>

      <template v-else-if="!isMoveOpen">
        <p class="mb-3 text-xs text-[var(--text-muted)]">{{ t('It goes with its history, its comments and who paid. Anything typed above and not saved stays behind.') }}
        </p>

        <button
          type="button"
          data-testid="move-open"
          class="btn btn-press btn-secondary w-full"
          style="border-color: var(--border)"
          @click="isMoveOpen = true"
        >{{ t('Move to another group') }}
        </button>
      </template>

      <div v-else class="flex flex-col gap-3" data-testid="move-panel">
        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Which group') }}</span>
          <select
            :value="moveTargetId"
            data-testid="move-target"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3 text-sm"
            style="border-color: var(--border)"
            @change="chooseMoveTarget(($event.target as HTMLSelectElement).value)"
          >
            <option value="" disabled>{{ t('Choose a group') }}</option>
            <option v-for="option in moveTargets" :key="option.id" :value="option.id">
              {{ option.name }}
            </option>
          </select>
        </label>

        <!--
          Only the people the two groups could not work out between them. Somebody
          in both under the same account, or under the same name, is not worth
          asking about; anyone else has to be named, because putting a debt on the
          wrong person is not something the group would notice.
        -->
        <div v-if="unmatchedMembers.length > 0" class="flex flex-col gap-2">
          <p class="text-xs text-[var(--text-muted)]">{{ t('These people are not in {group}. Say who they are there.', { group: moveTarget?.name ?? '' }) }}
          </p>

          <label
            v-for="person in unmatchedMembers"
            :key="person.id"
            class="flex items-center gap-2"
            data-testid="move-mapping"
          >
            <span class="min-w-0 flex-1 truncate text-sm">{{ person.name }}</span>
            <select
              v-model="moveMapping[person.id]"
              :data-testid="`move-mapping-${person.id}`"
              class="tap-target min-w-0 flex-1 rounded-lg border bg-[var(--surface)] px-2 text-sm"
              style="border-color: var(--border)"
              :aria-label="`Who ${person.name} is in ${moveTarget?.name ?? 'the other group'}`"
            >
              <option value="">{{ t('Choose someone') }}</option>
              <option v-for="member in targetMembers" :key="member.id" :value="member.id">
                {{ member.displayName }}
              </option>
            </select>
          </label>
        </div>

        <p v-if="moveError" class="text-sm text-owing" role="alert">{{ moveError }}</p>

        <div class="flex gap-2">
          <button
            type="button"
            class="btn btn-press btn-secondary flex-1"
            style="border-color: var(--border)"
            @click="isMoveOpen = false"
          >{{ t('Cancel') }}
          </button>
          <button
            type="button"
            data-testid="move-confirm"
            class="btn btn-press btn-primary flex-1"
            :disabled="!isMoveReady || isMoving"
            @click="move"
          >
            {{ isMoving ? t('Moving') : t('Move it') }}
          </button>
        </div>
      </div>
    </section>
  </AppShell>
</template>
