<script setup lang="ts">
import { intlLocale, t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import AddKindSwitch from '@/components/expenses/AddKindSwitch.vue'
import CategoryPicker from '@/components/expenses/CategoryPicker.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { notify, report } from '@/ui/toasts'
import { calculateSplit, splitValuesFor, type SplitType } from '@/domain/splitting'
import { formatMoney, parseAmountInput, roundMoney } from '@/domain/money'
import { memberColor, memberColors } from '@/domain/memberColors'
import { lastExpenseDate, rememberExpenseDate, today } from '@/domain/lastExpenseDate'
import { bucketOf } from '@/domain/buckets'
import { categoriseByKeywords, categoryFor } from '@/domain/categories'

const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const editingId = computed(() => {
  const id = route.params.expenseId
  return typeof id === 'string' && id ? id : null
})

const isEditing = computed(() => editingId.value !== null)

const groupId = ref(String(route.params.groupId ?? route.query.groupId ?? ''))
const description = ref('')
const amountInput = ref('')

const spentAt = ref(isEditing.value ? today() : (lastExpenseDate() ?? today()))

const isToday = computed(() => spentAt.value === today())

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

const categoryKey = ref<string | null>(null)
const isGuess = ref(false)

const splitType = ref<SplitType>('Equal')
const paidByMemberId = ref('')

const payers = ref<Array<{ memberId: string; amount: string }>>([])
const participantIds = ref<string[]>([])
const splitValues = ref<Record<string, number>>({})
const isSaving = ref(false)
const makeDefault = ref(false)

const splitTypes: Array<{ value: SplitType; label: string }> = [
  { value: 'Equal', label: t('Equally') },
  { value: 'Percentage', label: t('Percent') },
  { value: 'Shares', label: t('Shares') },
  { value: 'ExactAmount', label: t('Exact') },
]

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()

  await selectGroup(groupId.value || groups.mainGroupId || groups.visibleGroups[0]?.id || '')

  if (isEditing.value) prefillFromExpense()
})

