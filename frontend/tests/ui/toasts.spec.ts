import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearToasts, dismiss, notify, report, toasts } from '@/ui/toasts'

/**
 * What the app has to say, and how long it says it for.
 *
 * Every screen used to keep its own line of red text somewhere down the page, so a
 * failure announced itself wherever that screen happened to put it - often below
 * the fold, and always somewhere the eye had no reason to be.
 */
describe('what the app says', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    clearToasts()
  })

  afterEach(() => {
    clearToasts()
    vi.useRealTimers()
  })

  const texts = () => toasts.value.map((toast) => toast.text)

  it('says a thing, and says what kind of thing it is', () => {
    notify('Saved.', 'done')

    expect(texts()).toEqual(['Saved.'])
    expect(toasts.value[0].kind).toBe('done')
  })

  it('takes itself away', () => {
    notify('Saved.', 'done')
    vi.advanceTimersByTime(3500)

    expect(texts()).toEqual([])
  })

  it('leaves an error up for longer, because it is the one to act on', () => {
    notify('Could not save.', 'error')
    vi.advanceTimersByTime(3500)

    // Still there when a confirmation would already have gone: it often arrives
    // while somebody is still looking at the button they pressed.
    expect(texts()).toEqual(['Could not save.'])

    vi.advanceTimersByTime(4500)
    expect(texts()).toEqual([])
  })

  it('does not stack the same thing twice, and restarts its clock instead', () => {
    notify('Could not reach the server.', 'error')
    vi.advanceTimersByTime(6000)
    notify('Could not reach the server.', 'error')

    // One problem, retried. Three identical cards say no more than one.
    expect(texts()).toEqual(['Could not reach the server.'])

    vi.advanceTimersByTime(6000)
    expect(texts()).toEqual(['Could not reach the server.'])

    vi.advanceTimersByTime(2500)
    expect(texts()).toEqual([])
  })

  it('keeps three at most, newest last', () => {
    notify('One')
    notify('Two')
    notify('Three')
    notify('Four')

    // A screenful of stacked notices is a wall, not a message.
    expect(texts()).toEqual(['Two', 'Three', 'Four'])
  })

  it('quotes the failure, because its own words are the useful ones', () => {
    report(new Error('This group is archived.'), 'Could not save.')

    expect(texts()).toEqual(['This group is archived.'])
    expect(toasts.value[0].kind).toBe('error')
  })

  it('falls back when there is nothing to quote', () => {
    report('a string nobody threw deliberately', 'Could not save.')
    expect(texts()).toEqual(['Could not save.'])

    clearToasts()
    report(new Error('   '), 'Could not save.')
    expect(texts()).toEqual(['Could not save.'])
  })

  it('goes when it is dismissed, and stays gone', () => {
    const id = notify('Saved.', 'done')
    dismiss(id)

    expect(texts()).toEqual([])

    // The timer for a card that is already gone must not fire against the next one.
    notify('Something else')
    vi.advanceTimersByTime(3500)
    expect(texts()).toEqual(['Something else'])
  })

  it('says nothing when there is nothing to say', () => {
    notify('   ')
    expect(texts()).toEqual([])
  })
})
