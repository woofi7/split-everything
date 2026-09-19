<script setup lang="ts">
import { t } from '@/i18n'
import { dismiss, toasts, type Toast } from '@/ui/toasts'

const edgeOf = (toast: Toast) =>
  toast.kind === 'error'
    ? 'var(--color-owing)'
    : toast.kind === 'done'
      ? 'var(--color-owed)'
      : 'var(--accent-text)'
</script>
<template>
  <Teleport to="body">
    <div
      class="pointer-events-none fixed inset-x-0 z-50 mx-auto flex max-w-md flex-col gap-2 px-4"
      style="top: calc(0.75rem + env(safe-area-inset-top, 0px))"
      data-testid="toasts"
    >
      <TransitionGroup name="toast">
        <button
          v-for="toast in toasts"
          :key="toast.id"
          type="button"
          data-testid="toast"
          :data-toast-kind="toast.kind"
          class="toast pointer-events-auto flex w-full items-start gap-3 rounded-xl border border-l-4 p-3 text-left text-sm shadow-lg"
          style="background: var(--surface-raised); border-color: var(--border)"
          :style="{ borderLeftColor: edgeOf(toast) }"
          :role="toast.kind === 'error' ? 'alert' : 'status'"
          :aria-live="toast.kind === 'error' ? 'assertive' : 'polite'"
          :aria-label="t('Dismiss')"
          @click="dismiss(toast.id)"
        >
          <span class="min-w-0 flex-1">{{ toast.text }}</span>
          <span class="shrink-0 text-[var(--text-muted)]" aria-hidden="true">x</span>
        </button>
      </TransitionGroup>
    </div>
  </Teleport>
</template>
<style scoped>
.toast {
  backdrop-filter: none;
}

.toast-enter-active,
.toast-leave-active {
  transition: opacity 150ms ease, transform 150ms ease;
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-0.5rem);
}

@media (prefers-reduced-motion: reduce) {
  .toast-enter-active,
  .toast-leave-active {
    transition: none;
  }

  .toast-enter-from,
  .toast-leave-to {
    opacity: 1;
    transform: none;
  }
}
</style>
