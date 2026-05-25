/**
 * SignalR bridge — wraps `/admin/signalr/*` exposed by
 * `Tnzi.SignalR.Controllers.DefaultSignalRAdminController`.
 *
 * Read endpoints (stats, online-users, per-user connections, group lookups)
 * plus one destructive endpoint (force-disconnect a user). All endpoints
 * return 404 when the host app doesn't load Tnzi.SignalR — the bridge
 * still surfaces a typed contract so the page can degrade to an empty state.
 */
import type { HttpClient } from '@tnzi/core/http'

export interface SignalRStatsDto {
  onlineUserCount: number
  totalConnectionCount: number
  timestamp: string
}

export interface ConnectionInfoDto {
  connectionId: string
  userId?: string | null
  userName?: string | null
  connectedAt?: string | null
  ipAddress?: string | null
  userAgent?: string | null
  hubName?: string | null
  groups: string[]
}

export interface OnlineUserDto {
  userId: string
  connectionCount: number
  connections: ConnectionInfoDto[]
}

export interface SignalRBridgeDeps {
  client?: HttpClient
}

export interface SignalRBridge {
  getStats(): Promise<SignalRStatsDto | null>
  getOnlineUsers(): Promise<OnlineUserDto[]>
  isUserOnline(userId: string): Promise<boolean>
  getUserConnections(userId: string): Promise<OnlineUserDto | null>
  getConnection(connectionId: string): Promise<ConnectionInfoDto | null>
  disconnectUser(userId: string): Promise<void>
  getGroupConnections(groupName: string): Promise<string[]>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createSignalRBridge(deps: SignalRBridgeDeps = {}): SignalRBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createSignalRBridge: no HttpClient provided'))
    return {
      getStats: noOp as never,
      getOnlineUsers: noOp as never,
      isUserOnline: noOp as never,
      getUserConnections: noOp as never,
      getConnection: noOp as never,
      disconnectUser: noOp as never,
      getGroupConnections: noOp as never,
    }
  }

  return {
    getStats: async () =>
      unwrap<SignalRStatsDto | null>(
        await client.get<SignalRStatsDto>('/admin/signalr/stats'),
      ),
    getOnlineUsers: async () =>
      unwrap<OnlineUserDto[]>(
        await client.get<OnlineUserDto[]>('/admin/signalr/online-users'),
      ) ?? [],
    isUserOnline: async (userId: string) =>
      unwrap<boolean>(
        await client.get<boolean>(`/admin/signalr/users/${encodeURIComponent(userId)}/online`),
      ) ?? false,
    getUserConnections: async (userId: string) =>
      unwrap<OnlineUserDto | null>(
        await client.get<OnlineUserDto>(`/admin/signalr/users/${encodeURIComponent(userId)}/connections`),
      ),
    getConnection: async (connectionId: string) =>
      unwrap<ConnectionInfoDto | null>(
        await client.get<ConnectionInfoDto>(`/admin/signalr/connections/${encodeURIComponent(connectionId)}`),
      ),
    disconnectUser: async (userId: string) => {
      await client.delete(`/admin/signalr/users/${encodeURIComponent(userId)}/connections`)
    },
    getGroupConnections: async (groupName: string) =>
      unwrap<string[]>(
        await client.get<string[]>(`/admin/signalr/groups/${encodeURIComponent(groupName)}/connections`),
      ) ?? [],
  }
}
