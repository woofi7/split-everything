<script setup lang="ts">
import { t } from '@/i18n'
import { computed } from 'vue'
import SearchSelect from '@/components/ui/SearchSelect.vue'
import type { SelectOption } from '@/components/ui/selectOption'
import { resolveIcon } from '@/domain/icons'
import type { Category } from '@/domain/categories'

const props = withDefaults(
  defineProps<{
    modelValue: string | null
    categories: Category[]
    isGuess?: boolean
    isCreating?: boolean
  }>(),
  { isGuess: false, isCreating: false },
)

const emit = defineEmits<{
  'update:modelValue': [key: string | null]
  create: [name: string]
}>()

const options = computed<SelectOption[]>(() => [
  ...props.categories.map((category) => ({
    value: category.key,
    label: category.name,
    keywords: category.keywords ?? [],
    icon: resolveIcon(category.iconName).definition,
    iconColor: category.colorHex,
  })),
  { value: null, label: t('Not filed') },
])
</script>
<template>
  <SearchSelect
    :model-value="modelValue"
    :options="options"
    :title="t('Category')"
    testid="category"
    :disabled="isCreating"
    searchable
    creatable
    @update:model-value="emit('update:modelValue', $event)"
    @create="emit('create', $event)"
  >
    <template #label="{ chosen }">{{ isCreating ? t('Adding') : (chosen?.label ?? modelValue) }}</template>
  </SearchSelect>
</template>
