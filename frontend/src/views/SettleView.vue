<script setup lang="ts">
import { intlLocale, t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore, type CrossGroupBalance } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { notify, report } from '@/ui/toasts'
import { formatMoney, parseAmountInput } from '@/domain/money'

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()

const groupId = computed(() => String(route.params.groupId))
const fromMemberId = ref(String(route.query.from ?? ''))
const toMemberId = ref(String(route.query.to ?? ''))
const amountInput = ref(String(route.query.amount ?? ''))
const note = ref('')
const isSaving = ref(false)

onMounted(async () => {
  await groups.get(groupId.value)
  await expenses.hydrate()
  await loadCrossGroup()
})

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const members = computed(() => groups.membersOf(groupId.value).filter((m) => m.status === 'Active'))
const currency = computed(() => group.value?.baseCurrency ?? 'CAD')
const amount = computed(() => parseAmountInput(amountInput.value) ?? 0)

const plan = computed(() => expenses.settleUpPlan(groupId.value))
const memberName = (memberId: string) =>
  members.value.find((member) => member.id === memberId)?.displayName ?? 'Unknown'

const crossGroup = ref<CrossGroupBalance | null>(null)
const isOffsetting = ref(false)

const otherUserId = computed(() => {
  const mine = members.value.find((member) => member.userId === auth.user?.id)?.id
  const other = [toMemberId.value, fromMemberId.value].find(
    (memberId) => memberId && memberId !== mine,
  )

  return members.value.find((member) => member.id === other)?.userId ?? null
})

async function loadCrossGroup(): Promise<void> {
  crossGroup.value = null
  if (!otherUserId.value) return

  try {
    const balance = await expenses.crossGroupBalance(otherUserId.value)
    crossGroup.value = balance.offsets.length > 0 ? balance : null
  } catch {
    crossGroup.value = null
  }
}

watch(otherUserId, () => void loadCrossGroup())

const offsetTotal = computed(() =>
  (crossGroup.value?.offsets ?? []).reduce((sum, offset) => sum + offset.amount, 0),
)

async function offsetAcrossGroups(): Promise<void> {
  if (!otherUserId.value) return

  isOffsetting.value = true

  try {
    const result = await expenses.offsetAcrossGroups(otherUserId.value)
    const left = result.remaining.find((entry) => entry.net !== 0)

    notify(
      left?.groupName
        ? t('Cancelled. {amount} is left in {group}.', {
            amount: formatMoney(Math.abs(left.net), left.currency),
            group: left.groupName,
          })
        : t('Cancelled. You are square.'),
      'done',
    )

    await loadCrossGroup()
    await expenses.hydrate()

    followThePlan()
  } catch (caught) {
    report(caught, t('Could not cancel those out.'))
  } finally {
    isOffsetting.value = false
  }
}

function followThePlan(): void {
  const next = plan.value.find(
    (transfer) =>
      (transfer.fromMemberId === fromMemberId.value && transfer.toMemberId === toMemberId.value) ||
      (transfer.fromMemberId === toMemberId.value && transfer.toMemberId === fromMemberId.value),
  )

  if (next) usePlan(next)
  else amountInput.value = ''
}

function usePlan(transfer: { fromMemberId: string; toMemberId: string; amount: number }): void {
  fromMemberId.value = transfer.fromMemberId
  toMemberId.value = transfer.toMemberId
  amountInput.value = String(transfer.amount)
}

const recentSettlements = computed(() => expenses.settlementsForGroup(groupId.value).slice(0, 6))

const settledOn = (when: string) =>
  new Date(when).toLocaleDateString(intlLocale.value, { day: 'numeric', month: 'short' })

async function unsettle(settlementId: string): Promise<void> {
  try {
    await expenses.unsettle(settlementId)
    followThePlan()
  } catch (caught) {
    report(caught, t('Could not take that settlement back.'))
  }
}

