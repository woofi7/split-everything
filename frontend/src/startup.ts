
export const STARTUP_BUDGET_MS = 5000

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

export const WEDGED_MESSAGE =
  'The data stored on this device is not responding. This usually means the app ' +
  'is open in another tab. Close the other tabs, then reload.'

export const BLOCKED_MESSAGE =
  'It is open in another tab running an older version. Close the other tabs, ' +
  'then reload. On a phone, closing them from the tab switcher is enough.'

export const RENDER_MESSAGE =
  'Something on this screen could not be drawn. Reload to carry on. The details ' +
  'have been sent to the server so this can be fixed.'

export const SCREEN_MESSAGE =
  'That screen could not be loaded. It needs a connection the first time it is ' +
  'opened. Reload once you are back online and it will be available offline after ' +
  'that.'
