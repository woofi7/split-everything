import 'fake-indexeddb/auto'
import { vi } from 'vitest'

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

globalThis.fetch = vi.fn(() =>
  Promise.reject(new Error('Unexpected network call in a test')),
) as unknown as typeof fetch

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