async function save(): Promise<void> {
  isSaving.value = true

  try {
    await expenses.settle({
      groupId: groupId.value,
      fromMemberId: fromMemberId.value,
      toMemberId: toMemberId.value,
      amount: amount.value,
      currency: currency.value,
      note: note.value.trim() || null,
    })

    await router.replace({ name: 'group', params: { groupId: groupId.value } })
  } catch (caught) {
    report(caught, t('Could not record the settlement.'))
  } finally {
    isSaving.value = false
  }
}
</script>
<template>
  <AppShell
    :title="t('Settle up')"
    :subtitle="group?.name"
    :back-to="{ name: 'group', params: { groupId } }"
    :back-label="group?.name ?? 'Group'"
  >
    <section v-if="plan.length > 0" class="surface-card mb-5 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Suggested transfers') }}</h2>
      <ul class="flex flex-col gap-2 text-sm">
        <li
          v-for="transfer in plan"
          :key="`${transfer.fromMemberId}-${transfer.toMemberId}`"
          class="flex items-center justify-between gap-2"
        >
          <span class="truncate">
            {{ memberName(transfer.fromMemberId) }} pays {{ memberName(transfer.toMemberId) }}
          </span>
          <button
            type="button"
            class="btn btn-press btn-secondary shrink-0 min-h-0 px-2 py-1 text-xs"
            style="border-color: var(--border)"
            @click="usePlan(transfer)"
          >{{ t('Use') }}
            <MoneyAmount :amount="transfer.amount" :currency="currency" size="sm" />
          </button>
        </li>
      </ul>
    </section>
    <section v-if="crossGroup" data-testid="cross-group" class="surface-card mb-5 p-4">
      <h2 class="mb-1 text-sm font-medium text-[var(--text-muted)]">{{ t('You also owe each other elsewhere') }}</h2>
      <p class="mb-3 text-xs text-[var(--text-muted)]">{{ t('{name} and you have balances facing both ways. Cancelling them out moves no money: it writes a settlement in each group so what is really outstanding sits in one place.', { name: crossGroup.withName }) }}
      </p>
      <ul class="mb-3 flex flex-col gap-1.5 text-sm">
        <li
          v-for="entry in crossGroup.groups"
          :key="entry.groupId"
          class="flex items-center justify-between gap-2"
        >
          <span class="min-w-0 truncate">
            {{ entry.groupName }}
            <span v-if="!entry.canSettle" class="text-xs text-[var(--text-muted)]">
              ({{ t('archived') }})
            </span>
          </span>
          <span class="shrink-0 text-xs text-[var(--text-muted)]">
            {{ entry.net > 0 ? t('owes you') : t('you owe') }}
          </span>
          <MoneyAmount :amount="Math.abs(entry.net)" :currency="entry.currency" size="sm" />
        </li>
      </ul>
      <button
        type="button"
        data-testid="offset-across-groups"
        class="btn btn-press btn-secondary w-full"
        style="border-color: var(--border)"
        :disabled="isOffsetting"
        @click="offsetAcrossGroups"
      >
        {{
          isOffsetting
            ? t('Cancelling')
            : t('Cancel out {amount}', { amount: formatMoney(offsetTotal, crossGroup.offsets[0].currency) })
        }}
      </button>
    </section>
    <section v-if="recentSettlements.length > 0" class="surface-card mb-5 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Already settled') }}</h2>
      <ul class="flex flex-col gap-2 text-sm">
        <li
          v-for="entry in recentSettlements"
          :key="entry.id"
          data-testid="settlement-row"
          class="flex items-center justify-between gap-2"
        >
          <span class="min-w-0">
            <span class="block truncate">
              {{ memberName(entry.fromMemberId) }} paid {{ memberName(entry.toMemberId) }}
            </span>
            <span class="block truncate text-xs text-[var(--text-muted)]">
              {{ settledOn(entry.settledAt) }}<template v-if="entry.note"> - {{ entry.note }}</template>
            </span>
          </span>
          <span class="flex shrink-0 items-center gap-2">
            <MoneyAmount :amount="entry.amount" :currency="entry.currency" size="sm" />
            <button
              type="button"
              :data-testid="`unsettle-${entry.id}`"
              class="tap-target px-2 text-xs text-[var(--text-muted)]"
              :aria-label="t('Take this settlement back')"
              :title="t('Take this settlement back')"
              @click="unsettle(entry.id)"
            >
              <span aria-hidden="true">x</span>
            </button>
          </span>
        </li>
      </ul>
    </section>
    <form class="flex flex-col gap-5" @submit.prevent="save">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Who paid') }}</span>
        <select
          v-model="fromMemberId"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        >
          <option v-for="member in members" :key="member.id" :value="member.id">
            {{ member.displayName }}
          </option>
        </select>
      </label>
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Who received it') }}</span>
        <select
          v-model="toMemberId"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        >
          <option v-for="member in members" :key="member.id" :value="member.id">
            {{ member.displayName }}
          </option>
        </select>
      </label>
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Amount ({{ currency }})</span>
        <input
          v-model="amountInput"
          type="text"
          inputmode="decimal"
          required
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3 text-lg tabular-nums"
          style="border-color: var(--border)"
        />
      </label>
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Note') }}</span>
        <input
          v-model="note"
          type="text"
          maxlength="1000"
          :placeholder="t('Etransfer')"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        />
      </label>
      <button
        type="submit"
        class="btn btn-press btn-primary w-full"
        :disabled="isSaving || amount <= 0"
      >
        {{ isSaving ? t('Recording') : t('Record settlement') }}
      </button>
    </form>
  </AppShell>
</template>
