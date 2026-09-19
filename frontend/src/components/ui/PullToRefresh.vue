<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

const emit = defineEmits<{ refresh: [] }>()

const THRESHOLD_PX = 64

const MAX_PULL_PX = 96

const RESISTANCE = 0.5

const DECIDE_PX = 8

const SETTLE_MS = 220

const distance = ref(0)
const isRefreshing = ref(false)
const isPulling = ref(false)

type Phase = 'idle' | 'tracking' | 'pulling' | 'refreshing'
let phase: Phase = 'idle'
let origin: { x: number; y: number } | null = null
let page: HTMLElement | null = null

const turned = computed(() => Math.min(distance.value / (THRESHOLD_PX * RESISTANCE), 1) * 180)

function pageElement(): HTMLElement | null {
  page ??= document.querySelector<HTMLElement>('[data-app-page]')
  return page
}

function onStart(event: TouchEvent): void {
  if (phase !== 'idle') return

  const touch = event.touches[0]
  if (event.touches.length !== 1 || !touch || overlaid(event.target)) return

  const scroller = pageElement()
  if (!scroller || scroller.scrollTop > 0) return

  origin = { x: touch.clientX, y: touch.clientY }
  phase = 'tracking'
}

function onMove(event: TouchEvent): void {
  if (phase !== 'tracking' && phase !== 'pulling') return

  const touch = event.touches[0]
  if (event.touches.length > 1 || !origin || !touch) {
    give()
    return
  }

  const dx = touch.clientX - origin.x
  const dy = touch.clientY - origin.y

  if (phase === 'tracking') {
    if (Math.max(Math.abs(dx), Math.abs(dy)) < DECIDE_PX) return

    if (dy <= 0 || Math.abs(dx) >= dy) {
      give()
      return
    }

    phase = 'pulling'
    isPulling.value = true
  }

  if (event.cancelable) event.preventDefault()

  distance.value = Math.min(dy * RESISTANCE, MAX_PULL_PX)
  hold(distance.value, 0)
}

function onEnd(): void {
  if (phase !== 'pulling') {
    give()
    return
  }

  if (distance.value < THRESHOLD_PX * RESISTANCE) {
    spring()
    return
  }

  phase = 'refreshing'
  isPulling.value = false
  isRefreshing.value = true
  distance.value = THRESHOLD_PX * RESISTANCE
  hold(distance.value, SETTLE_MS)

  emit('refresh')
}

function done(): void {
  if (phase !== 'refreshing') return
  spring()
}

defineExpose({ done })

function spring(): void {
  phase = 'idle'
  origin = null
  isPulling.value = false
  isRefreshing.value = false
  distance.value = 0
  hold(0, SETTLE_MS)

  window.setTimeout(release, SETTLE_MS)
}

function give(): void {
  if (phase === 'refreshing') return

  origin = null
  if (phase === 'pulling') {
    spring()
    return
  }

  phase = 'idle'
}

function hold(offset: number, ms: number): void {
  const scroller = pageElement()
  if (!scroller) return

  scroller.style.transition = ms > 0 ? `transform ${ms}ms ease-out` : 'none'
  scroller.style.transform = offset > 0 ? `translateY(${offset}px)` : 'translateY(0px)'
}

function release(): void {
  if (!page) return

  page.style.transition = ''
  page.style.transform = ''
  page = null
}

function overlaid(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="dialog"]') !== null
}

onMounted(() => {
  window.addEventListener('touchstart', onStart, { passive: true })
  window.addEventListener('touchmove', onMove, { passive: false })
  window.addEventListener('touchend', onEnd, { passive: true })
  window.addEventListener('touchcancel', give, { passive: true })
})

onUnmounted(() => {
  window.removeEventListener('touchstart', onStart)
  window.removeEventListener('touchmove', onMove)
  window.removeEventListener('touchend', onEnd)
  window.removeEventListener('touchcancel', give)
  release()
})
</script>
<template>
  <Teleport to="body">
    <div
      v-if="isPulling || isRefreshing"
      data-testid="pull-indicator"
      class="pointer-events-none fixed inset-x-0 z-30 flex justify-center"
      :style="{ top: `${Math.max(8, distance - 8)}px` }"
      role="status"
      :aria-label="isRefreshing ? 'Syncing' : 'Pull to sync'"
    >
      <span
        class="flex h-9 w-9 items-center justify-center rounded-full border shadow-lg"
        style="background: var(--surface-raised); border-color: var(--border)"
      >
        <svg
          v-if="isRefreshing"
          data-testid="pull-spinner"
          class="pull-spin h-4 w-4 text-accent"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2.4"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="9" class="opacity-25" />
          <path d="M21 12a9 9 0 0 0-9-9" stroke-linecap="round" />
        </svg>
        <svg
          v-else
          data-testid="pull-arrow"
          class="h-4 w-4 text-accent"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2.2"
          aria-hidden="true"
          :style="{ transform: `rotate(${turned}deg)` }"
        >
          <path d="M12 5v14M6 13l6 6 6-6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </span>
    </div>
  </Teleport>
</template>
<style scoped>
.pull-spin {
  animation: pull-spin 900ms linear infinite;
}

@keyframes pull-spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

@media (prefers-reduced-motion: reduce) {
  .pull-spin {
    animation-duration: 2s;
  }
}
</style>
