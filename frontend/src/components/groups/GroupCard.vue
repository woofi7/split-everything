<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import type { LocalGroup } from '@/offline/db'

const props = defineProps<{ group: LocalGroup }>()

const isSettled = computed(() => Math.abs(props.group.myNetBalance) < 0.005)

const balanceLabel = computed(() => {
  if (isSettled.value) return 'Settled up'
  return props.group.myNetBalance > 0 ? 'You are owed' : 'You owe'
})

const memberSummary = computed(() => {
  const active = props.group.members.filter((member) => member.status === 'Active')
  if (active.length === 0) return 'Just you'
  if (active.length <= 3) return active.map((member) => member.displayName).join(', ')
  return `${active.slice(0, 2).map((m) => m.displayName).join(', ')} and ${active.length - 2} more`
})
</script>

<template>
  <RouterLink
    :to="{ name: 'group', params: { groupId: group.id } }"
    class="surface-card tap-target flex items-center gap-3 p-4"
    :data-archived="group.isArchived ? 'true' : 'false'"
  >
    <span
      class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl text-sm font-semibold text-white"
      :style="{ backgroundColor: group.colorHex }"
      aria-hidden="true"
    >
      {{ group.emojiIcon || group.name.slice(0, 2).toUpperCase() }}
    </span>

    <span class="min-w-0 flex-1">
      <span class="flex items-center gap-2">
        <span class="truncate font-medium">{{ group.name }}</span>
        <span
          v-if="group.isArchived"
          class="rounded-full border px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-[var(--text-muted)]"
          style="border-color: var(--border)"
        >
          Archived
        </span>
      </span>
      <span class="block truncate text-sm text-[var(--text-muted)]">{{ memberSummary }}</span>
    </span>

    <span class="shrink-0 text-right">
      <span class="block text-xs text-[var(--text-muted)]">{{ balanceLabel }}</span>
      <MoneyAmount
        :amount="group.myNetBalance"
        :currency="group.baseCurrency"
        signed
        size="md"
      />
    </span>
  </RouterLink>
</template>
