/**
 * SignalR bridge — delegates to `useAdminSignalRApi`
 * (from `@tnzi/core/services/signalr`) so admin pages get the standard
 * dependency-injection + single-mock-seam pattern other bridges use.
 *
 * Read endpoints (stats, online-users, per-user connections, group lookups)
 * plus one destructive endpoint (force-disconnect a user). All endpoints
 * return 404 when the host app doesn't load Tnzi.SignalR — the bridge
 * still surfaces a typed contract so the page can degrade to an empty state.
 *
 * DTO types are re-exported below so existing page imports keep resolving
 * after the contract moved into `@tnzi/core`.
 */
import {
  useAdminSignalRApi,
  type SignalRStatsDto,
  type ConnectionInfoDto,
  type OnlineUserDto,
} from '@tnzi/core/services/signalr'
import { unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminSignalRApi>[0]

export type {
  SignalRStatsDto,
  ConnectionInfoDto,
  OnlineUserDto,
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

  const api = useAdminSignalRApi(client)

  return {
    getStats: async () =>
      unwrap<SignalRStatsDto | null>(await api.getStats()),
    getOnlineUsers: async () =>
      unwrap<OnlineUserDto[]>(await api.getOnlineUsers()) ?? [],
    isUserOnline: async (userId: string) =>
      unwrap<boolean>(await api.isUserOnline(userId)) ?? false,
    getUserConnections: async (userId: string) =>
      unwrap<OnlineUserDto | null>(await api.getUserConnections(userId)),
    getConnection: async (connectionId: string) =>
      unwrap<ConnectionInfoDto | null>(await api.getConnection(connectionId)),
    disconnectUser: async (userId: string) => {
      await api.disconnectUser(userId)
    },
    getGroupConnections: async (groupName: string) =>
      unwrap<string[]>(await api.getGroupConnections(groupName)) ?? [],
  }
}
