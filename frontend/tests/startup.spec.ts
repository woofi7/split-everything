import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  BLOCKED_MESSAGE,
  STARTUP_BUDGET_MS,
  WEDGED_MESSAGE,
  settleWithin,
  showStartupProblem,
} from '@/startup'
import {
  db,
  isReplicaResponsive,
  onDatabaseBlocked,
  resetBlockedState,
  resetDatabase,
} from '@/offline/db'

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

    expect(() => showStartupProblem('Anything')).not.toThrow()
  })

  it('says which tab to close, and how', () => {
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

    expect(listener).toHaveBeenCalled()
  })
})

describe('isReplicaResponsive', () => {
  it('says yes when the replica answers', async () => {
    await resetDatabase()

    expect(await isReplicaResponsive(1_000)).toBe(true)
  })

  it('counts a refusal as an answer', async () => {
    const get = vi.spyOn(db.meta, 'get').mockRejectedValue(new Error('QuotaExceededError'))

    expect(await isReplicaResponsive(1_000)).toBe(true)
    get.mockRestore()
  })

  it('says no when nothing comes back', async () => {
    vi.useFakeTimers()
    const get = vi.spyOn(db.meta, 'get').mockReturnValue(new Promise(() => {}) as never)

    const answer = isReplicaResponsive(8_000)
    await vi.advanceTimersByTimeAsync(8_100)

    expect(await answer).toBe(false)
    get.mockRestore()
    vi.useRealTimers()
  })

  it('tells someone what to do about it', () => {
    expect(WEDGED_MESSAGE).toContain('not responding')
    expect(WEDGED_MESSAGE).toContain('Close the other tabs')
  })
})
