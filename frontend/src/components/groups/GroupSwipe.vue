<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { readSwipe } from '@/domain/swipe'
import { resolveIcon } from '@/domain/icons'
import type { LocalGroup } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'

/**
 * Swipe across to change group.
 *
 * The app is on one group at a time, so moving between them is the navigation it
 * does most, and it used to cost a tap on the mark and a tap in a sheet. A
 * sideways swipe is what a phone already means by "the next one of these", and
 * these screens scroll but never pan, so the gesture is free.
 *
 * The group being swiped to comes in with the finger, as a page alongside this
 * one: a gesture that only acted on release would leave the screen dead while a
 * finger dragged across it, and there would be no way to change your mind. The
 * incoming page is a stand-in rather than the real screen - it names the group,
 * says what you owe there and where it sits in the cycle - because building a
 * second dashboard for a group nobody has arrived at yet is a lot of work to
 * throw away, and this covers the switch until the real one has painted.
 *
 * Listens on the window rather than wrapping the page: a short screen is mostly
 * empty space, and a gesture that only worked over the content would look broken
 * exactly there. Drop it into any screen that follows the main group.
 *
 * Left is the next group and right the previous one, as a carousel, and the ends
 * join up so a swipe never does nothing.
 */

const groups = useGroupsStore()

/** How far the finger goes before this takes the gesture off the scroller. */
const MIN_DRAG_PX = 12

/** Sliding the rest of the way once the finger has let go. */
const COMMIT_MS = 220

/** Putting a gesture that changed its mind back where it was. */
const RETURN_MS = 180

/** A beat for the group's real screen to paint behind the stand-in. */
const PAINT_MS = 32

/** Taking the stand-in back off, once there is something behind it. */
const SETTLE_MS = 140

/** The group coming in, if a swipe is under way, and the edge it comes from. */
const peek = ref<{ group: LocalGroup; side: 1 | -1; at: number; of: number } | null>(null)

/** Where its page is, in pixels from the middle of the screen. */
const offset = ref(0)

/** Milliseconds for the page to move on its own, or none while it is dragged. */
const glide = ref(0)

/** Whether the stand-in is on its way out, having done its job. */
const faded = ref(false)

const panel = ref<HTMLElement | null>(null)

type Phase = 'idle' | 'tracking' | 'dragging' | 'settling'
let phase: Phase = 'idle'

let origin: { x: number; y: number; at: number } | null = null
let step: 1 | -1 = 1
let page: HTMLElement | null = null
const timers: number[] = []

function onStart(event: TouchEvent): void {
  // Mid-flight, the gesture that is landing owns the screen.
  if (phase !== 'idle') return

  const touch = event.touches[0]

  // A second finger is a pinch, and a sheet over the page is its own world: the
  // picker and the icon chooser are full of things to drag past.
  if (event.touches.length !== 1 || !touch || overlaid(event.target)) return

  origin = { x: touch.clientX, y: touch.clientY, at: Date.now() }
  phase = 'tracking'
}

function onMove(event: TouchEvent): void {
  if (phase !== 'tracking' && phase !== 'dragging') return

  const touch = event.touches[0]

  // A gesture that becomes a pinch part way through is not a swipe either.
  if (event.touches.length > 1 || !origin || !touch) {
    abandon()
    return
  }

  const dx = touch.clientX - origin.x
  const dy = touch.clientY - origin.y

  if (phase === 'tracking') {
    // Given up on rather than merely ignored once it is clearly a scroll, so a
    // finger that wanders back across mid-scroll cannot still turn a page.
    if (Math.abs(dy) > MIN_DRAG_PX && Math.abs(dy) >= Math.abs(dx)) {
      abandon()
      return
    }

    if (Math.abs(dx) < MIN_DRAG_PX || Math.abs(dx) <= Math.abs(dy)) return

    // Nothing slides for someone who has asked for less motion, so there is
    // nothing to drag either: the group changes on release instead.
    if (motionless()) return

    if (!take(dx < 0 ? 1 : -1)) {
      abandon()
      return
    }

    phase = 'dragging'
  }

  // Dragged back past where it started: it is the other group coming in now.
  const wanted = dx < 0 ? 1 : -1
  if (wanted !== step && !take(wanted)) {
    abandon()
    return
  }

  /*
   * The one thing here that is not passive. Without it the browser is free to
   * scroll the page down while the finger is dragging it sideways, and the two
   * together read as the screen coming apart.
   */
  if (event.cancelable) event.preventDefault()

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

  // A gesture nobody dragged is still a swipe, on a screen too still to have
  // reported one: a fast flick can end before a single move arrives.
  if (!dragging) {
    phase = 'idle'
    if (!direction) return

    const wanted = direction === 'left' ? 1 : -1
    if (motionless()) {
      groups.cycleMainGroup(wanted)
      return
    }

    if (take(wanted)) commit()
    return
  }

  // Committed on the way it went rather than on how far, so a short flick counts,
  // and put back when it was neither.
  if (direction && (direction === 'left' ? 1 : -1) === step) commit()
  else if (Math.abs(dx) > width() / 2) commit()
  else back()
}

