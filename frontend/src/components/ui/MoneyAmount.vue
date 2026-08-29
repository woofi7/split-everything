<script setup lang="ts">
import { computed } from 'vue'
import { formatMoney } from '@/domain/money'

const props = withDefaults(
  defineProps<{
    amount: number
    currency: string
    /** Colours the amount green when owed to you and red when you owe. */
    signed?: boolean
    size?: 'sm' | 'md' | 'lg'
  }>(),
  { signed: false, size: 'md' },
)

const formatted = computed(() => formatMoney(Math.abs(props.amount), props.currency))

const isSettled = computed(() => Math.abs(props.amount) < 0.005)

const toneClass = computed(() => {
  if (!props.signed || isSettled.value) return 'text-[var(--text)]'
  return props.amount > 0 ? 'text-owed' : 'text-owing'
})

const sizeClass = computed(
  () =>
    ({
      sm: 'text-sm',
      md: 'text-base',
      lg: 'text-2xl font-semibold',
    })[props.size],
)
</script>

<template>
  <span
    :class="[toneClass, sizeClass, 'tabular-nums whitespace-nowrap']"
    :data-settled="isSettled ? 'true' : 'false'"
  >
    <template v-if="signed && !isSettled">{{ amount > 0 ? '+' : '-' }}</template>
    {{ formatted }}
  </span>
</template>
