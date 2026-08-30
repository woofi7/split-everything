<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useCategoriesStore } from '@/stores/categories'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { calculateSplit, type SplitType } from '@/domain/splitting'
import { parseAmountInput } from '@/domain/money'

const groups = useGroupsStore()
const categories = useCategoriesStore()
const expenses = useExpensesStore()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const groupId = ref(String(route.query.groupId ?? ''))
const description = ref('')
const amountInput = ref('')
const spentAt = ref(new Date().toISOString().slice(0, 10))
const splitType = ref<SplitType>('Equal')
const categoryId = ref<string | null>(null)
const paidByMemberId = ref('')
const participantIds = ref<string[]>([])
const splitValues = ref<Record<string, number>>({})
const error = ref<string | null>(null)
const isSaving = ref(false)

const splitTypes: Array<{ value: SplitType; label: string }> = [
  { value: 'Equal', label: 'Equally' },
  { value: 'Percentage', label: 'By percentage' },
  { value: 'Shares', label: 'By shares' },
  { value: 'ExactAmount', label: 'Exact amounts' },
]

onMounted(async () => {
  void categories.load()
  await groups.loadAll()
  // The main group, then whatever the query asked for, then anything at all. The
  // query wins when it is there, because it means someone arrived from a group.
  await selectGroup(groupId.value || groups.mainGroupId || groups.visibleGroups[0]?.id || '')
})

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
  splitValues.value = {}
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
    return caught instanceof Error ? caught.message : 'That split does not add up.'
  }
})

const memberName = (memberId: string) =>
  members.value.find((member) => member.id === memberId)?.displayName ?? 'Unknown'

function toggleParticipant(memberId: string): void {
  const index = participantIds.value.indexOf(memberId)
  if (index >= 0) participantIds.value.splice(index, 1)
  else participantIds.value.push(memberId)
}

async function save(): Promise<void> {
  error.value = null

  if (!group.value) {
    error.value = 'Pick a group first.'
    return
  }

  isSaving.value = true

  try {
    await expenses.add({
      groupId: group.value.id,
      paidByMemberId: paidByMemberId.value,
      description: description.value,
      amount: amount.value,
      currency: currency.value,
      spentAt: new Date(`${spentAt.value}T12:00:00Z`),
      splitType: splitType.value,
      participantIds: participantIds.value,
      splitValues: splitValues.value,
      categoryId: categoryId.value,
    })

    // Queued locally, so this returns straight away whether online or not.
    await router.replace({ name: 'group', params: { groupId: group.value.id } })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not save the expense.'
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <AppShell title="Add expense">
    <form class="flex flex-col gap-5" @submit.prevent="save">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Group</span>
        <select
          :value="groupId"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
          @change="selectGroup(($event.target as HTMLSelectElement).value)"
        >
          <option v-for="option in groups.visibleGroups" :key="option.id" :value="option.id">
            {{ option.name }}
          </option>
        </select>
      </label>

      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">What was it</span>
        <input
          v-model="description"
          type="text"
          required
          maxlength="500"
          placeholder="Groceries"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        />
      </label>

      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Category</span>
        <!--
          Offered at last: the API has served these from the start and nothing
          asked, so every expense was uncategorised and the by-category breakdown
          in stats read "Uncategorised, 100%".
        -->
        <select
          v-model="categoryId"
          data-testid="category"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        >
          <option :value="null">No category</option>
          <option v-for="category in categories.all" :key="category.id" :value="category.id">
            {{ category.name }}
          </option>
        </select>
      </label>

      <div class="grid grid-cols-2 gap-3">
        <label class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">Amount ({{ currency }})</span>
          <input
            v-model="amountInput"
            type="text"
            inputmode="decimal"
            required
            placeholder="0.00"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3 text-lg tabular-nums"
            style="border-color: var(--border)"
          />
        </label>

        <label class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">Date</span>
          <input
            v-model="spentAt"
            type="date"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          />
        </label>
      </div>

      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Who paid</span>
        <select
          v-model="paidByMemberId"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        >
          <option v-for="member in members" :key="member.id" :value="member.id">
            {{ member.displayName }}
          </option>
        </select>
      </label>

      <fieldset class="flex flex-col gap-2">
        <legend class="text-sm text-[var(--text-muted)]">Split</legend>
        <div class="flex flex-wrap gap-2">
          <button
            v-for="option in splitTypes"
            :key="option.value"
            type="button"
            class="tap-target rounded-full border px-3 text-sm"
            :class="splitType === option.value ? 'border-brand-500 text-brand-400' : ''"
            :style="splitType === option.value ? undefined : 'border-color: var(--border)'"
            @click="splitType = option.value"
          >
            {{ option.label }}
          </button>
        </div>
      </fieldset>

      <fieldset class="flex flex-col gap-2">
        <legend class="text-sm text-[var(--text-muted)]">Between</legend>
        <ul class="flex flex-col gap-2">
          <li
            v-for="member in members"
            :key="member.id"
            class="flex items-center justify-between gap-3"
          >
            <label class="tap-target flex flex-1 items-center gap-2 text-sm">
              <input
                type="checkbox"
                :checked="participantIds.includes(member.id)"
                @change="toggleParticipant(member.id)"
              />
              {{ member.displayName }}
            </label>

            <input
              v-if="splitType !== 'Equal' && participantIds.includes(member.id)"
              v-model.number="splitValues[member.id]"
              type="number"
              inputmode="decimal"
              step="0.01"
              class="w-24 rounded-lg border bg-[var(--surface-raised)] px-2 py-1 text-right tabular-nums"
              style="border-color: var(--border)"
              :aria-label="`${splitType} for ${member.displayName}`"
            />

            <MoneyAmount
              v-else-if="preview.length > 0 && participantIds.includes(member.id)"
              :amount="preview.find((share) => share.memberId === member.id)?.amount ?? 0"
              :currency="currency"
              size="sm"
            />
          </li>
        </ul>
      </fieldset>

      <section
        v-if="preview.length > 0"
        class="surface-card p-3 text-sm"
        aria-live="polite"
      >
        <p class="mb-2 text-[var(--text-muted)]">Each person owes</p>
        <ul class="flex flex-col gap-1">
          <li v-for="share in preview" :key="share.memberId" class="flex justify-between">
            <span>{{ memberName(share.memberId) }}</span>
            <MoneyAmount :amount="share.amount" :currency="currency" size="sm" />
          </li>
        </ul>
      </section>

      <p v-if="previewProblem" class="text-sm text-[var(--text-muted)]">{{ previewProblem }}</p>
      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

      <button
        type="submit"
        class="btn btn-press btn-primary w-full"
        :disabled="isSaving || preview.length === 0"
      >
        {{ isSaving ? 'Saving' : 'Save expense' }}
      </button>

      <p class="text-center text-xs text-[var(--text-muted)]">
        Saved on this device straight away, and synced when you are back online.
      </p>
    </form>
  </AppShell>
</template>
