import { ref } from 'vue'

type InstallChoice = { outcome: 'accepted' | 'dismissed' }

interface InstallEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<InstallChoice>
}

export const canInstall = ref(false)

let waiting: InstallEvent | null = null

export function isInstalled(): boolean {
  if (typeof window.matchMedia === 'function' && window.matchMedia('(display-mode: standalone)').matches) {
    return true
  }

  return (window.navigator as { standalone?: boolean }).standalone === true
}

export function canBeInstalled(): boolean {
  return 'serviceWorker' in navigator && window.isSecureContext
}

export function installsByHand(): boolean {
  const agent = navigator.userAgent
  const iPhoneOrIPad = /iPhone|iPad|iPod/.test(agent)
  const iPadPretendingToBeAMac = /Macintosh/.test(agent) && navigator.maxTouchPoints > 1

  return iPhoneOrIPad || iPadPretendingToBeAMac
}

export function watchForInstallPrompt(): () => void {
  const onPrompt = (event: Event) => {
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
