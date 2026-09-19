<script setup lang="ts">
import { t } from '@/i18n'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { navTabs } from './navTabs'

const route = useRoute()
const currentName = computed(() => String(route?.name ?? ''))
const isActive = (tab: { owns: string[] }) => tab.owns.includes(currentName.value)
</script>

<template>
  <nav
    data-testid="side-nav"
    class="hidden shrink-0 flex-col gap-1 border-r px-3 py-4 lg:flex lg:w-56"
    style="border-color: var(--border); background: var(--surface-raised)"
    :aria-label="t('Main')"
  >
    <RouterLink
      :to="{ name: 'dashboard' }"
      class="mb-4 flex items-center gap-2 px-2"
    >
      <img src="/icons/icon.svg" alt="" width="28" height="28" class="h-7 w-7 rounded-lg" />
      <span class="truncate text-sm font-semibold">{{ t('Split Everything') }}</span>
    </RouterLink>

    <RouterLink
      :to="{ name: 'add-expense' }"
      data-testid="side-add"
      class="btn btn-press btn-primary mb-3 w-full"
    >
      <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" aria-hidden="true">
        <path d="M12 5v14M5 12h14" stroke-linecap="round" />
      </svg>
      {{ t('Add an expense') }}
    </RouterLink>

    <RouterLink
      v-for="tab in navTabs"
      :key="tab.name"
      :to="tab.to"
      :data-side-tab="tab.name"
      class="tap-target flex items-center gap-3 rounded-lg px-3 text-sm"
      :class="isActive(tab)
        ? 'bg-brand-600/15 font-medium text-accent'
        : 'text-[var(--text-muted)]'"
      :aria-current="isActive(tab) ? 'page' : undefined"
    >
      <svg
        class="h-5 w-5 shrink-0"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.8"
        aria-hidden="true"
      >
        <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
      <span class="truncate">{{ tab.label }}</span>
    </RouterLink>
  </nav>
</template>
