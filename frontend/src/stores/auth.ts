import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError, type ApiClient } from '@/api/client'
import { resetDatabase, rotateDeviceId } from '@/offline/db'

export interface AuthenticatedUser {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  defaultCurrency: string
  prefersLightTheme: boolean
}

export interface AuthTokens {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
}

export interface SignInResult {
  user: AuthenticatedUser
  tokens: AuthTokens
  isNewUser: boolean
  autoJoinedGroupIds: string[]
}

export type Theme = 'dark' | 'light'

/** Enough to name whose device this is. Never a credential. */
export interface RememberedAccount {
  email: string
  displayName: string
  avatarUrl: string | null
}

const SESSION_KEY = 'split-everything.session'

/**
 * Who this device belongs to, kept apart from the session on purpose.
 *
 * It outlives signing out, which is the whole point: the next visit can ask for
 * this account by name instead of presenting a blank form to someone the device
 * already knows. It holds no credential, only enough to say whose device this is.
 */
const DEVICE_ACCOUNT_KEY = 'split-everything.device-account'
const THEME_KEY = 'split-everything.theme'

/**
 * Session state.
 *
 * The session is mirrored into localStorage so a reload or a cold start of the
 * native shell does not bounce the user back to sign-in. The refresh token is
 * also held in an httpOnly cookie for the browser; the copy here is what the
 * Capacitor shells use, since they have no cookie jar shared with the API.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<AuthenticatedUser | null>(null)
  const tokens = ref<AuthTokens | null>(null)
  const storedTheme = ref<Theme | null>(null)
  const rememberedAccount = ref<RememberedAccount | null>(null)
  let api: ApiClient | null = null

  const isSignedIn = computed(() => user.value !== null && tokens.value !== null)
  const accessToken = computed(() => tokens.value?.accessToken ?? null)

  /** Dark by default, as the spec asks; the user can switch it in settings. */
  const theme = computed<Theme>(() => {
    if (storedTheme.value) return storedTheme.value
    return user.value?.prefersLightTheme ? 'light' : 'dark'
  })

  function attachApi(client: ApiClient): void {
    api = client
  }

  function requireApi(): ApiClient {
    if (!api) throw new Error('The auth store has no API client attached.')
    return api
  }

  function restore(): void {
    const rawTheme = localStorage.getItem(THEME_KEY)
    if (rawTheme === 'dark' || rawTheme === 'light') storedTheme.value = rawTheme

    const rawAccount = localStorage.getItem(DEVICE_ACCOUNT_KEY)
    if (rawAccount) {
      try {
        rememberedAccount.value = JSON.parse(rawAccount) as RememberedAccount
      } catch {
        localStorage.removeItem(DEVICE_ACCOUNT_KEY)
      }
    }

    const raw = localStorage.getItem(SESSION_KEY)
    if (!raw) return

    try {
      const session = JSON.parse(raw) as { user: AuthenticatedUser; tokens: AuthTokens }

      // A dead refresh token cannot be revived, so treat it as signed out rather
      // than showing a logged-in shell that fails on its first request.
      if (new Date(session.tokens.refreshTokenExpiresAt) <= new Date()) {
        localStorage.removeItem(SESSION_KEY)
        return
      }

      user.value = session.user
      tokens.value = session.tokens
    } catch {
      localStorage.removeItem(SESSION_KEY)
    }
  }

  async function signInWithGoogle(credential: string, deviceLabel?: string): Promise<SignInResult> {
    const result = await withDeviceHandover(() =>
      requireApi().post<SignInResult>('/auth/google', {
        idToken: credential,
        deviceLabel: deviceLabel ?? null,
        platform: detectPlatform(),
      }),
    )

    user.value = result.user
    tokens.value = result.tokens
    persist()

    return result
  }

  /**
   * Signs in, and makes room first if this install already belongs to someone.
   *
   * A device id keys every vector clock, so the server refuses to move one between
   * accounts rather than interleaving two histories under one id. That is right,
   * but it left a phone able to hold only one account for the life of the install.
   * A different account here is a new install: it gets a new device id, and the
   * replica the previous account left behind goes with the old one, or the new
   * account would open the app looking at someone else's groups.
   *
   * Once only. A second refusal is a real failure and is reported.
   */
  async function withDeviceHandover<T>(attempt: () => Promise<T>): Promise<T> {
    try {
      return await attempt()
    } catch (error) {
      if (!isDeviceTakenError(error)) throw error

      await resetDatabase()
      await rotateDeviceId()

      return attempt()
    }
  }

  /**
   * Signs in without Google. The server refuses this unless it was deliberately
   * enabled outside production, so the store simply asks and reports the answer.
   */
  async function signInAsDeveloper(email: string, displayName?: string): Promise<SignInResult> {
    const result = await withDeviceHandover(() =>
      requireApi().post<SignInResult>('/auth/dev', {
        email,
        displayName: displayName?.trim() || null,
        deviceId: null,
      }),
    )

    user.value = result.user
    tokens.value = result.tokens
    persist()

    return result
  }

  /**
   * Signs in again on a device that already has a session, without asking.
   *
   * The browser holds the refresh token in an httpOnly cookie the app cannot
   * read, so the only way to find out whether a session is still good is to ask.
   * Posting no token is what tells the server to use the cookie; the native
   * shells send theirs in the body and never reach this path.
   *
   * Returns whether there is a session to work with, so startup can decide
   * between letting someone in and asking who they are.
   */
  async function resumeSession(): Promise<boolean> {
    if (isSignedIn.value) return true

    try {
      // Probed, not posted: a 401 here means "no session", which is an answer.
      // The ordinary path would sign the app out and push to sign-in, discarding
      // whatever public page was being opened.
      const next = await requireApi().probe<AuthTokens>('/auth/refresh')
      if (!next?.accessToken) return false

      tokens.value = next

      // Asked rather than assumed: a rotated token says the session lives, not
      // whose it is, and a shell rendered around a missing user fails on its
      // first real request.
      user.value = await requireApi().get<AuthenticatedUser>('/auth/me')
      persist()
      return true
    } catch {
      // No session, or no server. Either way there is nothing to resume, and the
      // sign-in page explains itself from here.
      tokens.value = null
      user.value = null
      return false
    }
  }

  /** Hands the device to someone else: the next visit starts from nobody. */
  function forgetDevice(): void {
    rememberedAccount.value = null
    localStorage.removeItem(DEVICE_ACCOUNT_KEY)
  }

  async function refresh(): Promise<string | null> {
    const current = tokens.value
    if (!current) return null

    // Another tab may have already done this. Scanning a QR code opens a new tab
    // each time, so a phone ends up with several on one origin, sharing storage
    // but not memory. The server treats a replayed refresh token as theft and
    // revokes every token for the account, so the second tab to ask would sign
    // both of them out. Taking the newer token costs nothing and avoids that.
    const stored = readStoredSession()
    const storedIsNewerForSameAccount =
      stored !== null &&
      stored.user.id === user.value?.id &&
      stored.tokens.refreshToken !== current.refreshToken

    if (storedIsNewerForSameAccount) {
      tokens.value = stored.tokens
      return stored.tokens.accessToken
    }

    try {
      const next = await requireApi().post<AuthTokens>('/auth/refresh', {
        refreshToken: current.refreshToken,
      })

      tokens.value = next
      persist()
      return next.accessToken
    } catch {
      // The chain is gone; anything else would leave the app retrying forever.
      clear()
      return null
    }
  }

  async function signOut(): Promise<void> {
    const current = tokens.value

    try {
      if (current) {
        await requireApi().post('/auth/signout', { refreshToken: current.refreshToken })
      }
    } catch {
      // Signing out locally matters more than telling the server about it.
    } finally {
      clear()
    }
  }

  async function updateProfile(changes: {
    displayName?: string
    defaultCurrency?: string
    prefersLightTheme?: boolean
    locale?: string
  }): Promise<void> {
    const updated = await requireApi().patch<AuthenticatedUser>('/auth/me', changes)
    user.value = updated
    persist()
  }

  async function setTheme(next: Theme): Promise<void> {
    storedTheme.value = next
    localStorage.setItem(THEME_KEY, next)

    if (isSignedIn.value && api) {
      try {
        await updateProfile({ prefersLightTheme: next === 'light' })
      } catch {
        // The local preference already applied; syncing it is a nicety.
      }
    }
  }

  async function deleteAccount(): Promise<void> {
    await requireApi().delete('/auth/me')
    clear()
  }

  /**
   * The server's answer when this install already belongs to another account. The
   * wording is pinned by a test on both sides, since it is the only thing that
   * separates this from any other refusal.
   */
  function isDeviceTakenError(error: unknown): boolean {
    if (!(error instanceof ApiError) || error.status !== 403) return false
    return error.message.includes('registered to another account')
  }

  /** The session as another tab may have left it. Null when absent or unreadable. */
  function readStoredSession(): { user: AuthenticatedUser; tokens: AuthTokens } | null {
    const raw = localStorage.getItem(SESSION_KEY)
    if (!raw) return null

    try {
      return JSON.parse(raw) as { user: AuthenticatedUser; tokens: AuthTokens }
    } catch {
      return null
    }
  }

  function clear(): void {
    user.value = null
    tokens.value = null
    localStorage.removeItem(SESSION_KEY)
  }

  function persist(): void {
    if (!user.value || !tokens.value) return
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ user: user.value, tokens: tokens.value }),
    )

    // Written on every successful sign-in, so the device keeps up with whoever
    // last used it rather than remembering the first person forever.
    rememberedAccount.value = {
      email: user.value.email,
      displayName: user.value.displayName,
      avatarUrl: user.value.avatarUrl,
    }
    localStorage.setItem(DEVICE_ACCOUNT_KEY, JSON.stringify(rememberedAccount.value))
  }

  return {
    user,
    tokens,
    isSignedIn,
    accessToken,
    theme,
    rememberedAccount,
    resumeSession,
    forgetDevice,
    attachApi,
    restore,
    signInWithGoogle,
    signInAsDeveloper,
    refresh,
    signOut,
    updateProfile,
    setTheme,
    deleteAccount,
  }
})

function detectPlatform(): string {
  const capacitor = (globalThis as { Capacitor?: { getPlatform?: () => string } }).Capacitor
  return capacitor?.getPlatform?.() ?? 'web'
}
