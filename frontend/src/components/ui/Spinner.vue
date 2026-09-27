<script setup lang="ts">
withDefaults(defineProps<{ size?: 'sm' | 'md' }>(), { size: 'sm' })
</script>
<template>
  <svg
    data-testid="spinner"
    class="shrink-0"
    :class="size === 'md' ? 'h-5 w-5' : 'h-4 w-4'"
    viewBox="0 0 64 64"
    role="img"
    aria-hidden="true"
  >
    <rect width="64" height="64" rx="14" fill="#4f46e5" />
    <g class="spin" fill="none" stroke-width="9" stroke-linecap="round">
      <path d="M 33.04 15.03 A 17 17 0 0 1 40.16 46.91" stroke="#ffffff" />
      <path d="M 38.29 47.80 A 17 17 0 0 1 15.30 35.16" stroke="#c7d2fe" />
      <path d="M 15.04 33.10 A 17 17 0 0 1 30.96 15.03" stroke="#34d399" />
    </g>
  </svg>
</template>
<style scoped>
.spin {
  transform-origin: 32px 32px;
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

@media (prefers-reduced-motion: reduce) {
  .spin {
    animation-duration: 2.4s;
  }
}
</style>
