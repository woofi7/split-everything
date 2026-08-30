/**
 * Getting the app on screen, whatever else is wrong.
 *
 * Everything the app does before its first render is an improvement to that
 * render rather than a requirement for it: the stored session, the local rows, a
 * repaired outbox. None of it is needed for a correct screen, because the route
 * guard and each view fetch what they need on mount. So none of it may be allowed
 * to prevent the app appearing, and a step with no timeout of its own is exactly
 * how a white screen happens: no error, no render, nothing to read.
 */

/** How long startup work gets before the app is shown without it. */
export const STARTUP_BUDGET_MS = 5000

/**
 * Waits for work, but not forever.
 *
 * Resolves either way, and says which happened, so the caller can carry on rather
 * than choose between waiting indefinitely and giving up. A rejection counts as
 * finished: the work is over, and startup is not the place to handle it.
 */
export async function settleWithin(
  work: Promise<unknown>,
  budgetMs: number = STARTUP_BUDGET_MS,
): Promise<'finished' | 'timed-out'> {
  let timer: ReturnType<typeof setTimeout> | undefined

  const timeout = new Promise<'timed-out'>((resolve) => {
    timer = setTimeout(() => resolve('timed-out'), budgetMs)
  })

  try {
    return await Promise.race([work.then(() => 'finished' as const, () => 'finished' as const), timeout])
  } finally {
    clearTimeout(timer)
  }
}

/**
 * Says what went wrong, in place of the app.
 *
 * Plain DOM on purpose. This runs when the app could not start, so it cannot
 * assume Vue mounted, the stylesheet loaded, or the local replica can be read.
 * A button rather than an instruction to reload, because on a phone the reload
 * control is behind a menu.
 */
export function showStartupProblem(message: string, actionLabel = 'Reload'): void {
  const host = document.getElementById('app')
  if (!host) return

  host.textContent = ''

  const panel = document.createElement('div')
  panel.setAttribute('role', 'alert')
  panel.dataset.testid = 'startup-problem'
  panel.style.cssText =
    'max-width:26rem;margin:15vh auto 0;padding:1.5rem;text-align:center;' +
    'font:400 15px/1.5 system-ui,sans-serif;color:#e2e8f0'

  const title = document.createElement('p')
  title.textContent = 'Split Everything could not start'
  title.style.cssText = 'margin:0 0 .75rem;font-size:1.05rem;font-weight:600'

  const detail = document.createElement('p')
  detail.textContent = message
  detail.style.cssText = 'margin:0 0 1.25rem;color:#94a3b8'

  const button = document.createElement('button')
  button.type = 'button'
  button.textContent = actionLabel
  button.style.cssText =
    'min-height:44px;padding:0 1.25rem;border:0;border-radius:.75rem;' +
    'background:#4f46e5;color:#fff;font:inherit;font-weight:600'
  button.addEventListener('click', () => window.location.reload())

  panel.append(title, detail, button)
  host.append(panel)
}

/**
 * A replica that is not answering, for a reason we cannot name from here.
 *
 * The same instruction, because the same thing cures nearly all of it: another
 * page on this device holding the local data. Hedged rather than asserted,
 * because a browser can also refuse storage outright.
 */
export const WEDGED_MESSAGE =
  'The data stored on this device is not responding. This usually means the app ' +
  'is open in another tab. Close the other tabs, then reload.'

/** What a replica held open by another tab needs the person to do. */
export const BLOCKED_MESSAGE =
  'It is open in another tab running an older version. Close the other tabs, ' +
  'then reload. On a phone, closing them from the tab switcher is enough.'
