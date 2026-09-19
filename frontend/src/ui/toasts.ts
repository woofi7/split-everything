import { ref } from 'vue'

export type ToastKind = 'error' | 'done' | 'note'

export interface Toast {
  id: number
  kind: ToastKind
  text: string
}

const HOLD: Record<ToastKind, number> = {
  error: 8000,
  done: 3500,
  note: 5000,
}

const MOST = 3

export const toasts = ref<Toast[]>([])

const timers = new Map<number, ReturnType<typeof setTimeout>>()
let nextId = 1

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

  for (const id of [...timers.keys()]) {
    if (!toasts.value.some((candidate) => candidate.id === id)) forget(id)
  }

  hold(toast.id, kind)
  return toast.id
}

export function report(caught: unknown, fallback: string): number {
  const message = caught instanceof Error ? caught.message.trim() : ''
  return notify(message || fallback, 'error')
}

export function dismiss(id: number): void {
  forget(id)
  toasts.value = toasts.value.filter((toast) => toast.id !== id)
}

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
