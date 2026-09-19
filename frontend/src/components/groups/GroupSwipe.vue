<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { readSwipe } from '@/domain/swipe'
import { resolveIcon } from '@/domain/icons'
import { groupColor } from '@/domain/themes'
import type { LocalGroup } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'

const groups = useGroupsStore()

const DECIDE_PX = 6

const COMMIT_MS = 220

const RETURN_MS = 180

const PAINT_MS = 32

const SETTLE_MS = 140

const peek = ref<{ group: LocalGroup; side: 1 | -1; at: number; of: number } | null>(null)

const offset = ref(0)

const glide = ref(0)

const faded = ref(false)

const panel = ref<HTMLElement | null>(null)

type Phase = 'idle' | 'tracking' | 'left-alone' | 'dragging' | 'settling'
let phase: Phase = 'idle'

let origin: { x: number; y: number; at: number } | null = null
let step: 1 | -1 = 1

let quiet = false
let page: HTMLElement | null = null
const timers: number[] = []

function onStart(event: TouchEvent): void {
  if (phase === 'dragging' || phase === 'settling') return

  const touch = event.touches[0]

  if (event.touches.length !== 1 || !touch || overlaid(event.target)) return

  origin = { x: touch.clientX, y: touch.clientY, at: Date.now() }
  quiet = motionless()
  phase = 'tracking'
}

function onMove(event: TouchEvent): void {
  if (phase !== 'tracking' && phase !== 'dragging') return

  const touch = event.touches[0]

  if (event.touches.length > 1 || !origin || !touch) {
    abandon()
    return
  }

  const dx = touch.clientX - origin.x
  const dy = touch.clientY - origin.y

  if (phase === 'tracking') {
    if (Math.max(Math.abs(dx), Math.abs(dy)) < DECIDE_PX) return

    if (Math.abs(dy) >= Math.abs(dx)) {
      giveUp()
      return
    }

    if (!begin(dx < 0 ? 1 : -1)) {
      giveUp()
      return
    }

    phase = 'dragging'
  }

  const wanted = dx < 0 ? 1 : -1
  if (wanted !== step && !begin(wanted)) {
    abandon()
    return
  }

  if (event.cancelable) event.preventDefault()

  if (quiet) return

  glide.value = 0
  offset.value = step * width() + dx
  movePage(dx, 0)
}

function onEnd(event: TouchEvent): void {
  const began = origin
  const dragging = phase === 'dragging'
  origin = null

  const touch = event.changedTouches[0]

  if (!began || !touch) {
    abandon()
    return
  }

  const dx = touch.clientX - began.x
  const direction = readSwipe({
    dx,
    dy: touch.clientY - began.y,
    elapsedMs: Date.now() - began.at,
  })

  if (!dragging) {
    phase = 'idle'
    if (!direction) return
    if (begin(direction === 'left' ? 1 : -1)) commit()
    return
  }

  if (direction && (direction === 'left' ? 1 : -1) === step) commit()
  else if (Math.abs(dx) > width() / 2) commit()
  else back()
}

function begin(direction: 1 | -1): boolean {
  const group = groups.groupInCycle(direction)
  if (!group) return false

  step = direction
  if (quiet) return true

  const order = groups.visibleGroups
  glide.value = 0
  offset.value = direction * width()
  peek.value = {
    group,
    side: direction,
    at: order.findIndex((candidate) => candidate.id === group.id) + 1,
    of: order.length,
  }

  return true
}

function commit(): void {
  phase = 'settling'

  if (quiet) {
    groups.cycleMainGroup(step)
    toTop()
    finish()
    return
  }

  void panel.value?.offsetWidth

  glide.value = COMMIT_MS
  offset.value = 0
  movePage(-step * width(), COMMIT_MS)

  after(COMMIT_MS, () => {
    groups.cycleMainGroup(step)
    movePage(0, 0)

    after(PAINT_MS, () => {
      toTop()

      faded.value = true
      after(SETTLE_MS, finish)
    })
  })
}

function back(): void {
  phase = 'settling'

  if (quiet) {
    finish()
    return
  }

  glide.value = RETURN_MS
  offset.value = step * width()
  movePage(0, RETURN_MS)

  after(RETURN_MS, finish)
}

function finish(): void {
  clearTimers()
  peek.value = null
  offset.value = 0
  glide.value = 0
  faded.value = false
  releasePage()
  phase = 'idle'
  origin = null
}

