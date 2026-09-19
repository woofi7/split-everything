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
