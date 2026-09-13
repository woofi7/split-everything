<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore, type CrossGroupBalance } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
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
const error = ref<string | null>(null)
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

/**
 * What the two of them owe each other everywhere else.
 *
 * Two people who share more than one group can owe each other in both directions
 * at once - the flat one way, a trip the other - and paying both in full is two
 * transfers where none is needed. This screen is already about settling with
 * somebody, so it is where the app should say so.
 *
 * Only for a person with an account: a placeholder exists in one group and nowhere
 * else, so there is nothing of theirs to look up in another.
 */
const crossGroup = ref<CrossGroupBalance | null>(null)
const isOffsetting = ref(false)
const offsetError = ref<string | null>(null)
const offsetMessage = ref<string | null>(null)

/** The other side of this settlement, when that is a person with an account. */
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
    // Only worth a word when there is something to cancel; a single group between
    // two people is the ordinary case and needs no explaining.
    crossGroup.value = balance.offsets.length > 0 ? balance : null
  } catch {
    // A convenience on top of a screen that works without it.
    crossGroup.value = null
  }
}

watch(otherUserId, () => void loadCrossGroup())

const offsetTotal = computed(() =>
  (crossGroup.value?.offsets ?? []).reduce((sum, offset) => sum + offset.amount, 0),
)

async function offsetAcrossGroups(): Promise<void> {
  if (!otherUserId.value) return

  offsetError.value = null
  isOffsetting.value = true

  try {
    const result = await expenses.offsetAcrossGroups(otherUserId.value)
    const left = result.remaining.find((entry) => entry.net !== 0)

    offsetMessage.value = left?.groupName
      ? t('Cancelled. {amount} is left in {group}.', {
          amount: formatMoney(Math.abs(left.net), left.currency),
          group: left.groupName,
        })
      : t('Cancelled. You are square.')

    await loadCrossGroup()
    await expenses.hydrate()
  } catch (caught) {
    offsetError.value =
      caught instanceof Error ? caught.message : t('Could not cancel those out.')
  } finally {
    isOffsetting.value = false
  }
}

function usePlan(transfer: { fromMemberId: string; toMemberId: string; amount: number }): void {
  fromMemberId.value = transfer.fromMemberId
  toMemberId.value = transfer.toMemberId
  amountInput.value = String(transfer.amount)
}

async function save(): Promise<void> {
  error.value = null
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
    error.value = caught instanceof Error ? caught.message : t('Could not record the settlement.')
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

    <!--
      The balance these two hold everywhere else.

      Shown only when some of it faces the other way, because that is the only
      time there is anything to do about it: a debt in one group and the opposite
      debt in another are the same two people, and settling both in full is two
      transfers where none is needed.
    -->
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

      <p v-if="offsetMessage" data-testid="offset-done" class="mb-2 text-xs text-owed">{{ offsetMessage }}</p>
      <p v-if="offsetError" class="mb-2 text-sm text-owing" role="alert">{{ offsetError }}</p>

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

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

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
