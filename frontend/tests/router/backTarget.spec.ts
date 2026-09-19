import { afterEach, describe, expect, it } from 'vitest'
import { labelForPath, previousScreen } from '@/router/backTarget'

describe('the screen behind this one', () => {
  afterEach(() => window.history.replaceState(null, ''))

  it('is nothing at all on a screen opened cold', () => {
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
    expect(labelForPath('/groups/abc/settings')).not.toBe('Group')
    expect(labelForPath('/groups/abc/expenses/def/edit')).toBe('Expense')
  })

  it('says nothing for a path it does not know, so the declared name stands', () => {
    expect(labelForPath('/nowhere')).toBeNull()
  })
})
