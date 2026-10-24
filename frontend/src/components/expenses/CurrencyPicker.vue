<script setup lang="ts">
import { intlLocale, t } from '@/i18n'
import { computed } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faCoins } from '@fortawesome/free-solid-svg-icons'
import SearchSelect from '@/components/ui/SearchSelect.vue'
import type { SelectOption } from '@/components/ui/selectOption'
import { currencyKeywords, currencyName } from '@/domain/currencies'

const props = withDefaults(
  defineProps<{
    modelValue: string
    currencies: readonly string[]
    baseCurrency?: string | null
    variant?: 'badge' | 'field'
    testid?: string
  }>(),
  { baseCurrency: null, variant: 'badge', testid: 'currency' },
)

const emit = defineEmits<{
  'update:modelValue': [code: string]
}>()

const options = computed<SelectOption[]>(() =>
  props.currencies.map((code) => ({
    value: code,
    label: code,
    hint: code === props.baseCurrency ? `${currencyName(code, intlLocale.value)} · ${t('Group')}` : currencyName(code, intlLocale.value),
    keywords: currencyKeywords(code, intlLocale.value),
  })),
)
</script>
<template>
  <SearchSelect
    :class="variant === 'badge' ? 'absolute inset-y-1 right-1' : ''"
    :model-value="modelValue"
    :options="options"
    :title="t('Currency')"
    :testid="testid"
    searchable
    @update:model-value="emit('update:modelValue', $event ?? modelValue)"
  >
    <template v-if="variant === 'badge'" #trigger="{ toggle, open }">
      <button
        type="button"
        :data-testid="testid"
        :data-value="modelValue"
        :aria-label="t('Currency')"
        aria-haspopup="listbox"
        :aria-expanded="open ? 'true' : 'false'"
        class="flex h-full items-center gap-1.5 rounded-md px-2 text-sm font-medium"
        :class="baseCurrency && modelValue !== baseCurrency ? 'bg-[var(--surface-sunken)] text-accent' : 'text-[var(--text-muted)]'"
        @click="toggle"
      >
        <FontAwesomeIcon :icon="faCoins" class="h-3.5 w-3.5" aria-hidden="true" />
        {{ modelValue }}
        <span class="text-xs" aria-hidden="true">&#9662;</span>
      </button>
    </template>
    <template v-else #label>{{ modelValue }} · {{ currencyName(modelValue, intlLocale) }}</template>
  </SearchSelect>
</template>
