import { afterEach, describe, expect, it } from 'vitest'
import { labelForPath, previousScreen } from '@/router/backTarget'

/**
 * What the back control does, and what it calls itself.
 *
 * It used to be a link to a fixed destination, so an expense opened from the
 * activity feed sent you to the dashboard on the way out - that being where an
 * expense says it belongs. Where somebody actually came from is the history.
 */
describe('the screen behind this one', () => {
  afterEach(() => window.history.replaceState(null, ''))

  it('is nothing at all on a screen opened cold', () => {
    // A shared link, a notification, or the app started fresh: going back there
    // would leave the app, so the screen's own declared destination is used.
    expect(previousScreen()).toBeNull()
  })

  it('is the path the router recorded', () => {
    window.history.replaceState({ back: '/activity' }, '')

    expect(previousScreen()).toBe('/activity')
  })

  it('ignores anything in that slot that is not a path', () => {
    window.history.replaceState({ back: 42 }, '')
    expect(previousScreen()).toBeNull()

    window.history.replaceState({ back: 'https://example.com/elsewhere' }, '')
    expect(previousScreen()).toBeNull()
  })

  it('names the screens a person can come from', () => {
    expect(labelForPath('/activity')).toBe('Activity')
    expect(labelForPath('/stats')).toBe('Stats')
    expect(labelForPath('/dashboard')).toBe('Dashboard')
    expect(labelForPath('/groups/abc')).toBe('Group')
    expect(labelForPath('/groups/abc/settings')).toBe('Settings')
    expect(labelForPath('/groups/abc/settle')).toBe('Settle up')
    expect(labelForPath('/groups/abc/expenses/def')).toBe('Expense')
    expect(labelForPath('/groups/new')).toBe('New group')
  })

  it('reads the longer paths as themselves, not as the group they sit under', () => {
    // Settings lives under a group, and "Back to Group" from a screen reached
    // through settings would name the wrong one.
    expect(labelForPath('/groups/abc/settings')).not.toBe('Group')
    expect(labelForPath('/groups/abc/expenses/def/edit')).toBe('Expense')
  })

  it('says nothing for a path it does not know, so the declared name stands', () => {
    expect(labelForPath('/nowhere')).toBeNull()
  })
})
