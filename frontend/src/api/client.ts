export interface ApiClientOptions {
  baseUrl: string
  getAccessToken: () => string | null
  getDeviceId: () => string | null
  /** Returns a fresh access token, or null when the session is over. */
  refreshAccessToken?: () => Promise<string | null>
  onUnauthorized: () => void
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly isOffline: boolean

  constructor(status: number, code: string, message: string, isOffline = false) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.isOffline = isOffline
  }
}

type QueryValue = string | number | boolean | null | undefined

/**
 * The HTTP layer.
 *
 * Two behaviours worth naming. A 401 triggers exactly one refresh-and-retry, and
 * concurrent 401s share that single refresh: refresh tokens rotate, so a second
 * concurrent refresh would invalidate the token the first just issued and sign the
 * user out. A network failure is reported as `isOffline`, which is how the UI
 * tells "we are offline, your change is queued" apart from "the server said no".
 */
export class ApiClient {
  private refreshInFlight: Promise<string | null> | null = null
  private readonly options: ApiClientOptions

  constructor(options: ApiClientOptions) {
    this.options = options
  }

  get<T>(path: string, query?: Record<string, QueryValue>): Promise<T> {
    return this.request<T>('GET', path, { query })
  }

  post<T>(path: string, body?: unknown, query?: Record<string, QueryValue>): Promise<T> {
    return this.request<T>('POST', path, { body, query })
  }

  patch<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PATCH', path, { body })
  }

  put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, { body })
  }

  delete<T>(path: string, query?: Record<string, QueryValue>): Promise<T> {
    return this.request<T>('DELETE', path, { query })
  }

  upload<T>(path: string, files: Record<string, File | string>): Promise<T> {
    const form = new FormData()
    for (const [key, value] of Object.entries(files)) form.append(key, value)

    return this.request<T>('POST', path, { form })
  }

  /**
   * A request whose 401 is an answer rather than a failure.
   *
   * Used to ask whether this device still has a session. The ordinary path treats
   * a 401 as the session ending: it refreshes, and failing that signs the app out
   * and sends the person to sign-in. On the way in that is wrong twice over -
   * there is nothing to refresh yet, and pushing to sign-in would throw away a
   * public page someone deliberately opened, an invite link most of all.
   */
  async probe<T>(path: string): Promise<T | null> {
    try {
      const response = await this.send('POST', path, {})
      if (!response.ok) return null

      const text = await response.text()
      return text ? (JSON.parse(text) as T) : null
    } catch {
      // Offline, or nothing there. Either way there is no session to report.
      return null
    }
  }

  /** Raw response, for endpoints that return a file rather than JSON. */
  async blob(path: string, query?: Record<string, QueryValue>): Promise<Blob> {
    const response = await this.send('GET', path, { query })
    if (!response.ok) throw await this.toError(response)
    return response.blob()
  }

  private async request<T>(
    method: string,
    path: string,
    init: { body?: unknown; query?: Record<string, QueryValue>; form?: FormData },
    isRetry = false,
  ): Promise<T> {
    const response = await this.send(method, path, init)

    if (response.status === 401 && !isRetry && this.options.refreshAccessToken) {
      const token = await this.refresh()
      if (token) return this.request<T>(method, path, init, true)

      this.options.onUnauthorized()
      throw await this.toError(response)
    }

    if (response.status === 401) {
      this.options.onUnauthorized()
      throw await this.toError(response)
    }

    if (!response.ok) throw await this.toError(response)

    if (response.status === 204 || response.headers.get('Content-Length') === '0') {
      return null as T
    }

    const text = await response.text()
    if (!text) return null as T

    try {
      return JSON.parse(text) as T
    } catch {
      return text as unknown as T
    }
  }

  private async send(
    method: string,
    path: string,
    init: { body?: unknown; query?: Record<string, QueryValue>; form?: FormData },
  ): Promise<Response> {
    const headers = new Headers()

    const token = this.options.getAccessToken()
    if (token) headers.set('Authorization', `Bearer ${token}`)

    const deviceId = this.options.getDeviceId()
    if (deviceId) headers.set('X-Device-Id', deviceId)

    let body: BodyInit | undefined
    if (init.form) {
      // No Content-Type: the browser has to add the multipart boundary.
      body = init.form
    } else if (init.body !== undefined) {
      headers.set('Content-Type', 'application/json')
      body = JSON.stringify(init.body)
    }

    try {
      return await fetch(this.buildUrl(path, init.query), {
        method,
        headers,
        body,
        credentials: 'include',
      })
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      throw new ApiError(0, 'Offline', `Could not reach the server (${message}).`, true)
    }
  }

  private async refresh(): Promise<string | null> {
    // Shared, because refresh tokens rotate: two concurrent refreshes would leave
    // one of them holding a token the server has already revoked.
    if (this.refreshInFlight) return this.refreshInFlight

    this.refreshInFlight = (this.options.refreshAccessToken?.() ?? Promise.resolve(null)).finally(
      () => {
        this.refreshInFlight = null
      },
    )

    return this.refreshInFlight
  }

  private buildUrl(path: string, query?: Record<string, QueryValue>): string {
    const base = `${this.options.baseUrl.replace(/\/$/, '')}${path.startsWith('/') ? path : `/${path}`}`
    if (!query) return base

    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null) continue
      params.append(key, String(value))
    }

    const serialised = params.toString()
    return serialised ? `${base}?${serialised}` : base
  }

  private async toError(response: Response): Promise<ApiError> {
    const fallback = `The server returned ${response.status}.`

    try {
      const text = await response.text()
      if (!text) return new ApiError(response.status, 'Error', fallback)

      const problem = JSON.parse(text) as { title?: string; detail?: string }
      return new ApiError(
        response.status,
        problem.title ?? 'Error',
        problem.detail ?? problem.title ?? fallback,
      )
    } catch {
      return new ApiError(response.status, 'Error', fallback)
    }
  }
}
