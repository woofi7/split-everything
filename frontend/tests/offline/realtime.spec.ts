import { beforeEach, describe, expect, it, vi } from 'vitest'

const handlers = new Map<string, (payload: unknown) => void>()
const start = vi.fn(async () => {})
const stop = vi.fn(async () => {})
let reconnectedHandler: (() => void) | undefined
let builtUrl: string | undefined
let tokenFactory: (() => string) | undefined
let connectionState = 'Connected'

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl(url: string, options: { accessTokenFactory: () => string }) {
      builtUrl = url
      tokenFactory = options.accessTokenFactory
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        on: (name: string, handler: (payload: unknown) => void) => handlers.set(name, handler),
        onreconnected: (handler: () => void) => {
          reconnectedHandler = handler
        },
        start,
        stop,
        get state() {
          return connectionState
        },
      }
    }
  }

  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected' },
    LogLevel: { Warning: 3 },
  }
})

const { createRealtimeConnection, stopRealtime } = await import('@/offline/realtime')

describe('realtime connection', () => {
  beforeEach(() => {
    handlers.clear()
    start.mockClear()
    stop.mockClear()
    reconnectedHandler = undefined
    connectionState = 'Connected'
  })

  it('connects to the sync hub', () => {
    createRealtimeConnection({ getAccessToken: () => 'token', onChanged: vi.fn() })

    expect(builtUrl).toBe('/hubs/sync')
    expect(start).toHaveBeenCalled()
  })

  it('passes the token through a factory, since a websocket handshake takes no headers', () => {
    createRealtimeConnection({ getAccessToken: () => 'token-1', onChanged: vi.fn() })

    expect(tokenFactory?.()).toBe('token-1')
  })

  it('sends an empty token rather than undefined when signed out', () => {
    createRealtimeConnection({ getAccessToken: () => null, onChanged: vi.fn() })

    expect(tokenFactory?.()).toBe('')
  })

  it('does not connect without a token', () => {
    createRealtimeConnection({ getAccessToken: () => null, onChanged: vi.fn() })

    expect(start).not.toHaveBeenCalled()
  })

  it('reports a change for the group it happened in', () => {
    const onChanged = vi.fn()
    createRealtimeConnection({ getAccessToken: () => 'token', onChanged })

    handlers.get('syncChanged')?.({ groupId: 'group-1' })

    expect(onChanged).toHaveBeenCalledWith('group-1')
  })

  it('reports a conflict when one is pushed', () => {
    const onConflict = vi.fn()
    createRealtimeConnection({ getAccessToken: () => 'token', onChanged: vi.fn(), onConflict })

    handlers.get('syncConflict')?.({ conflictId: 'conflict-1' })

    expect(onConflict).toHaveBeenCalledWith({ conflictId: 'conflict-1' })
  })

  it('survives a conflict with no handler attached', () => {
    createRealtimeConnection({ getAccessToken: () => 'token', onChanged: vi.fn() })

    expect(() => handlers.get('syncConflict')?.({})).not.toThrow()
  })

  it('triggers a pull on reconnect', () => {
    const onChanged = vi.fn()
    createRealtimeConnection({ getAccessToken: () => 'token', onChanged })

    reconnectedHandler?.()

    // A reconnect is exactly when the delta pull is due: the cursor, not the
    // connection, is what guarantees nothing was missed.
    expect(onChanged).toHaveBeenCalled()
  })

  it('accepts a custom hub url', () => {
    createRealtimeConnection({
      getAccessToken: () => 'token',
      onChanged: vi.fn(),
      hubUrl: 'https://api.example/hubs/sync',
    })

    expect(builtUrl).toBe('https://api.example/hubs/sync')
  })

  it('swallows a failed connection, since the pull path still works', async () => {
    start.mockRejectedValueOnce(new Error('offline'))

    expect(() =>
      createRealtimeConnection({ getAccessToken: () => 'token', onChanged: vi.fn() }),
    ).not.toThrow()
  })

  it('stops a live connection', async () => {
    const connection = createRealtimeConnection({
      getAccessToken: () => 'token',
      onChanged: vi.fn(),
    })

    await stopRealtime(connection)

    expect(stop).toHaveBeenCalled()
  })

  it('does not stop a connection that is already down', async () => {
    connectionState = 'Disconnected'
    const connection = createRealtimeConnection({
      getAccessToken: () => 'token',
      onChanged: vi.fn(),
    })

    await stopRealtime(connection)

    expect(stop).not.toHaveBeenCalled()
  })
})
