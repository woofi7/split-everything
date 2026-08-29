/**
 * Identifiers, with or without a secure context.
 *
 * crypto.randomUUID is only defined in a secure context, so it is missing when
 * the app is served over plain HTTP on a LAN address - which is how it gets
 * tested on a phone. getDeviceId runs during startup, so calling it unguarded
 * meant bootstrap threw before the app mounted and the screen stayed blank.
 *
 * crypto.getRandomValues carries no such restriction, so the fallback is still
 * cryptographically random rather than Math.random: these ids are primary keys
 * that two devices must never generate alike.
 */
export function newId(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()

  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)

  // Version 4, variant 1, as RFC 4122 requires.
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80

  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')

  return [
    hex.slice(0, 8),
    hex.slice(8, 12),
    hex.slice(12, 16),
    hex.slice(16, 20),
    hex.slice(20),
  ].join('-')
}
