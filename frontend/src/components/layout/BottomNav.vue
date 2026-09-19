<script setup lang="ts">
import { t } from '@/i18n'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { navTabs as tabs } from './navTabs'

const route = useRoute()
const currentName = computed(() => String(route?.name ?? ''))
const isActive = (tab: { owns: string[] }) => tab.owns.includes(currentName.value)
</script>
<template>
  <nav
    class="relative z-30 shrink-0 overflow-visible border-t bg-[var(--surface-raised)] pb-[env(safe-area-inset-bottom)] lg:hidden"
    style="border-color: var(--border)"
    :aria-label="t('Main')"
  >
    <ul class="mx-auto grid max-w-2xl grid-cols-5 items-end">
      <li v-for="tab in tabs.slice(0, 2)" :key="tab.name" class="contents">
        <RouterLink
          :to="tab.to"
          :data-tab="tab.name"
          class="nav-tab tap-target flex flex-col items-center gap-1 py-2 text-xs text-[var(--text-muted)]"
          :class="isActive(tab) ? 'nav-tab-active text-accent' : ''"
        >
          <span data-testid="tab-icon" class="nav-tab-icon">
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.8"
              aria-hidden="true"
            >
              <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </span>
          {{ tab.label }}
        </RouterLink>
      </li>
      <li class="flex justify-center">
        <RouterLink
          :to="{ name: 'add-expense' }"
          class="tap-target -mt-8 flex h-16 w-16 items-center justify-center rounded-full bg-brand-600 text-white shadow-lg shadow-brand-600/40 transition-transform active:scale-95"
          :aria-label="t('Add an expense')"
        >
          <svg class="h-7 w-7" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" aria-hidden="true">
            <path d="M12 5v14M5 12h14" stroke-linecap="round" />
          </svg>
        </RouterLink>
      </li>
      <li v-for="tab in tabs.slice(2)" :key="tab.name" class="contents">
        <RouterLink
          :to="tab.to"
          :data-tab="tab.name"
          class="nav-tab tap-target flex flex-col items-center gap-1 py-2 text-xs text-[var(--text-muted)]"
          :class="isActive(tab) ? 'nav-tab-active text-accent' : ''"
        >
          <span data-testid="tab-icon" class="nav-tab-icon">
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.8"
              aria-hidden="true"
            >
              <path :d="tab.icon" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </span>
          {{ tab.label }}
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>
<style scoped>
.nav-tab-icon {
  display: flex;
  height: 1.25rem;
  width: 1.25rem;
  align-items: center;
  justify-content: center;
  border-radius: 9999px;
  transition:
    height 150ms ease,
    width 150ms ease,
    margin 150ms ease,
    background-color 150ms ease;
}

.nav-tab-active .nav-tab-icon {
  height: 3rem;
  width: 3rem;
  margin-top: -1.75rem;
  background-color: var(--color-brand-600);
  color: #fff;
  box-shadow: 0 10px 15px -3px color-mix(in srgb, var(--color-brand-600) 30%, transparent);
}
</style>
