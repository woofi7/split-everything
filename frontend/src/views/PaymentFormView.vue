<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import AddKindSwitch from '@/components/expenses/AddKindSwitch.vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faRightLeft } from '@fortawesome/free-solid-svg-icons'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { notify, report } from '@/ui/toasts'
import { parseAmountInput } from '@/domain/money'
import { today } from '@/domain/lastExpenseDate'

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()

const groupId = ref(String(route.query.groupId ?? ''))
const amountInput = ref('')
const paidOn = ref(today())
const fromMemberId = ref('')
const toMemberId = ref('')
const note = ref('')
const isSaving = ref(false)

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()
  await selectGroup(groupId.value || groups.mainGroupId || groups.visibleGroups[0]?.id || '')
})

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const members = computed(() => group.value?.members.filter((m) => m.status === 'Active') ?? [])
const currency = computed(() => group.value?.baseCurrency ?? auth.user?.defaultCurrency ?? 'CAD')
const amount = computed(() => parseAmountInput(amountInput.value) ?? 0)

async function selectGroup(nextGroupId: string): Promise<void> {
  groupId.value = nextGroupId
  if (!nextGroupId) return

  const loaded = await groups.refresh(nextGroupId)
  const active = (loaded?.members ?? groups.membersOf(nextGroupId)).filter(
    (member) => member.status === 'Active',
  )

  const mine = active.find((member) => member.userId === auth.user?.id)?.id ?? active[0]?.id ?? ''
  const other = active.find((member) => member.id !== mine)?.id ?? ''

  toMemberId.value = mine
  fromMemberId.value = other
}

function swap(): void {
  const wasFrom = fromMemberId.value
  fromMemberId.value = toMemberId.value
  toMemberId.value = wasFrom
}

const memberName = (memberId: string) =>
  members.value.find((member) => member.id === memberId)?.displayName ?? ''

const effect = computed(() => {
  if (!fromMemberId.value || !toMemberId.value || amount.value <= 0) return null

  return t('{from} owes {to} that much less.', {
    from: memberName(fromMemberId.value),
    to: memberName(toMemberId.value),
  })
})

const problem = computed(() => {
  if (!groupId.value) return t('Pick a group.')
  if (amount.value <= 0) return t('Enter an amount.')
  if (!fromMemberId.value || !toMemberId.value) return t('Say who paid whom.')
  if (fromMemberId.value === toMemberId.value) return t('A payment needs two different people.')
  return null
})

async function save(): Promise<void> {
  if (problem.value) {
    notify(problem.value, 'error')
    return
  }

  isSaving.value = true

  try {
    await expenses.settle({
      groupId: groupId.value,
      fromMemberId: fromMemberId.value,
      toMemberId: toMemberId.value,
      amount: amount.value,
      currency: currency.value,
      settledAt: new Date(`${paidOn.value}T12:00:00`),
      note: note.value.trim() || null,
    })

    await router.push({ name: 'group', params: { groupId: groupId.value } })
  } catch (caught) {
    report(caught, t('Could not record that payment.'))
  } finally {
    isSaving.value = false
  }
}
</script>
<template>
  <AppShell :title="t('Add payment')" :back-to="{ name: 'dashboard' }" back-label="Dashboard">
    <AddKindSwitch current="payment" />
    <form class="flex flex-col gap-3" @submit.prevent="save">
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
            data-testid="amount"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-3 text-2xl font-semibold tabular-nums"
            style="border-color: var(--border)"
          />
        </label>
        <label class="flex shrink-0 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Date') }}</span>
          <input
            v-model="paidOn"
            type="date"
            data-testid="paid-on"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
          />
        </label>
      </div>
      <label class="flex flex-col gap-1">
        <span class="text-xs text-[var(--text-muted)]">{{ t('Note (optional)') }}</span>
        <input
          v-model="note"
          type="text"
          data-testid="note"
          maxlength="200"
          :placeholder="t('Interac transfer')"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        />
      </label>
      <label class="flex flex-col gap-1">
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
      <div class="flex items-end gap-2">
        <label class="flex min-w-0 flex-1 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Who paid') }}</span>
          <select
            v-model="fromMemberId"
            data-testid="paid-from"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
          >
            <option v-for="member in members" :key="member.id" :value="member.id">
              {{ member.displayName }}
            </option>
          </select>
        </label>
        <button
          type="button"
          data-testid="swap-sides"
          class="tap-target shrink-0 rounded-lg border px-3 text-accent"
          style="border-color: var(--border)"
          :aria-label="t('The other way round')"
          :title="t('The other way round')"
          @click="swap"
        >
          <FontAwesomeIcon :icon="faRightLeft" class="h-5 w-5" aria-hidden="true" />
        </button>
        <label class="flex min-w-0 flex-1 flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Who received it') }}</span>
          <select
            v-model="toMemberId"
            data-testid="paid-to"
            class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
            style="border-color: var(--border)"
          >
            <option v-for="member in members" :key="member.id" :value="member.id">
              {{ member.displayName }}
            </option>
          </select>
        </label>
      </div>
      <p v-if="effect" data-testid="payment-effect" class="text-xs text-[var(--text-muted)]">
        {{ effect }}
      </p>
      <p class="text-xs text-[var(--text-muted)]">
        {{ t('A payment moves the balance between two people. It is not spending, so it stays out of the expenses and out of the totals.') }}
      </p>
      <button
        type="submit"
        data-testid="save-payment"
        class="btn btn-press btn-primary mt-1"
        :disabled="isSaving"
      >
        {{ isSaving ? t('Recording') : t('Record payment') }}
      </button>
    </form>
  </AppShell>
</template>
