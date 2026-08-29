import type { ApiClient } from './client'

/**
 * The one API client the app uses.
 *
 * Views ask for it rather than constructing one. Building a client inside a
 * component meant every view carried a copy of the auth wiring and could only be
 * tested by stubbing global fetch; with a single injected instance a test hands in
 * a fake and the component is exercised for real.
 */
let current: ApiClient | null = null

export function setApiClient(client: ApiClient | null): void {
  current = client
}

export function useApi(): ApiClient {
  if (!current) {
    throw new Error('No API client has been set. Call setApiClient during bootstrap.')
  }
  return current
}

/** True once bootstrap has provided a client. Lets a view degrade rather than throw. */
export function hasApiClient(): boolean {
  return current !== null
}
