<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue'

/**
 * A thin bar across the top while a screen is on its way.
 *
 * Screens are loaded on demand and a guard may have to bring a session back
 * first, so a tap can take a moment on a phone. With nothing on screen to say so,
 * the tap reads as ignored and people tap again.
 *
 * Held back for a beat on purpose: most navigations are instant, and a bar that
 * flashes on every tap is noise. It only appears once a navigation has taken long
 * enough to be worth mentioning.
 */
const props = withDefaults(defineProps<{ active: boolean; delayMs?: number }>(), {
  delayMs: 150,
})

const visible = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

watch(
  () => props.active,
  (active) => {
    clearTimeout(timer)

    if (!active) {
      visible.value = false
      return
    }

    timer = setTimeout(() => {
      visible.value = true
    }, props.delayMs)
  },
  { immediate: true },
)

onUnmounted(() => clearTimeout(timer))
</script>

<template>
  <div
    v-if="visible"
    class="nav-progress"
    data-testid="navigation-progress"
    role="status"
    aria-label="Loading"
  >
    <div class="nav-progress-bar" />
  </div>
</template>

<style scoped>
.nav-progress {
  position: fixed;
  top: 0;
  left: 0;
  z-index: 50;
  width: 100%;
  height: 2px;
  overflow: hidden;
  background: color-mix(in oklab, var(--color-brand-600) 20%, transparent);
}

/*
  Indeterminate, because there is no progress to report: the wait is a request
  that has not answered yet. It sweeps rather than fills, so it never implies a
  position it does not know.
*/
.nav-progress-bar {
  width: 40%;
  height: 100%;
  border-radius: 999px;
  background: linear-gradient(
    90deg,
    transparent,
    var(--color-brand-400),
    var(--color-brand-600),
    transparent
  );
  animation: nav-progress-sweep 1.1s ease-in-out infinite;
}

@keyframes nav-progress-sweep {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: translateX(350%);
  }
}

@media (prefers-reduced-motion: reduce) {
  .nav-progress-bar {
    width: 100%;
    animation: none;
    background: var(--color-brand-600);
    opacity: 0.7;
  }
}
</style>