function prefillFromExpense(): void {
  const existing = expenses.expenses.find((candidate) => candidate.id === editingId.value)
  if (!existing) {
    notify(t('That expense is not on this device.'), 'error')
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

  categoryKey.value = existing.categoryKey ?? null
  isGuess.value = false
}

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const categories = computed(() => groups.categoriesOf(groupId.value))

watch([description, categories], () => {
  if (!isGuess.value && categoryKey.value !== null) return

  const guessed = categoriseByKeywords(description.value, categories.value)
  categoryKey.value = guessed
  isGuess.value = guessed !== null
})

function chooseCategory(key: string): void {
  categoryKey.value = key || null
  isGuess.value = false
}

const guessedName = computed(() =>
  isGuess.value ? (categoryFor(categoryKey.value, categories.value)?.name ?? null) : null,
)

const isCreatingCategory = ref(false)

async function createCategory(name: string): Promise<void> {
  const wanted = name.trim()
  if (!wanted || !groupId.value) return

  const wasFollowingTheServer = !groups.hasOwnCategories(groupId.value)
  isCreatingCategory.value = true

  try {
    const saved = await groups.setCategories(groupId.value, [
      ...categories.value.map((category) => ({
        key: category.key,
        name: category.name,
        iconName: category.iconName,
        colorHex: category.colorHex,
        keywords: category.keywords,
      })),
      { name: wanted },
    ])

    const created = saved.find(
      (category) => category.name.toLowerCase() === wanted.toLowerCase(),
    )
    if (created) chooseCategory(created.key)

    notify(
      wasFollowingTheServer
        ? t('{name} added. This group now keeps its own list of categories.', { name: wanted })
        : t('{name} added.', { name: wanted }),
      'done',
    )
  } catch (caught) {
    report(caught, t('Could not add that category. It needs a connection.'))
  } finally {
    isCreatingCategory.value = false
  }
}
const members = computed(() => group.value?.members.filter((m) => m.status === 'Active') ?? [])
const currency = computed(() => group.value?.baseCurrency ?? auth.user?.defaultCurrency ?? 'CAD')
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

const availablePayers = computed(() =>
  members.value.filter(
    (member) => !payers.value.some((payer) => payer.memberId === member.id),
  ),
)

function sharePayment(): void {
  const others = members.value.filter((member) => member.id !== paidByMemberId.value)

  payers.value = [
    { memberId: paidByMemberId.value, amount: amountInput.value.trim() || '' },
    { memberId: others[0]?.id ?? '', amount: '' },
  ].filter((payer) => payer.memberId !== '')
}

function stopSharing(): void {
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

async function selectGroup(nextGroupId: string): Promise<void> {
  groupId.value = nextGroupId
  if (!nextGroupId) return

  const loaded = await groups.refresh(nextGroupId)
  const active = (loaded?.members ?? groups.membersOf(nextGroupId)).filter(
    (member) => member.status === 'Active',
  )

  participantIds.value = active.map((member) => member.id)

  const mine = active.find((member) => member.userId === auth.user?.id)?.id
  paidByMemberId.value = mine ?? active[0]?.id ?? ''

  applyGroupDefault(loaded?.defaultSplitType, loaded?.defaultSplitValues, active.map((m) => m.id))
}

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

const isMoveOpen = ref(false)
const moveTargetId = ref('')
const moveMapping = ref<Record<string, string>>({})
const isMoving = ref(false)

const editing = computed(() =>
  expenses.expenses.find((candidate) => candidate.id === editingId.value),
)

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

function matchInTarget(memberId: string) {
  const source = groups.membersOf(groupId.value).find((member) => member.id === memberId)
  if (!source) return undefined

  return source.userId
    ? targetMembers.value.find((member) => member.userId === source.userId)
    : targetMembers.value.find(
        (member) => member.displayName.toLowerCase() === source.displayName.toLowerCase(),
      )
}

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

async function chooseMoveTarget(nextGroupId: string): Promise<void> {
  moveTargetId.value = nextGroupId
  moveMapping.value = {}

  if (!nextGroupId) return

  await groups.refresh(nextGroupId)

  if (targetMembers.value.length === 0) {
    notify(t('Could not read who is in that group. Moving an expense needs a connection.'), 'error')
  }
}

async function move(): Promise<void> {
  if (!isMoveReady.value || !editingId.value) return

  isMoving.value = true

  try {
    await expenses.transfer(editingId.value, moveTargetId.value, { ...moveMapping.value })

    await router.replace({
      name: 'expense',
      params: { groupId: moveTargetId.value, expenseId: editingId.value },
    })
  } catch (caught) {
    report(caught, t('Could not move the expense.'))
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

function shareOf(memberId: string): number | null {
  if (!participantIds.value.includes(memberId)) return null
  return preview.value.find((share) => share.memberId === memberId)?.amount ?? null
}

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
  if (!group.value) {
    notify(t('Pick a group first.'), 'error')
    return
  }

  isSaving.value = true

  const contributions = payers.value
    .filter((payer) => payer.memberId && (parseAmountInput(payer.amount) ?? 0) > 0)
    .map((payer) => ({
      memberId: payer.memberId,
      amount: parseAmountInput(payer.amount) as number,
    }))

  if (isShared.value && contributions.length < 2) {
    notify(t('Say what each person paid, or go back to a single payer.'), 'error')
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
    categoryKey: categoryKey.value,
  }

  if (makeDefault.value) {
    try {
      await groups.setDefaultSplit(
        group.value.id,
        splitType.value,
        splitType.value === 'Equal' ? null : { ...splitValues.value },
      )
    } catch {
      notify(t('Saved, but the group default could not be changed.'), 'error')
    }
  }

  try {
    if (editingId.value) {
      await expenses.edit(editingId.value, fields)

      await router.replace({
        name: 'expense',
        params: { groupId: group.value.id, expenseId: editingId.value },
      })
    } else {
      const added = await expenses.add({ groupId: group.value.id, ...fields })

      rememberExpenseDate(spentAt.value)

      await router.replace({
        name: 'group',
        params: { groupId: group.value.id },
        query: { month: bucketOf(fields.spentAt, 'month'), added: added.id },
      })
    }
  } catch (caught) {
    report(caught, t('Could not save the expense.'))
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
    <AddKindSwitch v-if="!isEditing" current="expense" />
    <form class="flex flex-col gap-3" @submit.prevent="save">
      <div class="flex items-end gap-3">
        <label class="flex min-w-0 flex-1 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">
            {{ t('Amount ({currency})', { currency }) }}
          </span>
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
        <label v-if="categories.length > 0" class="flex min-w-0 flex-col gap-1">
          <span class="flex items-baseline justify-between gap-2 text-xs text-[var(--text-muted)]">
            <span>{{ t('Category') }}</span>
            <span v-if="guessedName" data-testid="category-guess" class="truncate text-accent">
              {{ t('guessed') }}
            </span>
          </span>
          <CategoryPicker
            :model-value="categoryKey"
            :categories="categories"
            :is-guess="isGuess"
            :is-creating="isCreatingCategory"
            @update:model-value="chooseCategory($event ?? '')"
            @create="createCategory"
          />
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
      <p class="text-center text-[11px] text-[var(--text-muted)]">{{ t('Saved on this device straight away, and synced when you are back online.') }}
      </p>
      <button
        type="submit"
        class="btn btn-press btn-primary sticky bottom-2 mt-1 w-full"
        :disabled="isSaving || preview.length === 0"
      >
        {{ isSaving ? t('Saving') : isEditing ? t('Save changes') : t('Save expense') }}
      </button>
    </form>
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