/** Takes up the group that is coming in, and answers whether there is one. */
function take(direction: 1 | -1): boolean {
  const group = groups.groupInCycle(direction)
  if (!group) return false

  const order = groups.visibleGroups
  step = direction
  // Off screen to begin with, so there is somewhere for it to come in from.
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

/**
 * Sees the swipe through.
 *
 * The stand-in slides into place and the page it replaces carries on out, so the
 * two move as one. Only then does the group change, behind a cover, which is what
 * keeps a screen rebuilding itself out of sight.
 */
function commit(): void {
  phase = 'settling'

  // A frame at the position it is coming from, forced by reading the layout: a
  // page that is put where it is going in the same breath as being created has
  // nothing to animate from, and a flick that ends before a single move arrives
  // creates it right here.
  void panel.value?.offsetWidth

  glide.value = COMMIT_MS
  offset.value = 0
  movePage(-step * width(), COMMIT_MS)

  after(COMMIT_MS, () => {
    groups.cycleMainGroup(step)
    // Back in place with no transition: the page is under the stand-in, and the
    // group it is showing is now the one that just arrived.
    movePage(0, 0)

    after(PAINT_MS, () => {
      faded.value = true
      after(SETTLE_MS, finish)
    })
  })
}

/** Puts a gesture that changed its mind back where it started. */
function back(): void {
  phase = 'settling'
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

/** Drops a gesture that turned out to be something else, with nothing to undo. */
function abandon(): void {
  // A gesture that is already landing owns the screen until it has.
  if (phase === 'settling') return

  origin = null
  if (phase === 'dragging') {
    back()
    return
  }

  phase = 'idle'
}

/**
 * Moves the screen itself.
 *
 * Written straight onto the element rather than through a binding, because the
 * page belongs to the shell around this component and not to it. The transform is
 * cleared again on the way out: an element that keeps one is what everything
 * fixed inside it is positioned against.
 */
function movePage(x: number, ms: number): void {
  page ??= document.querySelector<HTMLElement>('[data-swipe-page]')
  if (!page) return

  page.style.transition = ms > 0 ? `transform ${ms}ms cubic-bezier(0.22, 0.61, 0.36, 1)` : 'none'
  page.style.transform = `translateX(${x}px)`
  page.style.willChange = 'transform'
}

function releasePage(): void {
  if (!page) return

  page.style.transition = ''
  page.style.transform = ''
  page.style.willChange = ''
  page = null
}

function after(ms: number, run: () => void): void {
  timers.push(window.setTimeout(run, ms))
}

function clearTimers(): void {
  for (const timer of timers) window.clearTimeout(timer)
  timers.length = 0
}

/** Whether something is over the page, which owns any gesture inside it. */
function overlaid(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="dialog"]') !== null
}

const width = () => window.innerWidth || 360

/** Whether the person using this has asked not to be slid about. */
function motionless(): boolean {
  return (
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  )
}

/**
 * Passive but for the move, which has to be able to stop the page scrolling
 * sideways-and-down at once. Saying so for the rest is what keeps scrolling off
 * the main thread on a phone.
 */
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
  <!--
    To the body, so it slides over the whole screen rather than over whatever
    corner of the content this component happens to sit in. Under the tab bar,
    which stays put: the page is turning, the app is not going anywhere.
  -->
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
        // A shadow on the edge it leads with, so the two pages read as two sheets
        // rather than as one long one sliding past.
        boxShadow: peek.side === 1
          ? '-16px 0 30px rgba(0, 0, 0, 0.35)'
          : '16px 0 30px rgba(0, 0, 0, 0.35)',
      }"
      aria-hidden="true"
    >
      <div
        class="mx-auto flex h-full max-w-2xl flex-col px-4 pt-[max(1rem,env(safe-area-inset-top))] pb-28"
      >
        <!-- Laid out like the header it is about to become. -->
        <div class="flex min-w-0 items-start gap-3">
          <span
            class="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-white"
            :style="{ backgroundColor: peek.group.colorHex || '#4f46e5' }"
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

        <!--
          What you would come here to know, while the group's own screen is still
          being built: what you owe, and which of the groups this is.
        -->
        <div class="flex flex-1 flex-col items-center justify-center gap-3">
          <MoneyAmount
            :amount="peek.group.myNetBalance"
            :currency="peek.group.baseCurrency"
            signed
          />

          <p data-testid="peek-position" class="text-xs text-[var(--text-muted)]">
            {{ peek.at }} of {{ peek.of }}
          </p>

          <!-- Where this group sits in the cycle, so the order becomes learnable. -->
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
