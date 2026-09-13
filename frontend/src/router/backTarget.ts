/**
 * What the back control does, and what it should call itself.
 *
 * Its own file, with no import of the router: the shell reads this on every
 * screen, and pulling the router in there would build a real one inside every
 * test that mounts a view with a mocked one.
 */

/**
 * The screen behind the one on show, or null when there is none.
 *
 * The control used to be a link to a fixed destination, which is right when there
 * is nowhere to go back to and wrong the rest of the time: an expense opened from
 * the activity feed sent you to the dashboard on the way out, because that is
 * where an expense says it belongs. Where somebody actually came from is the
 * history, and the history is what back means.
 *
 * Read from the entry the router wrote rather than from a counter kept here: it
 * survives a reload, and it is null in exactly the cases that need the declared
 * destination - a shared link, a notification, or the app opened cold - where
 * going back would leave the app altogether.
 */
export function previousScreen(): string | null {
  try {
    const back = (window.history.state as { back?: unknown } | null)?.back
    return typeof back === 'string' && back.startsWith('/') ? back : null
  } catch {
    // No history object worth reading: treat it as nothing behind us.
    return null
  }
}

/**
 * What to call that screen.
 *
 * The control names where it leads rather than saying "back", which is worth
 * keeping now that where it leads is wherever somebody came from. Matched on the
 * path rather than resolved through the router, for the same reason this file
 * exists; anything unrecognised falls back to whatever the screen declared.
 */
const SCREENS: ReadonlyArray<[RegExp, string]> = [
  [/^\/groups\/[^/]+\/settings/, 'Settings'],
  [/^\/groups\/[^/]+\/settle/, 'Settle up'],
  [/^\/groups\/[^/]+\/expenses\/[^/]+/, 'Expense'],
  [/^\/groups\/new/, 'New group'],
  [/^\/groups\/[^/]+/, 'Group'],
  [/^\/dashboard/, 'Dashboard'],
  [/^\/activity/, 'Activity'],
  [/^\/stats/, 'Stats'],
  [/^\/admin/, 'Server groups'],
  [/^\/profile\/settings/, 'Settings'],
  [/^\/profile/, 'Profile'],
  [/^\/conflicts/, 'Sync'],
  [/^\/import/, 'Import'],
  [/^\/add\/payment/, 'Add payment'],
  [/^\/add/, 'Add expense'],
]

export function labelForPath(path: string): string | null {
  return SCREENS.find(([pattern]) => pattern.test(path))?.[1] ?? null
}
