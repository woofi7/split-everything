<script setup lang="ts">
/**
 * Something is happening and it is not instant.
 *
 * A ring rather than a bar: nothing here knows how far along it is. Imports of four
 * hundred rows take seconds, and a screen that says nothing for seconds reads as a
 * tap that did not land.
 */
withDefaults(defineProps<{ size?: 'sm' | 'md' }>(), { size: 'sm' })
</script>

<template>
  <svg
    data-testid="spinner"
    class="spin shrink-0 text-brand-400"
    :class="size === 'md' ? 'h-5 w-5' : 'h-4 w-4'"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2.4"
    role="img"
    aria-hidden="true"
  >
    <circle cx="12" cy="12" r="9" class="opacity-25" />
    <path d="M21 12a9 9 0 0 0-9-9" stroke-linecap="round" />
  </svg>
</template>

<style scoped>
.spin {
  animation: spin 900ms linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

/* Still turning, slowly: a still spinner says stuck rather than working. */
@media (prefers-reduced-motion: reduce) {
  .spin {
    animation-duration: 2.4s;
  }
}
</style>
