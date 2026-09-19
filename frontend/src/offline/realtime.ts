import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'

export interface RealtimeOptions {
  getAccessToken: () => string | null
  onChanged: (groupId: string) => void
  onConflict?: (conflict: unknown) => void
  hubUrl?: string
}

export function createRealtimeConnection(options: RealtimeOptions): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl(options.hubUrl ?? '/hubs/sync', {
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
    options.onChanged('')
  })

  if (options.getAccessToken()) {
    void connection.start().catch(() => {
    })
  }

  return connection
}

export async function stopRealtime(connection: HubConnection): Promise<void> {
  if (connection.state !== HubConnectionState.Disconnected) await connection.stop()
}
