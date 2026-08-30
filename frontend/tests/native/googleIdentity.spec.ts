import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { loadGoogleIdentity, resetGoogleIdentity } from '@/native/googleIdentity'

/**
 * Fetching Google's sign-in library.
 *
 * The sign-in screen was written to wait for this library to appear on the window,
 * and nothing ever put it there: no script tag in the page, none in the bundle. The
 * component's own tests stubbed the global, so they all passed while production had
 * no way to sign in at all - it only said "Google sign-in is unavailable".
 */

const SRC = 'https://accounts.google.com/gsi/client'

const identity = () => ({
  initialize: vi.fn(),
  renderButton: vi.fn(),
})

/** The script arriving, the way the browser announces it. */
function scriptLoads(withGoogle = true) {
  const tag = document.querySelector<HTMLScriptElement>(`script[src="${SRC}"]`)
  if (!tag) throw new Error('nothing asked for the script')

  if (withGoogle) {
    ;(window as unknown as { google: unknown }).google = { accounts: { id: identity() } }
  }
  tag.dispatchEvent(new Event('load'))
  return tag
}

beforeEach(() => {
  resetGoogleIdentity()
  delete (window as unknown as Record<string, unknown>).google
})

afterEach(() => {
  resetGoogleIdentity()
  delete (window as unknown as Record<string, unknown>).google
})

describe('loading Google Identity Services', () => {
  it('asks for the script, which is the whole point', async () => {
    const pending = loadGoogleIdentity()

    // The one assertion that would have caught this: something has to fetch it.
    const tag = document.querySelector<HTMLScriptElement>(`script[src="${SRC}"]`)
    expect(tag).not.toBeNull()
    expect(tag!.async).toBe(true)
    expect(tag!.defer).toBe(true)

    scriptLoads()
    expect(await pending).not.toBeNull()
  })

  it('answers with the library once it has arrived', async () => {
    const pending = loadGoogleIdentity()
    scriptLoads()

    const api = await pending
    expect(api?.initialize).toBeTypeOf('function')
    expect(api?.renderButton).toBeTypeOf('function')
  })

  it('answers immediately when it is already there', async () => {
    ;(window as unknown as { google: unknown }).google = { accounts: { id: identity() } }

    expect(await loadGoogleIdentity()).not.toBeNull()
    // Nothing fetched: a second tag would re-run Google's library.
    expect(document.querySelector(`script[src="${SRC}"]`)).toBeNull()
  })

  it('fetches one script for two callers', async () => {
    const first = loadGoogleIdentity()
    const second = loadGoogleIdentity()

    expect(document.querySelectorAll(`script[src="${SRC}"]`)).toHaveLength(1)

    scriptLoads()
    expect(await first).not.toBeNull()
    expect(await second).not.toBeNull()
  })

  it('answers nothing when the script fails', async () => {
    const pending = loadGoogleIdentity()
    document.querySelector(`script[src="${SRC}"]`)!.dispatchEvent(new Event('error'))

    // Blocked by a content policy, or an ad blocker, or no connection.
    expect(await pending).toBeNull()
  })

  it('answers nothing when the script loads without the library', async () => {
    const pending = loadGoogleIdentity()
    scriptLoads(false)

    expect(await pending).toBeNull()
  })

  it('gives up rather than waiting forever', async () => {
    vi.useFakeTimers()

    try {
      const pending = loadGoogleIdentity(8000)
      // A blocked script fires neither load nor error, which is how a sign-in page
      // ends up pending with nothing on it.
      await vi.advanceTimersByTimeAsync(8100)

      expect(await pending).toBeNull()
    } finally {
      vi.useRealTimers()
    }
  })

  it('does not wait on a script that has already failed', async () => {
    // The case that hangs a screen: a tag left from an earlier attempt fires neither
    // event again, so a second caller must read its state rather than listen.
    const first = loadGoogleIdentity()
    document.querySelector(`script[src="${SRC}"]`)!.dispatchEvent(new Event('error'))
    expect(await first).toBeNull()

    const second = loadGoogleIdentity(50)
    expect(await second).toBeNull()
  })

  it('reuses a tag somebody else added', async () => {
    const planted = document.createElement('script')
    planted.src = SRC
    document.head.appendChild(planted)

    const pending = loadGoogleIdentity()
    expect(document.querySelectorAll(`script[src="${SRC}"]`)).toHaveLength(1)

    scriptLoads()
    expect(await pending).not.toBeNull()
  })
})
