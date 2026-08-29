import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'

export interface RealtimeOptions {
  getAccessToken: () => string | null
  onChanged: (groupId: string) => void
  onConflict?: (conflict: unknown) => void
  hubUrl?: string
}

/**
 * Live sync over SignalR.
 *
 * Carries no authority: everything it announces has already been written, and the
 * delta pull delivers the same operations to a client that was disconnected. That
 * is why automatic reconnect with no replay is safe here - the cursor, not the
 * connection, is what guarantees nothing is missed.
 */
export function createRealtimeConnection(options: RealtimeOptions): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl(options.hubUrl ?? '/hubs/sync', {
      // The browser cannot set headers on a WebSocket handshake, so the token
      // travels as a query parameter the API reads for hub paths only.
      accessTokenFactory: () => options.getAccessToken() ?? '',
    })
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('syncChanged', (payload: { groupId: string }) => {
    options.onChanged(payload.groupId)
  })

  connection.on('syncConflict', (conflict: unknown) => {
    options.onConflict?.(conflict)
  })

  connection.onreconnected(() => {
    // A reconnect is exactly when a delta pull is due.
    options.onChanged('')
  })

  if (options.getAccessToken()) {
    void connection.start().catch(() => {
      // Offline or unauthenticated: the pull path still works.
    })
  }

  return connection
}

export async function stopRealtime(connection: HubConnection): Promise<void> {
  if (connection.state !== HubConnectionState.Disconnected) await connection.stop()
}
