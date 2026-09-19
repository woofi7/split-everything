import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { db, deviceIdNow, getDeviceId, resetDatabase } from '@/offline/db'
import { ApiError } from '@/api/client'

const alice = {
  id: 'user-1',
  email: 'alice@example.com',
  displayName: 'Alice',
  avatarUrl: null,
  defaultCurrency: 'CAD',
  prefersLightTheme: false,
}

const bob = { ...alice, id: 'user-2', email: 'bob@example.com', displayName: 'Bob' }

const tokens = {
  accessToken: 'access-1',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-1',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
}

const deviceTaken = () =>
  new ApiError(403, 'Forbidden', 'That device is registered to another account.')

function apiRefusingOnce(user = bob) {
  let refused = false

  return {
    post: vi.fn(async (path: string) => {
      if (path === '/auth/dev' || path === '/auth/google') {
        if (!refused) {
          refused = true
          throw deviceTaken()
        }
        return { user, tokens, isNewUser: false, autoJoinedGroupIds: [] }
      }
      return null
    }),
    probe: vi.fn(async () => null),
    get: vi.fn(async () => user),
    patch: vi.fn(async () => user),
    delete: vi.fn(async () => null),
  }
}

async function seedReplica(): Promise<void> {
  await db.groups.put({
    id: 'group-of-the-previous-account',
    name: 'Roommates',
    baseCurrency: 'CAD',
    colorHex: '#4f46e5',
    isArchived: false,
    lineageId: 'l1',
    members: [],
    myNetBalance: 0,
    totalSpend: 0,
    expenseCount: 0,
    updatedAt: '2026-01-01T00:00:00Z',
  })
}

describe('handing a device to another account', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('signs in on a second attempt with a new device id', async () => {
    const api = apiRefusingOnce()
    const store = useAuthStore()
    store.attachApi(api as never)
    const original = await getDeviceId()

    await store.signInAsDeveloper('bob@example.com')

    expect(store.isSignedIn).toBe(true)
    expect(store.user?.email).toBe('bob@example.com')
    expect(deviceIdNow()).not.toBe(original)
    expect(api.post).toHaveBeenCalledTimes(2)
  })

  it('keeps the new device id for next time', async () => {
    const api = apiRefusingOnce()
    const store = useAuthStore()
    store.attachApi(api as never)
    await getDeviceId()

    await store.signInAsDeveloper('bob@example.com')
    const adopted = deviceIdNow()

    expect(await getDeviceId()).toBe(adopted)
  })

  it('clears the replica the previous account left behind', async () => {
    await seedReplica()
    const api = apiRefusingOnce()
    const store = useAuthStore()
    store.attachApi(api as never)

    await store.signInAsDeveloper('bob@example.com')

    expect(await db.groups.count()).toBe(0)
  })

  it('remembers the account that actually got in', async () => {
    const api = apiRefusingOnce()
    const store = useAuthStore()
    store.attachApi(api as never)

    await store.signInAsDeveloper('bob@example.com')

    expect(store.rememberedAccount?.email).toBe('bob@example.com')
  })

  it('recovers a Google sign-in the same way', async () => {
    const api = apiRefusingOnce()
    const store = useAuthStore()
    store.attachApi(api as never)
    const original = await getDeviceId()

    await store.signInWithGoogle('credential')

    expect(store.isSignedIn).toBe(true)
    expect(deviceIdNow()).not.toBe(original)
  })

  it('tries once and no more', async () => {
    const api = {
      post: vi.fn(async () => {
        throw deviceTaken()
      }),
      probe: vi.fn(async () => null),
      get: vi.fn(async () => bob),
      patch: vi.fn(async () => bob),
      delete: vi.fn(async () => null),
    }
    const store = useAuthStore()
    store.attachApi(api as never)

    await expect(store.signInAsDeveloper('bob@example.com')).rejects.toThrow()
    expect(api.post).toHaveBeenCalledTimes(2)
    expect(store.isSignedIn).toBe(false)
  })

  it('leaves the replica alone when the sign-in fails for another reason', async () => {
    await seedReplica()
    const api = {
      post: vi.fn(async () => {
        throw new ApiError(400, 'Bad Request', 'An email address is required.')
      }),
      probe: vi.fn(async () => null),
      get: vi.fn(async () => bob),
      patch: vi.fn(async () => bob),
      delete: vi.fn(async () => null),
    }
    const store = useAuthStore()
    store.attachApi(api as never)

    await expect(store.signInAsDeveloper('')).rejects.toThrow('An email address is required.')

    expect(await db.groups.count()).toBe(1)
    expect(api.post).toHaveBeenCalledTimes(1)
  })

  it('does not disturb a device signing in as its own account', async () => {
    await seedReplica()
    const api = {
      post: vi.fn(async (path: string) =>
        path === '/auth/dev'
          ? { user: alice, tokens, isNewUser: false, autoJoinedGroupIds: [] }
          : null,
      ),
      probe: vi.fn(async () => null),
      get: vi.fn(async () => alice),
      patch: vi.fn(async () => alice),
      delete: vi.fn(async () => null),
    }
    const store = useAuthStore()
    store.attachApi(api as never)
    const original = await getDeviceId()

    await store.signInAsDeveloper('alice@example.com')

    expect(deviceIdNow()).toBe(original)
    expect(await db.groups.count()).toBe(1)
    expect(api.post).toHaveBeenCalledTimes(1)
  })
})
