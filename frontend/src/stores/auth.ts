import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import type { ApiClient } from '@/api/client'

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

const SESSION_KEY = 'split-everything.session'
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
    const result = await requireApi().post<SignInResult>('/auth/google', {
      idToken: credential,
      deviceLabel: deviceLabel ?? null,
      platform: detectPlatform(),
    })

    user.value = result.user
    tokens.value = result.tokens
    persist()

    return result
  }

  async function refresh(): Promise<string | null> {
    const current = tokens.value
    if (!current) return null

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
  }

  return {
    user,
    tokens,
    isSignedIn,
    accessToken,
    theme,
    attachApi,
    restore,
    signInWithGoogle,
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
