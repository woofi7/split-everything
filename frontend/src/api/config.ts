/**
 * Build-time configuration, read through functions.
 *
 * Vite inlines `import.meta.env.VITE_*` at transform time, so a component reading
 * it directly cannot be exercised for both the configured and unconfigured cases.
 * Going through a function keeps that testable, and keeps the env surface in one
 * place.
 */
export function googleClientId(): string {
  return import.meta.env.VITE_GOOGLE_CLIENT_ID ?? ''
}

export function apiBaseUrl(): string {
  return import.meta.env.VITE_API_BASE_URL ?? '/api'
}
