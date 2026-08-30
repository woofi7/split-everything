/**
 * Stands in for the virtual module the PWA plugin generates.
 *
 * That module only exists once the plugin has run, so a test importing the update
 * prompt cannot resolve it at all. This one keeps the callbacks where a test can
 * reach them, which is also the only way to make the plugin's "a new version is
 * waiting" moment happen on demand.
 */
export interface RegisterOptions {
  immediate?: boolean
  onNeedRefresh?: () => void
  onOfflineReady?: () => void
}

export const pwa: {
  options: RegisterOptions | null
  applied: boolean
  reloaded: boolean
} = { options: null, applied: false, reloaded: false }

export function resetPwa(): void {
  pwa.options = null
  pwa.applied = false
  pwa.reloaded = false
}

export function registerSW(options: RegisterOptions = {}) {
  pwa.options = options

  return async (reload?: boolean) => {
    pwa.applied = true
    pwa.reloaded = reload === true
  }
}
