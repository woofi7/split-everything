<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useApi } from '@/api/provider'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'

interface CategorySpend {
  categoryId: string | null
  categoryKey: string
  categoryName: string
  colorHex: string
  amount: number
  expenseCount: number
  share: number
}

interface SpendPoint {
  bucket: string
  amount: number
  expenseCount: number
}

interface MemberSpend {
  memberId: string
  memberName: string
  paid: number
  owed: number
  net: number
}

interface Dashboard {
  currency: string
  totalSpend: number
  myShare: number
  myPaid: number
  expenseCount: number
  spendOverTime: SpendPoint[]
  byCategory: CategorySpend[]
  byMember: MemberSpend[]
}

const groups = useGroupsStore()
const expenses = useExpensesStore()

const dashboard = ref<Dashboard | null>(null)
const groupId = ref<string>('')
const granularity = ref<'day' | 'week' | 'month'>('month')
const isLoading = ref(true)
const isOffline = ref(false)


onMounted(async () => {
  await groups.loadAll()
  await load()
})

async function load(): Promise<void> {
  isLoading.value = true
  try {
    dashboard.value = await useApi().get<Dashboard>('/stats', {
      groupId: groupId.value || undefined,
      granularity: granularity.value,
    })
    isOffline.value = false
  } catch {
    isOffline.value = true
  } finally {
    isLoading.value = false
  }
}

/** Scaled against the largest bucket, so the bars are readable at any spend level. */
const peak = computed(() =>
  Math.max(1, ...(dashboard.value?.spendOverTime.map((point) => point.amount) ?? [1])),
)

const bucketLabel = (bucket: string) =>
  new Date(`${bucket}T00:00:00Z`).toLocaleDateString(undefined, {
    month: 'short',
    year: granularity.value === 'month' ? '2-digit' : undefined,
    day: granularity.value === 'month' ? undefined : 'numeric',
  })
</script>

<template>
  <AppShell
    title="Stats"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-offline="isOffline"
    :is-syncing="expenses.isSyncing"
  >
    <div class="mb-4 flex gap-2">
      <select
        v-model="groupId"
        class="tap-target flex-1 rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
        style="border-color: var(--border)"
        @change="load"
      >
        <option value="">All groups</option>
        <option v-for="group in groups.visibleGroups" :key="group.id" :value="group.id">
          {{ group.name }}
        </option>
      </select>

      <select
        v-model="granularity"
        class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
        style="border-color: var(--border)"
        @change="load"
      >
        <option value="day">Daily</option>
        <option value="week">Weekly</option>
        <option value="month">Monthly</option>
      </select>
    </div>

    <template v-if="dashboard">
      <section class="surface-card mb-4 grid grid-cols-3 gap-3 p-4 text-center">
        <div>
          <p class="text-xs text-[var(--text-muted)]">Total</p>
          <MoneyAmount :amount="dashboard.totalSpend" :currency="dashboard.currency" size="sm" />
        </div>
        <div>
          <p class="text-xs text-[var(--text-muted)]">Your share</p>
          <MoneyAmount :amount="dashboard.myShare" :currency="dashboard.currency" size="sm" />
        </div>
        <div>
          <p class="text-xs text-[var(--text-muted)]">You paid</p>
          <MoneyAmount :amount="dashboard.myPaid" :currency="dashboard.currency" size="sm" />
        </div>
      </section>

      <section v-if="dashboard.spendOverTime.length > 0" class="surface-card mb-4 p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">Spending over time</h2>
        <ul class="flex h-32 items-end gap-1" role="img" aria-label="Spending over time">
          <li
            v-for="point in dashboard.spendOverTime"
            :key="point.bucket"
            class="flex-1"
            :title="`${bucketLabel(point.bucket)}: ${point.amount}`"
          >
            <span
              class="block w-full rounded-t bg-brand-500"
              :style="{ height: `${Math.max(4, (point.amount / peak) * 100)}%` }"
            />
          </li>
        </ul>
        <div class="mt-1 flex justify-between text-[10px] text-[var(--text-muted)]">
          <span>{{ bucketLabel(dashboard.spendOverTime[0].bucket) }}</span>
          <span>
            {{ bucketLabel(dashboard.spendOverTime[dashboard.spendOverTime.length - 1].bucket) }}
          </span>
        </div>
      </section>

      <section v-if="dashboard.byCategory.length > 0" class="surface-card mb-4 p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">By category</h2>
        <ul class="flex flex-col gap-2">
          <li v-for="category in dashboard.byCategory" :key="category.categoryKey">
            <div class="flex justify-between text-sm">
              <span>{{ category.categoryName }}</span>
              <MoneyAmount :amount="category.amount" :currency="dashboard.currency" size="sm" />
            </div>
            <div class="mt-1 h-1.5 rounded-full bg-[var(--surface-sunken)]">
              <span
                class="block h-full rounded-full"
                :style="{
                  width: `${Math.round(category.share * 100)}%`,
                  backgroundColor: category.colorHex,
                }"
              />
            </div>
          </li>
        </ul>
      </section>

      <section v-if="dashboard.byMember.length > 0" class="surface-card p-4">
        <h2 class="mb-3 text-sm font-medium text-[var(--text-muted)]">Who owes whom</h2>
        <ul class="flex flex-col gap-2 text-sm">
          <li v-for="member in dashboard.byMember" :key="member.memberId" class="flex justify-between">
            <span>{{ member.memberName }}</span>
            <MoneyAmount :amount="member.net" :currency="dashboard.currency" signed size="sm" />
          </li>
        </ul>
      </section>
    </template>

    <p v-else-if="isLoading" class="py-12 text-center text-sm text-[var(--text-muted)]">
      Loading stats
    </p>

    <p v-else class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      Stats need a connection. Your groups and expenses still work offline.
    </p>
  </AppShell>
</template>
