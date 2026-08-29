import type { ApiClient } from './client'
import type {
  SyncApi,
  SyncOperationRequest,
  SyncPullResult,
  SyncPushResult,
} from '@/offline/syncEngine'

/** The sync engine's transport, over the HTTP API. */
export class HttpSyncApi implements SyncApi {
  private readonly api: ApiClient

  constructor(api: ApiClient) {
    this.api = api
  }

  push(request: { deviceId: string; operations: SyncOperationRequest[] }): Promise<SyncPushResult> {
    return this.api.post<SyncPushResult>('/sync/push', request)
  }

  pull(request: {
    deviceId: string
    groupCursors: Record<string, number>
    maxEntries: number
  }): Promise<SyncPullResult> {
    return this.api.post<SyncPullResult>('/sync/pull', request)
  }

  async acknowledge(groupCursors: Record<string, number>): Promise<void> {
    await this.api.post('/sync/ack', groupCursors)
  }
}
