/**
 * Google Identity Services, fetched when the sign-in screen needs it.
 *
 * The screen was written to wait for Google's library to appear on the window, and
 * nothing ever put it there: no script tag in the page, none in the bundle. In
 * development that went unnoticed because the development sign-in is another way in;
 * in production, where that is forced off, it meant the app could not be signed into
 * at all and said only "Google sign-in is unavailable".
 *
 * Loaded here rather than from index.html on purpose. This app opens offline and
 * every other screen works without Google, so the one screen that needs it is the
 * one that should wait for it - and the wait is bounded, because a blocked or
 * unreachable script must leave the page usable rather than pending forever.
 */

const SCRIPT_SRC = 'https://accounts.google.com/gsi/client'

/** How long to wait before treating the script as unavailable. */
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

/** The one in flight, so two screens mounting at once fetch one script. */
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
      // Marked on the tag itself, because a tag left in the document from an
      // earlier attempt will never fire load or error again: without this, a second
      // caller attaches listeners to a finished script and waits out its whole
      // budget for events that have already happened.
      if (tag) tag.dataset.gisSettled = 'true'
      resolve(alreadyLoaded())
    }

    // Bounded, because a script that is blocked rather than slow never fires either
    // event: an ad blocker, a content policy, or a network that swallows it.
    const timer = window.setTimeout(finish, budgetMs)

    // A tag from an earlier attempt, or one added by something else on the page.
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

/** Forgets the in-flight load, for a test that wants a clean slate. */
export function resetGoogleIdentity(): void {
  loading = null
  document.querySelectorAll(`script[src="${SCRIPT_SRC}"]`).forEach((tag) => tag.remove())
}
