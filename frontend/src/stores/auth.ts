import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError, type ApiClient } from '@/api/client'
import { resetDatabase, rotateDeviceId } from '@/offline/db'
import { findAccent, resolveAccent, type AccentTheme } from '@/domain/themes'
import { resolveLocale, type Locale } from '@/i18n'

export interface AuthenticatedUser {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  defaultCurrency: string
  prefersLightTheme: boolean
  themeName?: string | null
  locale?: string | null
  isAdmin?: boolean
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

export interface RememberedAccount {
  email: string
  displayName: string
  avatarUrl: string | null
}

const SESSION_KEY = 'split-everything.session'

const DEVICE_ACCOUNT_KEY = 'split-everything.device-account'
const THEME_KEY = 'split-everything.theme'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<AuthenticatedUser | null>(null)
  const tokens = ref<AuthTokens | null>(null)
  const storedTheme = ref<Theme | null>(null)
  const rememberedAccount = ref<RememberedAccount | null>(null)
  let api: ApiClient | null = null

  let hasReconnected = false

  let resumeInFlight: Promise<boolean> | null = null

  const isSignedIn = computed(() => user.value !== null && tokens.value !== null)
  const accessToken = computed(() => tokens.value?.accessToken ?? null)

  const theme = computed<Theme>(() => {
    if (storedTheme.value) return storedTheme.value
    return user.value?.prefersLightTheme ? 'light' : 'dark'
  })

  const accent = computed<AccentTheme>(() => resolveAccent(user.value?.themeName))

  const language = computed<Locale>(() => resolveLocale(user.value?.locale))

  async function setLanguage(tag: string): Promise<void> {
    const next = resolveLocale(tag)
    if (!user.value) return

    user.value = { ...user.value, locale: next }
    persist()

    if (!api) return

    try {
      await updateProfile({ locale: next })
    } catch {
    }
  }

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

  async function resumeSession(): Promise<boolean> {
    if (isSignedIn.value) return true

    resumeInFlight ??= attemptResume().finally(() => {
      resumeInFlight = null
    })

    return resumeInFlight
  }

  async function attemptResume(): Promise<boolean> {
    if (!rememberedAccount.value) return false

    if (await resumeFromCookie()) return true

    return reconnectRememberedAccount()
  }

  async function resumeFromCookie(): Promise<boolean> {
    try {
      const next = await requireApi().probe<AuthTokens>('/auth/refresh')
      if (!next?.accessToken) return false

      tokens.value = next

      user.value = await requireApi().get<AuthenticatedUser>('/auth/me')
      persist()
      return true
    } catch {
      tokens.value = null
      user.value = null
      return false
    }
  }

  async function reconnectRememberedAccount(): Promise<boolean> {
    const remembered = rememberedAccount.value
    if (!remembered) return false

    if (hasReconnected) return false

    try {
      const capabilities = await requireApi().get<{ developmentSignIn: boolean }>(
        '/auth/capabilities',
      )
      if (!capabilities.developmentSignIn) return false

      await signInAsDeveloper(remembered.email)
      hasReconnected = true
      return true
    } catch {
      return false
    }
  }

  function forgetDevice(): void {
    rememberedAccount.value = null
    localStorage.removeItem(DEVICE_ACCOUNT_KEY)
  }

  async function refresh(): Promise<string | null> {
    const current = tokens.value
    if (!current) return null

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
    } catch (caught) {
      if (isRefusedSession(caught)) {
        clear()
        return null
      }

      throw caught
    }
  }

  function isRefusedSession(error: unknown): boolean {
    const status = (error as { status?: unknown } | null)?.status

    return status === 401 || status === 403
  }

  async function signOut(): Promise<void> {
    const current = tokens.value

    try {
      if (current) {
        await requireApi().post('/auth/signout', { refreshToken: current.refreshToken })
      }
    } catch {
    } finally {
      clear()
      forgetDevice()
    }
  }

  function sessionExpired(): void {
    clear()
  }

  async function updateProfile(changes: {
    displayName?: string
    defaultCurrency?: string
    prefersLightTheme?: boolean
    themeName?: string
    locale?: string
  }): Promise<void> {
    const updated = await requireApi().patch<AuthenticatedUser>('/auth/me', changes)
    user.value = updated
    persist()
  }

  async function setAccent(name: string): Promise<void> {
    const theme = findAccent(name)
    if (!theme || !user.value) return

    user.value = { ...user.value, themeName: theme.name }
    persist()

    if (!api) return

    try {
      await updateProfile({ themeName: theme.name })
    } catch {
    }
  }

  async function setTheme(next: Theme): Promise<void> {
    storedTheme.value = next
    localStorage.setItem(THEME_KEY, next)

    if (isSignedIn.value && api) {
      try {
        await updateProfile({ prefersLightTheme: next === 'light' })
      } catch {
      }
    }
  }

  async function deleteAccount(): Promise<void> {
    await requireApi().delete('/auth/me')
    clear()
  }

  function isDeviceTakenError(error: unknown): boolean {
    if (!(error instanceof ApiError) || error.status !== 403) return false
    return error.message.includes('registered to another account')
  }

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
    accent,
    language,
    rememberedAccount,
    resumeSession,
    forgetDevice,
    sessionExpired,
    attachApi,
    restore,
    signInWithGoogle,
    signInAsDeveloper,
    refresh,
    signOut,
    updateProfile,
    setTheme,
    setAccent,
    setLanguage,
    deleteAccount,
  }
})

function detectPlatform(): string {
  const capacitor = (globalThis as { Capacitor?: { getPlatform?: () => string } }).Capacitor
  return capacitor?.getPlatform?.() ?? 'web'
}