function giveUp(): void {
  phase = 'left-alone'
}

function abandon(): void {
  if (phase === 'settling') return

  origin = null
  if (phase === 'dragging') {
    back()
    return
  }

  phase = 'idle'
}

function movePage(x: number, ms: number): void {
  page ??= document.querySelector<HTMLElement>('[data-app-page]')
  if (!page) return

  page.style.transition = ms > 0 ? `transform ${ms}ms cubic-bezier(0.22, 0.61, 0.36, 1)` : 'none'
  page.style.transform = `translateX(${x}px)`
  page.style.willChange = 'transform'

  page.style.overflowAnchor = 'none'
}

function toTop(): void {
  page ??= document.querySelector<HTMLElement>('[data-app-page]')
  if (page && page.scrollTop > 0) page.scrollTop = 0
}

function releasePage(): void {
  if (!page) return

  page.style.transition = ''
  page.style.transform = ''
  page.style.willChange = ''
  page.style.overflowAnchor = ''
  page = null
}

function after(ms: number, run: () => void): void {
  timers.push(window.setTimeout(run, ms))
}

function clearTimers(): void {
  for (const timer of timers) window.clearTimeout(timer)
  timers.length = 0
}

function overlaid(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="dialog"]') !== null
}

const width = () => window.innerWidth || 360

function motionless(): boolean {
  return (
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  )
}

onMounted(() => {
  window.addEventListener('touchstart', onStart, { passive: true })
  window.addEventListener('touchmove', onMove, { passive: false })
  window.addEventListener('touchend', onEnd, { passive: true })
  window.addEventListener('touchcancel', abandon, { passive: true })
})

onUnmounted(() => {
  window.removeEventListener('touchstart', onStart)
  window.removeEventListener('touchmove', onMove)
  window.removeEventListener('touchend', onEnd)
  window.removeEventListener('touchcancel', abandon)
  clearTimers()
  releasePage()
})
</script>
<template>
  <Teleport to="body">
    <div
      v-if="peek"
      ref="panel"
      data-testid="swipe-peek"
      class="fixed inset-0 z-20 overflow-hidden"
      :class="faded ? 'opacity-0' : 'opacity-100'"
      :style="{
        transform: `translateX(${offset}px)`,
        transition: [
          glide > 0 ? `transform ${glide}ms cubic-bezier(0.22, 0.61, 0.36, 1)` : '',
          faded ? `opacity ${SETTLE_MS}ms ease-out` : '',
        ]
          .filter(Boolean)
          .join(', '),
        background: 'var(--surface)',
        boxShadow: peek.side === 1
          ? '-16px 0 30px rgba(0, 0, 0, 0.35)'
          : '16px 0 30px rgba(0, 0, 0, 0.35)',
      }"
      aria-hidden="true"
    >
      <div
        class="mx-auto flex h-full max-w-2xl flex-col px-4 pt-[max(1rem,env(safe-area-inset-top))] pb-10"
      >
        <div class="flex min-w-0 items-start gap-3">
          <span
            class="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-white"
            :style="{ backgroundColor: groupColor(peek.group) }"
          >
            <FontAwesomeIcon
              :icon="resolveIcon(peek.group.iconName).definition"
              class="h-4 w-4"
            />
          </span>
          <div class="min-w-0">
            <p class="truncate text-xl font-semibold">{{ peek.group.name }}</p>
            <p class="truncate text-sm text-[var(--text-muted)]">
              {{ peek.group.memberCount || peek.group.members.length }} people
            </p>
          </div>
        </div>
        <div class="flex flex-1 flex-col items-center justify-center gap-3">
          <MoneyAmount
            :amount="peek.group.myNetBalance"
            :currency="peek.group.baseCurrency"
            signed
          />
          <p data-testid="peek-position" class="text-xs text-[var(--text-muted)]">
            {{ peek.at }} of {{ peek.of }}
          </p>
          <ul v-if="peek.of <= 8" class="flex items-center gap-1.5">
            <li
              v-for="index in peek.of"
              :key="index"
              class="h-1.5 w-1.5 rounded-full"
              :style="{
                background: index === peek.at ? 'var(--text)' : 'var(--border)',
              }"
            />
          </ul>
        </div>
      </div>
    </div>
  </Teleport>
</template>
