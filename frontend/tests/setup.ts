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
