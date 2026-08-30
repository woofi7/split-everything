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

/**
 * How far a finger travels before this decides what the gesture is.
 *
 * Small, and read on the first report that crosses it whichever way it went,
 * rather than waiting for enough movement to pile up sideways.
 *
 * A browser will not scroll while a listener that can cancel the move has yet to
 * answer. But the moment one move is allowed through it starts scrolling, and it
 * keeps scrolling for the rest of that gesture: cancelling a later move does not
 * take it back. So the choice between swiping and scrolling has to be made before
 * the browser makes it.
 *
 * Six pixels because a phone waits for about eight of its own before it starts
 * scrolling, so a swipe that says so first always beats it to the gesture.
 *
 * That is what was wrong with a thumb sweeping from the left: a thumb pivots at
 * the base of the hand, so the sweep arcs, and the arc was being scrolled. The
 * page ran under the finger, and a phone answers that by sliding its toolbar
 * away, which drops everything fixed to the bottom of the window - the tab bar
 * included. A sweep steep enough that even six pixels in it still looks like a
 * scroll is left alone here and picked up on release instead.
 */
const DECIDE_PX = 6

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

type Phase = 'idle' | 'tracking' | 'left-alone' | 'dragging' | 'settling'
let phase: Phase = 'idle'

let origin: { x: number; y: number; at: number } | null = null
let step: 1 | -1 = 1

/** Whether this gesture is being handled without any sliding about. */
let quiet = false
let page: HTMLElement | null = null
const timers: number[] = []

function onStart(event: TouchEvent): void {
  // A gesture that is landing owns the screen until it has. One that was being
  // watched or left alone is simply over, whether or not its end was reported.
  if (phase === 'dragging' || phase === 'settling') return

  const touch = event.touches[0]

  // A second finger is a pinch, and a sheet over the page is its own world: the
  // picker and the icon chooser are full of things to drag past.
  if (event.touches.length !== 1 || !touch || overlaid(event.target)) return

  origin = { x: touch.clientX, y: touch.clientY, at: Date.now() }
  quiet = motionless()
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
    // Too little to read. Nothing is claimed and nothing is cancelled, so a
    // scroll starting here is the browser's to run.
    if (Math.max(Math.abs(dx), Math.abs(dy)) < DECIDE_PX) return

    // Down at least as much as across is a scroll, and it is given up on rather
    // than merely ignored: a finger that wanders back across mid-scroll should
    // not still be able to turn a page.
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

  // Dragged back past where it started: it is the other group coming in now.
  const wanted = dx < 0 ? 1 : -1
  if (wanted !== step && !begin(wanted)) {
    abandon()
    return
  }

  /*
   * The one thing here that is not passive, and the reason the decision above has
   * to be quick: this is what stops the page scrolling while a finger is dragging
   * it sideways.
   */
  if (event.cancelable) event.preventDefault()

  // Someone who asked for less motion has the gesture taken all the same, so the
  // page cannot run about under them either. There is just nothing to watch.
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

  /*
   * A gesture nobody dragged can still have been a swipe: a fast flick can end
   * before a single move arrives, and a sweep that started too steeply to claim
   * was left to the browser rather than fought over. Read whole, from where the
   * finger landed to where it left, it is either a swipe or it is not - and by
   * now there is nothing to fight about, so the group changes and the incoming
   * page slides in from the edge instead of from under the finger.
   */
  if (!dragging) {
    phase = 'idle'
    if (!direction) return
    if (begin(direction === 'left' ? 1 : -1)) commit()
    return
  }

  // Committed on the way it went rather than on how far, so a short flick counts,
  // and put back when it was neither.
  if (direction && (direction === 'left' ? 1 : -1) === step) commit()
  else if (Math.abs(dx) > width() / 2) commit()
  else back()
}

/** Takes up the group that is coming in, and answers whether there is one. */
function begin(direction: 1 | -1): boolean {
  const group = groups.groupInCycle(direction)
  if (!group) return false

  step = direction
  if (quiet) return true

  const order = groups.visibleGroups
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

  // Nothing to slide, so nothing to wait for: the group changes and that is the
  // whole of it.
  if (quiet) {
    groups.cycleMainGroup(step)
    if (window.scrollY > 0) window.scrollTo(0, 0)
    finish()
    return
  }

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
      /*
       * The new group's screen starts at the top, like any other page arrived at.
       * Left alone it starts wherever the old one was being read, which is not a
       * place in this group at all, and a phone answers a scroll position that
       * jumps by a few hundred pixels by sliding its own toolbar about, taking
       * the tab bar with it. Done under the stand-in, so nothing of it shows.
       */
      if (window.scrollY > 0) window.scrollTo(0, 0)

      faded.value = true
      after(SETTLE_MS, finish)
    })
  })
}

/** Puts a gesture that changed its mind back where it started. */
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

/**
 * Leaves the gesture to the browser, while still watching where it ends up.
 *
 * Not the same as dropping it. Once a move has been allowed through, the browser
 * scrolls for the rest of the gesture whatever anyone says afterwards, so there
 * is no taking it back - but there is no harm in reading the whole thing when the
 * finger leaves, and a sweep too steep to claim at six pixels is usually obvious
 * by the end.
 */
function giveUp(): void {
  phase = 'left-alone'
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

  /*
   * And no anchoring while this is going on. A browser holding the scroll on
   * whatever was in view is right when something loads in above it, and wrong
   * when the whole screen is replaced by another group's: it hunts for the old
   * anchor in new content and lands hundreds of pixels down. Only for the length
   * of the swipe, so reading a list while an expense arrives still holds still.
   */
  page.style.overflowAnchor = 'none'
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
