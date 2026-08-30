/**
 * Telling the server what broke in here.
 *
 * This app is used on a phone, where there is no console to read and no way to
 * hand over a stack trace. A blank screen used to be reported as "it broke" and
 * nothing else, and finding the cause took a laptop, a cable and a reproduction.
 * Now the app says what happened, on which screen, in which build, and it lands in
 * the server's log beside the requests that led to it.
 *
 * Everything here is deliberately unable to make things worse: it never throws,
 * never blocks a render, never retries, and stops talking after a handful of
 * reports so a render loop cannot flood anything.
 */

export interface ClientErrorReport {
  message: string
  /** render, unhandled, rejection, startup: what kind of failure this was. */
  kind: string
  route?: string
  stack?: string
}

export interface ReportingChannel {
  send: (report: ClientErrorReport & { deviceId?: string; appVersion?: string }) => Promise<unknown>
  route: () => string | undefined
  deviceId: () => string | undefined
  appVersion?: string
}

/** How many reports one page load may send. A loop is one bug, not two hundred. */
const MAX_REPORTS = 8

/** How long the same message stays silent after being sent once. */
const REPEAT_SILENCE_MS = 30_000

let channel: ReportingChannel | null = null
let sent = 0
const lastSeen = new Map<string, number>()

/** Whether a report is already in flight, so a failure to report cannot recurse. */
let reporting = false

export function installErrorReporting(target: ReportingChannel): void {
  channel = target
}

/** Forgets everything, for a test that wants a clean slate. */
export function resetErrorReporting(): void {
  channel = null
  sent = 0
  reporting = false
  lastSeen.clear()
}

/**
 * Sends one report, if it is worth sending.
 *
 * Fire and forget by design: a caller is usually in the middle of failing, and
 * making it await a diagnostic would be its own bug.
 */
export function reportClientError(report: ClientErrorReport): void {
  if (!channel || reporting || sent >= MAX_REPORTS) return
  if (!report.message?.trim()) return

  const now = Date.now()
  const seen = lastSeen.get(report.message)
  if (seen !== undefined && now - seen < REPEAT_SILENCE_MS) return

  lastSeen.set(report.message, now)
  sent += 1
  reporting = true

  const payload = {
    ...report,
    message: report.message.slice(0, 500),
    stack: report.stack?.slice(0, 4000),
    route: report.route ?? channel.route(),
    deviceId: channel.deviceId(),
    appVersion: channel.appVersion,
  }

  void channel
    .send(payload)
    .catch(() => {
      // Offline, refused, rate limited. There is nothing sensible to do with a
      // failure to describe a failure.
    })
    .finally(() => {
      reporting = false
    })
}

/**
 * Watches for the two failures nothing else catches: a script error outside Vue,
 * and a promise nobody handled. Returns the way to stop watching, which is what a
 * test needs and what a hot reload should do.
 */
export function watchForUncaughtErrors(): () => void {
  const onError = (event: ErrorEvent) => {
    reportClientError({
      kind: 'unhandled',
      message: event.message || String(event.error ?? 'Unknown error'),
      stack: event.error instanceof Error ? event.error.stack : undefined,
    })
  }

  const onRejection = (event: PromiseRejectionEvent) => {
    const reason = event.reason
    reportClientError({
      kind: 'rejection',
      message: reason instanceof Error ? reason.message : String(reason),
      stack: reason instanceof Error ? reason.stack : undefined,
    })
  }

  window.addEventListener('error', onError)
  window.addEventListener('unhandledrejection', onRejection)

  return () => {
    window.removeEventListener('error', onError)
    window.removeEventListener('unhandledrejection', onRejection)
  }
}

/** What a Vue error handler needs, without importing Vue's types here. */
export function describeVueError(error: unknown, info: string): ClientErrorReport {
  return {
    kind: 'render',
    message: error instanceof Error ? error.message : String(error),
    stack: error instanceof Error ? error.stack : `Vue: ${info}`,
  }
}
