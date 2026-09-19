<script setup lang="ts">
import { t } from '@/i18n'
import { onUnmounted, ref, watch } from 'vue'

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
    :aria-label="t('Loading')"
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

.nav-progress-bar {
  width: 40%;
  height: 100%;
  border-radius: 999px;
  background: linear-gradient(
    90deg,
    transparent,
    var(--accent-text),
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
    background: var(--accent-text);
    opacity: 0.7;
  }
}
</style>
