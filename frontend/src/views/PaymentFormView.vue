<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
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

const props = withDefaults(defineProps<{ kind?: 'payment' | 'settlement' }>(), { kind: 'payment' })
const isSettlement = computed(() => props.kind === 'settlement')

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
const moveElsewhere = ref(false)
const amountTyped = ref(false)
const targetGroupId = ref('')

onMounted(async () => {
  await groups.loadAll()
  await expenses.hydrate()
  await selectGroup(groupId.value || groups.mainGroupId || groups.visibleGroups[0]?.id || '')
  if (isSettlement.value) await loadOtherGroups()
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

  if (isSettlement.value && owed.value < 0) swap()
}

const userOf = (memberId: string) => members.value.find((member) => member.id === memberId)?.userId

const targetGroups = computed(() => {
  const fromUser = userOf(fromMemberId.value)
  const toUser = userOf(toMemberId.value)
  if (!fromUser || !toUser) return []

  return groups.visibleGroups.filter((candidate) => {
    if (candidate.id === groupId.value || candidate.baseCurrency !== currency.value) return false

    const users = groups
      .membersOf(candidate.id)
      .filter((member) => member.status === 'Active')
      .map((member) => member.userId)

    return users.includes(fromUser) && users.includes(toUser)
  })
})

const targetGroup = computed(() =>
  targetGroups.value.find((candidate) => candidate.id === targetGroupId.value),
)

watch(targetGroups, (available) => {
  if (!available.some((candidate) => candidate.id === targetGroupId.value)) {
    targetGroupId.value = available[0]?.id ?? ''
  }
})

const sections = computed(() => (isSettlement.value ? ['parties', 'details'] : ['details', 'parties']))

async function loadOtherGroups(): Promise<void> {
  await Promise.all(
    groups.visibleGroups
      .filter((candidate) => candidate.id !== groupId.value)
      .map((candidate) => groups.refresh(candidate.id)),
  )
}

const owed = computed(() => {
  if (!groupId.value || !fromMemberId.value || !toMemberId.value) return 0

  return expenses.rawDebts(groupId.value).reduce((sum, debt) => {
    if (debt.fromMemberId === fromMemberId.value && debt.toMemberId === toMemberId.value) {
      return sum + debt.amount
    }
    if (debt.fromMemberId === toMemberId.value && debt.toMemberId === fromMemberId.value) {
      return sum - debt.amount
    }
    return sum
  }, 0)
})

watch([owed, isSettlement], () => {
  if (!isSettlement.value || amountTyped.value) return
  amountInput.value = owed.value > 0 ? owed.value.toFixed(2) : ''
})

function swap(): void {
  const wasFrom = fromMemberId.value
  fromMemberId.value = toMemberId.value
  toMemberId.value = wasFrom
}

const memberName = (memberId: string) =>
  members.value.find((member) => member.id === memberId)?.displayName ?? ''

const effect = computed(() => {
  if (!fromMemberId.value || !toMemberId.value || amount.value <= 0) return null

  if (moveElsewhere.value && targetGroup.value) {
    return t('{from} owes {to} that much less in {group} and that much more in {target}. No money changes hands.', {
      from: memberName(fromMemberId.value),
      to: memberName(toMemberId.value),
      group: group.value?.name ?? '',
      target: targetGroup.value.name,
    })
  }

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
  if (moveElsewhere.value && !targetGroup.value) return t('Pick the group to move it to.')
  return null
})

async function save(): Promise<void> {
  if (problem.value) {
    notify(problem.value, 'error')
    return
  }

  isSaving.value = true

  try {
    if (moveElsewhere.value && targetGroup.value) {
      await expenses.moveBalance({
        groupId: groupId.value,
        fromMemberId: fromMemberId.value,
        toMemberId: toMemberId.value,
        amount: amount.value,
        targetGroupId: targetGroup.value.id,
        settledAt: new Date(`${paidOn.value}T12:00:00`),
        note: note.value.trim() || null,
      })

      await router.push({ name: 'group', params: { groupId: targetGroup.value.id } })
      return
    }

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
  <AppShell
    :title="isSettlement ? t('Add settlement') : t('Add payment')"
    :back-to="{ name: 'dashboard' }"
    back-label="Dashboard"
  >
    <AddKindSwitch :current="kind" />
    <form class="flex flex-col gap-3" @submit.prevent="save">
      <template v-for="section in sections" :key="section">
        <template v-if="section === 'details'">
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
                @input="amountTyped = true"
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
        </template>
        <template v-else>
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
          <label
            v-if="isSettlement"
            class="flex items-center gap-2 text-sm"
            :class="targetGroups.length > 0 ? 'cursor-pointer' : 'text-[var(--text-muted)]'"
          >
            <input
              v-model="moveElsewhere"
              type="checkbox"
              data-testid="move-elsewhere"
              :disabled="targetGroups.length === 0 && !moveElsewhere"
            />
            {{ t('Move it to another group instead') }}
          </label>
          <p
            v-if="isSettlement && targetGroups.length === 0"
            data-testid="no-target-group"
            class="-mt-2 text-xs text-[var(--text-muted)]"
          >
            {{ t('No other group in {currency} has both of them.', { currency }) }}
          </p>
          <label v-if="moveElsewhere" class="flex flex-col gap-1">
            <span class="text-xs text-[var(--text-muted)]">{{ t('Move it to') }}</span>
            <select
              v-model="targetGroupId"
              data-testid="target-group"
              class="tap-target w-full rounded-lg border bg-[var(--surface-raised)] px-2 text-sm"
              style="border-color: var(--border)"
            >
              <option v-for="option in targetGroups" :key="option.id" :value="option.id">
                {{ option.name }}
              </option>
            </select>
          </label>
          <div class="flex items-end gap-2">
            <label class="flex min-w-0 flex-1 flex-col gap-1">
              <span class="text-xs text-[var(--text-muted)]">{{ isSettlement ? t('Who owes') : t('Who paid') }}</span>
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
              <span class="text-xs text-[var(--text-muted)]">{{ isSettlement ? t('Owed to') : t('Who received it') }}</span>
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
        </template>
      </template>
      <p
        v-if="isSettlement && group && fromMemberId && toMemberId && owed <= 0"
        data-testid="nothing-owed"
        class="text-xs text-[var(--text-muted)]"
      >
        {{ t('Nothing owed between them in {group}.', { group: group.name }) }}
      </p>
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
        {{
          isSaving
            ? t('Recording')
            : moveElsewhere
              ? t('Move the balance')
              : isSettlement
                ? t('Record settlement')
                : t('Record payment')
        }}
      </button>
    </form>
  </AppShell>
</template>
