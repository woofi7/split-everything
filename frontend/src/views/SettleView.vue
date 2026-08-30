<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { parseAmountInput } from '@/domain/money'

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()
const expenses = useExpensesStore()

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
})

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))
const members = computed(() => groups.membersOf(groupId.value).filter((m) => m.status === 'Active'))
const currency = computed(() => group.value?.baseCurrency ?? 'CAD')
const amount = computed(() => parseAmountInput(amountInput.value) ?? 0)

const plan = computed(() => expenses.settleUpPlan(groupId.value))
const memberName = (memberId: string) =>
  members.value.find((member) => member.id === memberId)?.displayName ?? 'Unknown'

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
