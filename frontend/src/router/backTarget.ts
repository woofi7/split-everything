
export function previousScreen(): string | null {
  try {
    const back = (window.history.state as { back?: unknown } | null)?.back
    return typeof back === 'string' && back.startsWith('/') ? back : null
  } catch {
    return null
  }
}

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
