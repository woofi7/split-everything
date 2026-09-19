<script setup lang="ts">
import { ref } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faChevronRight } from '@fortawesome/free-solid-svg-icons'

const props = withDefaults(
  defineProps<{
    title: string
    count?: number | string | null
    open?: boolean
    testid?: string
  }>(),
  { count: null, open: false, testid: undefined },
)

const isOpen = ref(props.open)
</script>
<template>
  <section class="surface-card mb-4">
    <button
      type="button"
      :data-testid="props.testid ? `${props.testid}-toggle` : 'section-toggle'"
      class="flex w-full items-center gap-2 p-4 text-left"
      :aria-expanded="isOpen"
      @click="isOpen = !isOpen"
    >
      <FontAwesomeIcon
        :icon="faChevronRight"
        class="h-3 w-3 shrink-0 text-[var(--text-muted)] transition-transform"
        :class="isOpen ? 'rotate-90' : ''"
        aria-hidden="true"
      />
      <span class="min-w-0 flex-1 truncate text-sm font-medium text-[var(--text-muted)]">
        {{ title }}
      </span>
      <span
        v-if="count !== null && count !== ''"
        data-testid="section-count"
        class="shrink-0 text-xs text-[var(--text-muted)]"
      >{{ count }}
      </span>
    </button>
    <div v-if="isOpen" :data-testid="props.testid" class="px-4 pb-4">
      <slot />
    </div>
  </section>
</template>
