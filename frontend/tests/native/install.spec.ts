import { afterEach, describe, expect, it } from 'vitest'
import {
  canBeInstalled,
  canInstall,
  install,
  installsByHand,
  isInstalled,
  watchForInstallPrompt,
} from '@/native/install'

/**
 * Installing the app on the device reading it.
 *
 * This is a PWA, but a browser only offers to install one from a secure origin, so
 * read from a plain address on the local network it looks like an ordinary page.
 * Chrome says it can by firing an event, once, early; Safari never says anything and
 * has to be told what to tap.
 */

const define = (target: object, name: string, value: unknown) => {
  const had = Object.getOwnPropertyDescriptor(target, name)
  Object.defineProperty(target, name, { value, configurable: true })

  return () => {
    if (had) Object.defineProperty(target, name, had)
    else delete (target as Record<string, unknown>)[name]
  }
}

const prompts: (() => void)[] = []

afterEach(() => {
  for (const undo of prompts.splice(0)) undo()
  canInstall.value = false
})

function offer(outcome: 'accepted' | 'dismissed' = 'accepted') {
  const event = new Event('beforeinstallprompt')
  let asked = false

  Object.assign(event, {
    prompt: async () => {
      asked = true
    },
    userChoice: Promise.resolve({ outcome }),
  })

  window.dispatchEvent(event)
  return () => asked
}

describe('installing the app', () => {
  it('knows it is not installed when running in a tab', () => {
    prompts.push(define(window, 'matchMedia', () => ({ matches: false })))

    expect(isInstalled()).toBe(false)
  })

  it('knows it is installed when the display mode says so', () => {
    prompts.push(define(window, 'matchMedia', (query: string) => ({
      matches: query.includes('standalone'),
    })))

    expect(isInstalled()).toBe(true)
  })

  it('knows it is installed the way iOS reports it', () => {
    prompts.push(define(window, 'matchMedia', () => ({ matches: false })))
    prompts.push(define(navigator, 'standalone', true))

    expect(isInstalled()).toBe(true)
  })

  it('says a plain address cannot install anything', () => {
    // No service worker outside a secure origin, so no installable app either.
    prompts.push(define(window, 'isSecureContext', false))

    expect(canBeInstalled()).toBe(false)
  })

  it('says a secure origin with a service worker can', () => {
    prompts.push(define(window, 'isSecureContext', true))
    prompts.push(define(navigator, 'serviceWorker', {}))

    expect(canBeInstalled()).toBe(true)
  })

  it('recognises an iPhone, which installs through the share sheet', () => {
    prompts.push(define(navigator, 'userAgent', 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0)'))

    expect(installsByHand()).toBe(true)
  })

  it('recognises an iPad, which claims to be a Mac', () => {
    prompts.push(define(navigator, 'userAgent', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15)'))
    prompts.push(define(navigator, 'maxTouchPoints', 5))

    expect(installsByHand()).toBe(true)
  })

  it('does not mistake a desktop Mac for one', () => {
    prompts.push(define(navigator, 'userAgent', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15)'))
    prompts.push(define(navigator, 'maxTouchPoints', 0))

    expect(installsByHand()).toBe(false)
  })

  it('catches the browser offer, which fires once and early', () => {
    const stop = watchForInstallPrompt()

    try {
      offer()
      expect(canInstall.value).toBe(true)
    } finally {
      stop()
    }
  })

  it('installs when asked, and answers what was chosen', async () => {
    const stop = watchForInstallPrompt()

    try {
      const asked = offer('accepted')

      expect(await install()).toBe('accepted')
      expect(asked()).toBe(true)
      // The offer is spent: a second attempt needs the browser to offer again.
      expect(canInstall.value).toBe(false)
      expect(await install()).toBe('unavailable')
    } finally {
      stop()
    }
  })

  it('reports a dismissal as a dismissal', async () => {
    const stop = watchForInstallPrompt()

    try {
      offer('dismissed')
      expect(await install()).toBe('dismissed')
    } finally {
      stop()
    }
  })

  it('has nothing to install before the browser has offered', async () => {
    expect(await install()).toBe('unavailable')
  })

  it('forgets the offer once the app is installed', () => {
    const stop = watchForInstallPrompt()

    try {
      offer()
      window.dispatchEvent(new Event('appinstalled'))

      expect(canInstall.value).toBe(false)
    } finally {
      stop()
    }
  })

  it('stops listening when told to', () => {
    watchForInstallPrompt()()

    offer()
    expect(canInstall.value).toBe(false)
  })
})
