export function googleClientId(): string {
  return import.meta.env.VITE_GOOGLE_CLIENT_ID ?? ''
}

export function apiBaseUrl(): string {
  return import.meta.env.VITE_API_BASE_URL ?? '/api'
}

export function appVersion(): string {
  return import.meta.env.VITE_APP_VERSION ?? 'dev'
}
