
const SCRIPT_SRC = 'https://accounts.google.com/gsi/client'

const LOAD_BUDGET_MS = 8000

export interface GoogleIdentity {
  initialize: (options: Record<string, unknown>) => void
  renderButton: (target: HTMLElement, options: Record<string, unknown>) => void
  prompt?: () => void
}

interface WindowWithGoogle {
  google?: { accounts?: { id?: GoogleIdentity } }
}

function alreadyLoaded(): GoogleIdentity | null {
  return (window as unknown as WindowWithGoogle).google?.accounts?.id ?? null
}

let loading: Promise<GoogleIdentity | null> | null = null

export function loadGoogleIdentity(budgetMs: number = LOAD_BUDGET_MS): Promise<GoogleIdentity | null> {
  const ready = alreadyLoaded()
  if (ready) return Promise.resolve(ready)

  loading ??= fetchScript(budgetMs).finally(() => {
    loading = null
  })

  return loading
}

function fetchScript(budgetMs: number): Promise<GoogleIdentity | null> {
  return new Promise((resolve) => {
    let settled = false

    const finish = (tag?: HTMLScriptElement) => {
      if (settled) return
      settled = true
      window.clearTimeout(timer)
      if (tag) tag.dataset.gisSettled = 'true'
      resolve(alreadyLoaded())
    }

    const timer = window.setTimeout(finish, budgetMs)

    const existing = document.querySelector<HTMLScriptElement>(`script[src="${SCRIPT_SRC}"]`)
    if (existing) {
      if (existing.dataset.gisSettled === 'true') {
        finish()
        return
      }

      existing.addEventListener('load', () => finish(existing), { once: true })
      existing.addEventListener('error', () => finish(existing), { once: true })
      return
    }

    const script = document.createElement('script')
    script.src = SCRIPT_SRC
    script.async = true
    script.defer = true
    script.addEventListener('load', () => finish(script), { once: true })
    script.addEventListener('error', () => finish(script), { once: true })
    document.head.appendChild(script)
  })
}

export function resetGoogleIdentity(): void {
  loading = null
  document.querySelectorAll(`script[src="${SCRIPT_SRC}"]`).forEach((tag) => tag.remove())
}
