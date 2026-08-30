import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiClient, ApiError } from '@/api/client'

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('api client', () => {
  let fetchMock: ReturnType<typeof vi.fn>
  let client: ApiClient

  beforeEach(() => {
    fetchMock = vi.fn()
    globalThis.fetch = fetchMock as unknown as typeof fetch
    client = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => 'token-1',
      getDeviceId: () => 'device-a',
      onUnauthorized: vi.fn(),
    })
  })

  it('sends the bearer token', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ok: true }))

    await client.get('/groups')

    const [, init] = fetchMock.mock.calls[0]
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer token-1')
  })

  it('sends the device id, which the server needs for the vector clock', async () => {
    fetchMock.mockResolvedValue(jsonResponse({}))

    await client.get('/groups')

    const [, init] = fetchMock.mock.calls[0]
    expect(new Headers(init.headers).get('X-Device-Id')).toBe('device-a')
  })

  it('omits the authorization header when there is no token', async () => {
    const anonymous = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => null,
      getDeviceId: () => 'device-a',
      onUnauthorized: vi.fn(),
    })
    fetchMock.mockResolvedValue(jsonResponse({}))

    await anonymous.get('/invites/abc')

    const [, init] = fetchMock.mock.calls[0]
    expect(new Headers(init.headers).has('Authorization')).toBe(false)
  })

  it('returns the parsed body', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ name: 'Roommates' }))

    expect(await client.get<{ name: string }>('/groups/1')).toEqual({ name: 'Roommates' })
  })

  it('posts a JSON body', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ id: 'group-1' }))

    await client.post('/groups', { name: 'Roommates' })

    const [, init] = fetchMock.mock.calls[0]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body)).toEqual({ name: 'Roommates' })
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json')
  })

  it('handles a 204 with no body', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))

    expect(await client.delete('/expenses/1')).toBeNull()
  })

  it('turns a problem response into a readable error', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ title: 'Validation', detail: 'Group name is required.' }, 400),
    )

    const error = await client.post('/groups', {}).catch((e) => e as ApiError)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(400)
    expect(error.code).toBe('Validation')
    expect(error.message).toBe('Group name is required.')
  })

  it('falls back to a generic message when the body is not a problem response', async () => {
    fetchMock.mockResolvedValue(new Response('gateway error', { status: 502 }))

    const error = await client.get('/groups').catch((e) => e as ApiError)

    expect(error.status).toBe(502)
    expect(error.message).toBeTruthy()
  })

  it('refreshes once and retries after a 401', async () => {
    // Refresh is delegated to the auth store, so it consumes no fetch of its own:
    // the 401 is followed directly by the retried request.
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ title: 'Unauthorized' }, 401))
      .mockResolvedValueOnce(jsonResponse({ ok: true }))

    const refreshed = vi.fn(async () => 'token-2')
    const retrying = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => 'token-1',
      getDeviceId: () => 'device-a',
      refreshAccessToken: refreshed,
      onUnauthorized: vi.fn(),
    })

    expect(await retrying.get('/groups')).toEqual({ ok: true })
    expect(refreshed).toHaveBeenCalledTimes(1)
  })

  it('gives up and signs out when the refresh also fails', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ title: 'Unauthorized' }, 401))
    const onUnauthorized = vi.fn()
    const failing = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => 'token-1',
      getDeviceId: () => 'device-a',
      refreshAccessToken: vi.fn(async () => null),
      onUnauthorized,
    })

    await expect(failing.get('/groups')).rejects.toBeInstanceOf(ApiError)
    expect(onUnauthorized).toHaveBeenCalled()
  })

  it('does not retry a 401 more than once, so a bad token cannot loop', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ title: 'Unauthorized' }, 401))
    const refreshed = vi.fn(async () => 'token-2')
    const looping = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => 'token-1',
      getDeviceId: () => 'device-a',
      refreshAccessToken: refreshed,
      onUnauthorized: vi.fn(),
    })

    await expect(looping.get('/groups')).rejects.toBeInstanceOf(ApiError)
    expect(refreshed).toHaveBeenCalledTimes(1)
  })

  it('shares one refresh across concurrent requests', async () => {
    let refreshCalls = 0
    fetchMock.mockImplementation(async (url: string) => {
      if (String(url).includes('/auth/refresh')) return jsonResponse({ accessToken: 'token-2' })
      return refreshCalls > 0 ? jsonResponse({ ok: true }) : jsonResponse({ title: 'Unauthorized' }, 401)
    })

    const refreshed = vi.fn(async () => {
      refreshCalls += 1
      return 'token-2'
    })
    const concurrent = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => 'token-1',
      getDeviceId: () => 'device-a',
      refreshAccessToken: refreshed,
      onUnauthorized: vi.fn(),
    })

    await Promise.all([concurrent.get('/groups'), concurrent.get('/expenses')])

    // Two parallel 401s must not trigger two refreshes: the second would rotate
    // the token the first just issued and invalidate the whole chain.
    expect(refreshed).toHaveBeenCalledTimes(1)
  })

  it('reports a network failure as an offline error', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    const error = await client.get('/groups').catch((e) => e as ApiError)

    expect(error.isOffline).toBe(true)
  })

  it('builds a query string from parameters', async () => {
    fetchMock.mockResolvedValue(jsonResponse([]))

    await client.get('/expenses', { groupId: 'group-1', page: 2, includeArchived: false })

    const [url] = fetchMock.mock.calls[0]
    expect(String(url)).toContain('groupId=group-1')
    expect(String(url)).toContain('page=2')
    expect(String(url)).toContain('includeArchived=false')
  })

  it('leaves undefined parameters out of the query string', async () => {
    fetchMock.mockResolvedValue(jsonResponse([]))

    await client.get('/expenses', { groupId: 'group-1', memberId: undefined })

    expect(String(fetchMock.mock.calls[0][0])).not.toContain('memberId')
  })

  it('uploads a file as multipart', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ id: 'receipt-1' }))
    const file = new File(['bytes'], 'till.jpg', { type: 'image/jpeg' })

    await client.upload('/receipts', { file })

    const [, init] = fetchMock.mock.calls[0]
    expect(init.body).toBeInstanceOf(FormData)
    // The browser must set the multipart boundary itself.
    expect(new Headers(init.headers).has('Content-Type')).toBe(false)
  })
})

