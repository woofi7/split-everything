<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

/**
 * Pull down at the top of a screen to sync.
 *
 * The app syncs on its own - when it starts, when it comes back online, when a
 * change arrives - but there was no way to ask. Watching a stale figure and
 * wondering whether the thing is doing anything is a bad place to leave somebody,
 * and every phone has taught the same gesture for exactly this question.
 *
 * The page is what scrolls here, not the window, so this watches the page: a pull
 * only counts when it is already at the top, which is what keeps it from firing
 * halfway down a list. The horizontal swipe that changes group decides its axis on
 * the first few pixels and gives up on anything vertical, so the two never both
 * claim a gesture.
 *
 * Renders the indicator only. What refreshing means belongs to the screen, which
 * says so by handling the event.
 */

const emit = defineEmits<{ refresh: [] }>()

/** How far the finger travels before letting go actually refreshes. */
const THRESHOLD_PX = 64

/** How far the page can follow it, however hard it is pulled. */
const MAX_PULL_PX = 96

/**
 * How much of the finger's movement the page takes.
 *
 * Half, so the pull feels like it is against something. A page that tracks a finger
 * exactly reads as dragged rather than stretched, and there is nothing underneath
 * to drag it to.
 */
const RESISTANCE = 0.5

/** When a drag is one: past the slop, and more down than across. */
const DECIDE_PX = 8

/** How long the page takes to settle back. */
const SETTLE_MS = 220

const distance = ref(0)
const isRefreshing = ref(false)
const isPulling = ref(false)

type Phase = 'idle' | 'tracking' | 'pulling' | 'refreshing'
let phase: Phase = 'idle'
let origin: { x: number; y: number } | null = null
let page: HTMLElement | null = null

/**
 * How far the arrow has turned, as a promise about what letting go will do.
 *
 * It finishes its turn exactly when the pull becomes far enough, so pointing up is
 * not decoration: it means release now and this will sync.
 */
const turned = computed(() => Math.min(distance.value / (THRESHOLD_PX * RESISTANCE), 1) * 180)

function pageElement(): HTMLElement | null {
  page ??= document.querySelector<HTMLElement>('[data-app-page]')
  return page
}

function onStart(event: TouchEvent): void {
  if (phase !== 'idle') return

  const touch = event.touches[0]
  if (event.touches.length !== 1 || !touch || overlaid(event.target)) return

  // Only from the very top. Anywhere else the gesture belongs to the list.
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

    // Up, or mostly sideways: not this gesture. Given up on rather than watched,
    // so a finger that wanders back down cannot start a pull mid-scroll.
    if (dy <= 0 || Math.abs(dx) >= dy) {
      give()
      return
    }

    phase = 'pulling'
    isPulling.value = true
  }

  /*
   * The scroller is at the top and has nowhere to go, so without this the browser
   * spends the gesture on its own overscroll bounce and the page never moves.
   */
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

  /*
   * Held at the threshold while it works, which is what makes the spinner read as
   * "doing it" rather than "done". Released by the screen finishing, not by a timer.
   */
  phase = 'refreshing'
  isPulling.value = false
  isRefreshing.value = true
  distance.value = THRESHOLD_PX * RESISTANCE
  hold(distance.value, SETTLE_MS)

  emit('refresh')
}

/** Called by the screen when its refresh is over, whichever way it went. */
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

/** Drops a gesture that turned out to be a scroll, with nothing to undo. */
function give(): void {
  if (phase === 'refreshing') return

  origin = null
  if (phase === 'pulling') {
    spring()
    return
  }

  phase = 'idle'
}

/**
 * Moves the page itself, written straight onto the element: it belongs to the shell
 * around this component, and a transform per frame through a binding is a re-render
 * per frame for one style.
 */
function hold(offset: number, ms: number): void {
  const scroller = pageElement()
  if (!scroller) return

  scroller.style.transition = ms > 0 ? `transform ${ms}ms ease-out` : 'none'
  scroller.style.transform = offset > 0 ? `translateY(${offset}px)` : 'translateY(0px)'
}

/** Hands the page back as it was found: a lasting transform changes what is fixed. */
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
  <!--
    Over the top of the page, where the pull comes from. Teleported so it is not
    inside the thing being moved: an indicator that travels with the page would sit
    still relative to it and never appear to arrive.
  -->
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
        <!--
          A ring while it works, an arrow while it is being pulled.

          The arrow used to stay and spin, which read as an arrow pointing every
          which way rather than as waiting: an arrow means a direction, and a whole
          turn of one means nothing. Its half turn is the promise ("let go and this
          syncs"); once it has been let go the question is only whether it is done,
          which is what a ring going round says.
        -->
        <svg
          v-if="isRefreshing"
          data-testid="pull-spinner"
          class="pull-spin h-4 w-4 text-brand-400"
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
          class="h-4 w-4 text-brand-400"
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
