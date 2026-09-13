<script setup lang="ts">
import { t } from '@/i18n'
import { dismiss, toasts, type Toast } from '@/ui/toasts'

/**
 * Where the app says things.
 *
 * At the top, over everything, because that is where a reader who has just pressed
 * a button is not looking - and so the only place a message can put itself in
 * front of them. Above the header rather than inside it: the header belongs to the
 * screen, and this belongs to the app.
 *
 * Teleported to the body so no screen's overflow, transform or stacking context can
 * clip it, which is exactly what happened to the inline messages this replaces.
 */

/** The edge that says what kind of news this is, without relying on the colour. */
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
  /* Two backgrounds would fight on a translucent surface; this one is opaque. */
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

/* A message that slides is still a message; someone who has asked for less motion
   gets it whole and still. */
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