describe('probing for a session', () => {
  const jsonResponse = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    })

  it('returns the body when there is a session', async () => {
    const onUnauthorized = vi.fn()
    const refreshAccessToken = vi.fn(async () => 'fresh')
    const fetchMock = vi.fn(async () => jsonResponse({ accessToken: 'a' }))
    vi.stubGlobal('fetch', fetchMock)

    const client = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => null,
      getDeviceId: () => 'device-a',
      onUnauthorized,
      refreshAccessToken,
    })

    expect(await client.probe<{ accessToken: string }>('/auth/refresh')).toEqual({
      accessToken: 'a',
    })
    vi.unstubAllGlobals()
  })

  it('treats a 401 as an answer, not a failure', async () => {
    const onUnauthorized = vi.fn()
    const refreshAccessToken = vi.fn(async () => 'fresh')
    const fetchMock = vi.fn(async () => jsonResponse({ title: 'Unauthorized' }, 401))
    vi.stubGlobal('fetch', fetchMock)

    const client = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => null,
      getDeviceId: () => 'device-a',
      onUnauthorized,
      refreshAccessToken,
    })

    const result = await client.probe('/auth/refresh')

    // Signing the app out and pushing to sign-in on the way in would throw away a
    // public page someone had deliberately opened, an invite link most of all.
    expect(result).toBeNull()
    expect(onUnauthorized).not.toHaveBeenCalled()
    expect(refreshAccessToken).not.toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalledTimes(1)
    vi.unstubAllGlobals()
  })

  it('returns nothing when the server cannot be reached', async () => {
    const onUnauthorized = vi.fn()
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new Error('network down')
    }))

    const client = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => null,
      getDeviceId: () => 'device-a',
      onUnauthorized,
    })

    expect(await client.probe('/auth/refresh')).toBeNull()
    expect(onUnauthorized).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })
})

/**
 * A stalled request is not a slow one.
 *
 * A phone that sleeps its wifi or changes network mid-request leaves the
 * connection open and silent. fetch has no timeout of its own, so nothing ever
 * settled: the sync indicator span the rest of the page's life, and because a
 * flush and a token refresh are each single-flight, every later one waited behind
 * a promise that was never going to resolve.
 */
describe('a request that never answers', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  /** A connection that goes quiet: it answers only the abort. */
  function stalls() {
    return vi.fn(
      (_url: string, init: RequestInit) =>
        new Promise((_resolve, reject) => {
          init.signal?.addEventListener('abort', () =>
            reject(new DOMException('The operation was aborted.', 'AbortError')),
          )
        }),
    )
  }

  function clientWith(timeoutMs?: number) {
    return new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => 'token-1',
      getDeviceId: () => 'device-a',
      onUnauthorized: vi.fn(),
      timeoutMs,
    })
  }

  beforeEach(() => {
    vi.useFakeTimers()
    fetchMock = stalls()
    globalThis.fetch = fetchMock as unknown as typeof fetch
  })

  it('gives up after twenty seconds', async () => {
    const pending = clientWith().get('/groups')
    const caught = pending.catch((error: unknown) => error)

    await vi.advanceTimersByTimeAsync(19_000)
    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(false)

    await vi.advanceTimersByTimeAsync(1_500)
    const error = await caught

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).isOffline).toBe(true)
    expect((error as ApiError).message).toContain('did not respond within 20 seconds')
    vi.useRealTimers()
  })

  it('reports it as offline, so the change stays queued', async () => {
    const caught = clientWith(1_000).post('/sync/push', {}).catch((e: unknown) => e as ApiError)
    await vi.advanceTimersByTimeAsync(1_100)

    // Not a refusal: the server never said no, so the operation is still good and
    // is sent again later. A hard error would discard it.
    expect((await caught).isOffline).toBe(true)
    vi.useRealTimers()
  })

  it('aborts the request rather than leaving it open', async () => {
    void clientWith(1_000).get('/groups').catch(() => {})
    await vi.advanceTimersByTimeAsync(1_100)

    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(true)
    vi.useRealTimers()
  })

  it('gives an upload two minutes, because it is carrying a file', async () => {
    const form = new FormData()
    form.append('file', new Blob(['bytes']), 'receipt.png')
    void clientWith().upload('/receipts', form).catch(() => {})

    await vi.advanceTimersByTimeAsync(30_000)
    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(false)

    await vi.advanceTimersByTimeAsync(91_000)
    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(true)
    vi.useRealTimers()
  })
})

describe('a request that answers in time', () => {
  it('does not abort afterwards', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    globalThis.fetch = fetchMock as unknown as typeof fetch

    const client = new ApiClient({
      baseUrl: '/api',
      getAccessToken: () => null,
      getDeviceId: () => null,
      onUnauthorized: vi.fn(),
    })

    await client.get('/groups')
    await vi.advanceTimersByTimeAsync(60_000)

    // The deadline has to be cleared, or a long-lived page accumulates a timer
    // per request and aborts a signal nobody is listening to any more.
    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(false)
    vi.useRealTimers()
  })
})
