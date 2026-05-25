/**
 * Channels bridge — wraps `/admin/channels/*` + `/admin/gateway/*` exposed by
 * `Tnzi.AI.Channels`. The module is an optional sub-module of Tnzi.AI; when
 * not loaded the backend returns 404 and the bridge surfaces empty values
 * so the page renders an "unavailable" state.
 */
import type { HttpClient } from '@tnzi/core/http'

export interface ChannelAdapterDto {
  name: string
  supportsStreaming: boolean
}

export interface ChannelModuleStatusDto {
  enabled: boolean
  maxConcurrency: number
  streamingThrottleMs: number
  adapters: ChannelAdapterDto[]
}

export interface GatewayStatusDto {
  enabled: boolean
  connectedWebSocketCount: number
  activeSessionCount: number
}

export interface GatewayConnectionInfo {
  connectionId: string
  userId?: string | null
  clientType: string
  deviceNodeId?: string | null
  connectedAt: string
}

export interface SessionBindingRuleDto {
  id: string
  channel?: string | null
  peerKind?: string | null
  peerId?: string | null
  agentId: string
  scope: number
  priority: number
  isEnabled: boolean
  creationTime?: string | null
  lastModificationTime?: string | null
}

export interface ChannelsBridgeDeps {
  client?: HttpClient
}

export interface ChannelsBridge {
  channels: {
    getStatus(): Promise<ChannelModuleStatusDto | null>
    getAdapters(): Promise<ChannelAdapterDto[]>
  }
  gateway: {
    getStatus(): Promise<GatewayStatusDto | null>
    getConnections(): Promise<GatewayConnectionInfo[]>
    getBindings(): Promise<SessionBindingRuleDto[]>
  }
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createChannelsBridge(deps: ChannelsBridgeDeps = {}): ChannelsBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createChannelsBridge: no HttpClient provided'))
    return {
      channels: { getStatus: noOp as never, getAdapters: noOp as never },
      gateway: { getStatus: noOp as never, getConnections: noOp as never, getBindings: noOp as never },
    }
  }

  return {
    channels: {
      getStatus: async () =>
        unwrap<ChannelModuleStatusDto | null>(
          await client.get<ChannelModuleStatusDto>('/admin/channels/status'),
        ),
      getAdapters: async () =>
        unwrap<ChannelAdapterDto[]>(
          await client.get<ChannelAdapterDto[]>('/admin/channels/adapters'),
        ) ?? [],
    },
    gateway: {
      getStatus: async () =>
        unwrap<GatewayStatusDto | null>(
          await client.get<GatewayStatusDto>('/admin/gateway/status'),
        ),
      getConnections: async () =>
        unwrap<GatewayConnectionInfo[]>(
          await client.get<GatewayConnectionInfo[]>('/admin/gateway/connections'),
        ) ?? [],
      getBindings: async () =>
        unwrap<SessionBindingRuleDto[]>(
          await client.get<SessionBindingRuleDto[]>('/admin/gateway/bindings'),
        ) ?? [],
    },
  }
}
