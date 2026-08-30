/**
 * Installing the app on the device it is being read on.
 *
 * This is a PWA - manifest, service worker, icons, standalone display - but a
 * browser only offers to install one when it is served over a secure origin
 * (https, or localhost). Read from a plain-HTTP address on the local network, as
 * during development, no install is offered and no service worker registers, which
 * makes the whole thing look like an ordinary web page.
 *
 * Chrome says so by firing beforeinstallprompt, which fires once, early, and long
 * before anybody opens the profile: it is caught here and kept. Safari never fires
 * it and has no API for this at all, so iOS is told what to tap instead.
 */
import { ref } from 'vue'

type InstallChoice = { outcome: 'accepted' | 'dismissed' }

interface InstallEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<InstallChoice>
}

/** Whether the browser has offered to install this app. */
export const canInstall = ref(false)

let waiting: InstallEvent | null = null

/** Whether the app is already running as an installed app rather than in a tab. */
export function isInstalled(): boolean {
  if (typeof window.matchMedia === 'function' && window.matchMedia('(display-mode: standalone)').matches) {
    return true
  }

  // What iOS reports instead, which is the only way to know there.
  return (window.navigator as { standalone?: boolean }).standalone === true
}

/**
 * Whether the browser could install this at all.
 *
 * A service worker needs a secure origin, and without one there is no installable
 * app to offer. Saying that plainly is more use than an install button that never
 * appears.
 */
export function canBeInstalled(): boolean {
  return 'serviceWorker' in navigator && window.isSecureContext
}

/**
 * Whether this device installs by hand, through the share sheet.
 *
 * Every browser on iOS is WebKit underneath and none of them offers to install
 * anything, so the only way is Share and then Add to Home Screen. An iPad reports
 * itself as a Mac, which is why the touch points are part of the question.
 */
export function installsByHand(): boolean {
  const agent = navigator.userAgent
  const iPhoneOrIPad = /iPhone|iPad|iPod/.test(agent)
  const iPadPretendingToBeAMac = /Macintosh/.test(agent) && navigator.maxTouchPoints > 1

  return iPhoneOrIPad || iPadPretendingToBeAMac
}

export function watchForInstallPrompt(): () => void {
  const onPrompt = (event: Event) => {
    // Kept rather than acted on: the browser's own banner is suppressed by this,
    // and the offer belongs in the profile beside the rest of the device settings.
    event.preventDefault()
    waiting = event as InstallEvent
    canInstall.value = true
  }

  const onInstalled = () => {
    waiting = null
    canInstall.value = false
  }

  window.addEventListener('beforeinstallprompt', onPrompt)
  window.addEventListener('appinstalled', onInstalled)

  return () => {
    window.removeEventListener('beforeinstallprompt', onPrompt)
    window.removeEventListener('appinstalled', onInstalled)
  }
}

/**
 * Asks the browser to install it, and answers what the person chose.
 *
 * The kept event can only be used once, so it is dropped either way: a second
 * attempt needs the browser to offer again.
 */
export async function install(): Promise<'accepted' | 'dismissed' | 'unavailable'> {
  const event = waiting
  if (!event) return 'unavailable'

  waiting = null
  canInstall.value = false

  try {
    await event.prompt()
    return (await event.userChoice).outcome
  } catch {
    return 'unavailable'
  }
}
