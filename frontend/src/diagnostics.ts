
export interface ClientErrorReport {
  message: string
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

const MAX_REPORTS = 8

const REPEAT_SILENCE_MS = 30_000

let channel: ReportingChannel | null = null
let sent = 0
const lastSeen = new Map<string, number>()

let reporting = false

export function installErrorReporting(target: ReportingChannel): void {
  channel = target
}

export function resetErrorReporting(): void {
  channel = null
  sent = 0
  reporting = false
  lastSeen.clear()
}

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
    })
    .finally(() => {
      reporting = false
    })
}

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

export function describeVueError(error: unknown, info: string): ClientErrorReport {
  return {
    kind: 'render',
    message: error instanceof Error ? error.message : String(error),
    stack: error instanceof Error ? error.stack : `Vue: ${info}`,
  }
}
