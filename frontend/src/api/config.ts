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

/**
 * Which build this is.
 *
 * Passed in by the image build as the tag it was cut from, so a crash report from
 * a phone says which version produced it rather than "the app". Unset in dev,
 * where the answer is always "whatever is on disk".
 */
export function appVersion(): string {
  return import.meta.env.VITE_APP_VERSION ?? 'dev'
}
