import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  describeVueError,
  installErrorReporting,
  reportClientError,
  resetErrorReporting,
  watchForUncaughtErrors,
} from '@/diagnostics'

/**
 * Telling the server what broke in the browser.
 *
 * A phone has no console anybody can read, so a blank screen used to be reported
 * as "it broke" and nothing else. Everything here has to be incapable of making
 * things worse: it never throws, never retries, and stops talking long before it
 * could flood anything.
 */

const send = vi.fn(async () => ({}))

const channel = () => ({
  send,
  route: () => 'dashboard',
  deviceId: () => 'device-1',
  appVersion: '1.2.3',
})

beforeEach(() => {
  resetErrorReporting()
  send.mockClear()
  send.mockResolvedValue({})
})

afterEach(() => resetErrorReporting())

describe('reporting a client error', () => {
  it('sends what broke, where, and in which build', async () => {
    installErrorReporting(channel())

    reportClientError({ kind: 'render', message: 'x is not a function', stack: 'at Dash.vue' })
    await vi.waitFor(() => expect(send).toHaveBeenCalled())

    expect(send).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'render',
        message: 'x is not a function',
        stack: 'at Dash.vue',
        route: 'dashboard',
        deviceId: 'device-1',
        appVersion: '1.2.3',
      }),
    )
  })

  it('says nothing at all before it is wired up', () => {
    reportClientError({ kind: 'render', message: 'too early' })

    expect(send).not.toHaveBeenCalled()
  })

  it('ignores a report with nothing in it', () => {
    installErrorReporting(channel())

    reportClientError({ kind: 'render', message: '   ' })

    expect(send).not.toHaveBeenCalled()
  })

  it('says the same thing once', async () => {
    installErrorReporting(channel())

    reportClientError({ kind: 'render', message: 'the same failure' })
    await vi.waitFor(() => expect(send).toHaveBeenCalledTimes(1))
    reportClientError({ kind: 'render', message: 'the same failure' })

    // A render loop is one bug, not two hundred.
    expect(send).toHaveBeenCalledTimes(1)
  })

  it('stops after a handful, whatever they are', async () => {
    installErrorReporting(channel())

    for (let i = 0; i < 30; i++) {
      reportClientError({ kind: 'render', message: `failure ${i}` })
      await vi.waitFor(() => expect(send).toHaveBeenCalled())
    }

    expect(send.mock.calls.length).toBeLessThanOrEqual(8)
  })

  it('swallows a failure to report a failure', async () => {
    send.mockRejectedValue(new Error('offline'))
    installErrorReporting(channel())

    expect(() => reportClientError({ kind: 'render', message: 'while offline' })).not.toThrow()
    await vi.waitFor(() => expect(send).toHaveBeenCalled())
  })

  it('trims a message and a stack that would fill a log line', async () => {
    installErrorReporting(channel())

    reportClientError({ kind: 'render', message: 'm'.repeat(900), stack: 's'.repeat(9000) })
    await vi.waitFor(() => expect(send).toHaveBeenCalled())

    const payload = send.mock.calls[0][0] as { message: string; stack?: string }
    expect(payload.message.length).toBe(500)
    expect(payload.stack?.length).toBe(4000)
  })

  it('reads a Vue failure into a report', () => {
    const report = describeVueError(new TypeError('cannot read x'), 'render function')

    expect(report.kind).toBe('render')
    expect(report.message).toBe('cannot read x')
    expect(report.stack).toContain('TypeError')
  })

  it('describes a thrown thing that is not an error', () => {
    const report = describeVueError('just a string', 'setup function')

    expect(report.message).toBe('just a string')
    expect(report.stack).toContain('setup function')
  })
})

describe('watching for what nothing else catches', () => {
  it('reports a script error outside Vue', async () => {
    installErrorReporting(channel())
    const stop = watchForUncaughtErrors()

    try {
      const event = new Event('error') as ErrorEvent
      Object.assign(event, { message: 'Script error', error: new Error('boom') })
      window.dispatchEvent(event)

      await vi.waitFor(() => expect(send).toHaveBeenCalled())
      expect((send.mock.calls[0][0] as { kind: string }).kind).toBe('unhandled')
    } finally {
      stop()
    }
  })

  it('reports a promise nobody handled', async () => {
    installErrorReporting(channel())
    const stop = watchForUncaughtErrors()

    try {
      const event = new Event('unhandledrejection') as PromiseRejectionEvent
      Object.assign(event, { reason: new Error('nobody caught this') })
      window.dispatchEvent(event)

      await vi.waitFor(() => expect(send).toHaveBeenCalled())
      const payload = send.mock.calls[0][0] as { kind: string; message: string }
      expect(payload.kind).toBe('rejection')
      expect(payload.message).toBe('nobody caught this')
    } finally {
      stop()
    }
  })

  it('stops listening when told to', async () => {
    installErrorReporting(channel())
    watchForUncaughtErrors()()

    const event = new Event('error') as ErrorEvent
    Object.assign(event, { message: 'after the fact' })
    window.dispatchEvent(event)

    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(send).not.toHaveBeenCalled()
  })
})
