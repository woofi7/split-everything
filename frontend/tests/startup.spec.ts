import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  BLOCKED_MESSAGE,
  STARTUP_BUDGET_MS,
  settleWithin,
  showStartupProblem,
} from '@/startup'
import { db, onDatabaseBlocked, resetBlockedState } from '@/offline/db'

/**
 * Getting on screen when startup does not go to plan.
 *
 * A blank page is the worst outcome there is: no error, no content, nothing to
 * act on. It happened for real, because IndexedDB will not upgrade a database
 * while an older connection is open and does not fail either, so the wait before
 * the first render never ended.
 */

describe('settleWithin', () => {
  it('reports work that finished in time', async () => {
    expect(await settleWithin(Promise.resolve('done'), 50)).toBe('finished')
  })

  it('reports work that did not', async () => {
    vi.useFakeTimers()
    const never = new Promise(() => {})

    const outcome = settleWithin(never, 5000)
    await vi.advanceTimersByTimeAsync(5000)

    expect(await outcome).toBe('timed-out')
    vi.useRealTimers()
  })

  it('treats a failure as finished, so startup carries on', async () => {
    // Startup is not the place to handle it: the work is over either way, and the
    // app is more useful on screen than not.
    expect(await settleWithin(Promise.reject(new Error('no')), 50)).toBe('finished')
  })

  it('does not leave its timer running when the work wins', async () => {
    vi.useFakeTimers()
    const clear = vi.spyOn(globalThis, 'clearTimeout')

    await settleWithin(Promise.resolve(1), 5000)

    expect(clear).toHaveBeenCalled()
    clear.mockRestore()
    vi.useRealTimers()
  })

  it('gives startup five seconds by default', () => {
    expect(STARTUP_BUDGET_MS).toBe(5000)
  })
})

describe('showStartupProblem', () => {
  beforeEach(() => {
    document.body.innerHTML = '<div id="app"><p>Loading Split Everything</p></div>'
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('replaces whatever was on screen with the reason', () => {
    showStartupProblem('It is open in another tab.')

    const host = document.getElementById('app')!
    expect(host.textContent).toContain('could not start')
    expect(host.textContent).toContain('It is open in another tab.')
    expect(host.textContent).not.toContain('Loading Split Everything')
  })

  it('announces itself, since it replaces the whole page', () => {
    showStartupProblem('Anything')

    expect(document.querySelector('[role="alert"]')).not.toBeNull()
  })

  it('offers a reload, because a phone hides that control in a menu', () => {
    const reload = vi.fn()
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...window.location, reload },
    })

    showStartupProblem('Anything')
    document.querySelector<HTMLButtonElement>('[data-testid="startup-problem"] button')!.click()

    expect(reload).toHaveBeenCalled()
  })

  it('does nothing when there is no host to write into', () => {
    document.body.innerHTML = ''

    // Called from the last-resort catch, so it must not throw on the way out.
    expect(() => showStartupProblem('Anything')).not.toThrow()
  })

  it('says which tab to close, and how', () => {
    // The instruction has to be actionable on the device where this happens.
    expect(BLOCKED_MESSAGE).toContain('another tab')
    expect(BLOCKED_MESSAGE).toContain('Close the other tabs')
    expect(BLOCKED_MESSAGE).toContain('tab switcher')
  })
})

describe('a replica another tab is holding open', () => {
  beforeEach(() => {
    resetBlockedState()
  })

  afterEach(() => {
    resetBlockedState()
  })

  it('tells whoever is listening', () => {
    const listener = vi.fn()
    onDatabaseBlocked(listener)

    db.on('blocked').fire(new Event('blocked'))

    expect(listener).toHaveBeenCalled()
  })

  it('tells a listener that arrived after the fact', () => {
    db.on('blocked').fire(new Event('blocked'))

    const listener = vi.fn()
    onDatabaseBlocked(listener)

    // The event fires while startup is still wiring itself up, so a listener
    // attached a moment later must not miss it and wait forever.
    expect(listener).toHaveBeenCalled()
  })
})
