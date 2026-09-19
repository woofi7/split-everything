import type { ApiClient } from './client'

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

export function hasApiClient(): boolean {
  return current !== null
}
