import 'fake-indexeddb/auto'
import { vi } from 'vitest'

// jsdom has no crypto.randomUUID in every version, and the app leans on it for
// client-generated ids that make offline creates idempotent.
if (!globalThis.crypto?.randomUUID) {
  Object.defineProperty(globalThis.crypto, 'randomUUID', {
    value: () =>
      '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, (c) =>
        (
          Number(c) ^
          (crypto.getRandomValues(new Uint8Array(1))[0] & (15 >> (Number(c) / 4)))
        ).toString(16),
      ),
  })
}

// Nothing in the test suite should reach the network by accident.
globalThis.fetch = vi.fn(() =>
  Promise.reject(new Error('Unexpected network call in a test')),
) as unknown as typeof fetch

/*
 * An external script in jsdom never settles.
 *
 * jsdom does not fetch one and fires neither load nor error, so anything waiting on
 * a third-party library - Google's sign-in, for instance - waits for its whole
 * timeout in every test that does not stub the global first. Failing them at once is
 * both faster and the truthful default: a test environment cannot reach
 * accounts.google.com, exactly like a browser behind a content policy that blocks
 * it. A test that wants the library puts it on the window before mounting.
 */
const watchForScripts = new MutationObserver((changes) => {
  for (const change of changes) {
    for (const node of change.addedNodes) {
      const script = node as HTMLScriptElement
      if (script.tagName === 'SCRIPT' && script.src) {
        queueMicrotask(() => script.dispatchEvent(new Event('error')))
      }
    }
  }
})

watchForScripts.observe(document.documentElement, { childList: true, subtree: true })
