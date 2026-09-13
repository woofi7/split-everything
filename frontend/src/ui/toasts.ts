import { ref } from 'vue'

/**
 * What the app has to say, said in one place.
 *
 * Every screen used to keep its own line of red text somewhere down the page: a
 * paragraph under a form, another beside a list, a third at the foot of a settings
 * screen. Which means a failure announced itself wherever that screen happened to
 * put it - often below the fold, or under the thumb that had just pressed the
 * button, and always in a place the eye had no reason to be. "Could not save" is
 * news, and news belongs where the reader is looking.
 *
 * So it comes here instead, and one stack at the top of the screen shows it. A
 * plain module rather than a store, because the things that need to report are not
 * all components: the sync engine, the api client and the stores have as much to
 * say as any screen, and none of them should have to reach for pinia to say it.
 */

export type ToastKind = 'error' | 'done' | 'note'

export interface Toast {
  id: number
  kind: ToastKind
  text: string
}

/**
 * How long each kind stays.
 *
 * An error outlives the others by a good margin: it is the one the reader has to
 * act on, it often arrives while they are looking at the button they just pressed,
 * and a message that has gone by the time the eye reaches it is no message at all.
 */
const HOLD: Record<ToastKind, number> = {
  error: 8000,
  done: 3500,
  note: 5000,
}

/** Three at once. A screenful of stacked notices is a wall, not a message. */
const MOST = 3

export const toasts = ref<Toast[]>([])

const timers = new Map<number, ReturnType<typeof setTimeout>>()
let nextId = 1

/**
 * Says something, and returns the id in case the caller wants it gone early.
 *
 * The same thing said twice in a row does not stack: a failing action retried
 * three times is one problem, and three identical cards say no more than one. The
 * repeat restarts the clock instead, so the message stays while it is still true.
 */
export function notify(text: string, kind: ToastKind = 'note'): number {
  const message = text.trim()
  if (!message) return 0

  const last = toasts.value.at(-1)
  if (last && last.kind === kind && last.text === message) {
    hold(last.id, kind)
    return last.id
  }

  const toast: Toast = { id: nextId++, kind, text: message }
  toasts.value = [...toasts.value, toast].slice(-MOST)

  // Anything pushed off the end has had its moment; its timer would fire against
  // a card that is no longer there.
  for (const id of [...timers.keys()]) {
    if (!toasts.value.some((candidate) => candidate.id === id)) forget(id)
  }

  hold(toast.id, kind)
  return toast.id
}

/**
 * Reports a failure, in the words the failure came with.
 *
 * The server's message is the useful one - "That expense is not on this device",
 * "This group is archived" - and the fallback is for the times there is nothing to
 * quote: a network that dropped, a promise that rejected with a string.
 */
export function report(caught: unknown, fallback: string): number {
  const message = caught instanceof Error ? caught.message.trim() : ''
  return notify(message || fallback, 'error')
}

export function dismiss(id: number): void {
  forget(id)
  toasts.value = toasts.value.filter((toast) => toast.id !== id)
}

/** Everything gone at once: leaving a screen, or starting a test. */
export function clearToasts(): void {
  for (const id of [...timers.keys()]) forget(id)
  toasts.value = []
}

function hold(id: number, kind: ToastKind): void {
  forget(id)
  timers.set(
    id,
    setTimeout(() => dismiss(id), HOLD[kind]),
  )
}

function forget(id: number): void {
  const timer = timers.get(id)
  if (timer !== undefined) clearTimeout(timer)
  timers.delete(id)
}
