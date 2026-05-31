/**
 * SignalR Module API — admin access to live connection/presence state.
 *
 * Mirrors `Tnzi.SignalR/Controllers/DefaultSignalRAdminController` — read
 * endpoints (stats, online users, per-user connections, group lookups, online
 * check) plus one destructive endpoint (force-disconnect a user) under
 * `/admin/signalr/*`. `Tnzi.SignalR` is optional, so every endpoint returns 404
 * when it is not loaded by the host app.
 */

import type { HttpClient } from '../../http/http';
import type {
  SignalRStatsDto,
  ConnectionInfoDto,
  OnlineUserDto,
} from './types';

const ADMIN_SIGNALR_BASE = '/admin/signalr';

/**
 * Admin SignalR API — live presence + connection introspection.
 *
 * Example:
 * ```ts
 * const api = useAdminSignalRApi(client);
 * const stats = await api.getStats();
 * const users = await api.getOnlineUsers();
 * await api.disconnectUser(userId);
 * ```
 */
export function useAdminSignalRApi(client: HttpClient) {
  return {
    /** Aggregate online-user / total-connection counts. */
    getStats: () =>
      client.get<SignalRStatsDto>(`${ADMIN_SIGNALR_BASE}/stats`),

    /** Every online user with their live connections. */
    getOnlineUsers: () =>
      client.get<OnlineUserDto[]>(`${ADMIN_SIGNALR_BASE}/online-users`),

    /** Whether a single user currently has any live connection. */
    isUserOnline: (userId: string) =>
      client.get<boolean>(`${ADMIN_SIGNALR_BASE}/users/${encodeURIComponent(userId)}/online`),

    /** A single user's live connections (null when offline). */
    getUserConnections: (userId: string) =>
      client.get<OnlineUserDto>(`${ADMIN_SIGNALR_BASE}/users/${encodeURIComponent(userId)}/connections`),

    /** Metadata for a single connection by id. */
    getConnection: (connectionId: string) =>
      client.get<ConnectionInfoDto>(`${ADMIN_SIGNALR_BASE}/connections/${encodeURIComponent(connectionId)}`),

    /** Force-disconnect all of a user's connections. */
    disconnectUser: (userId: string) =>
      client.delete(`${ADMIN_SIGNALR_BASE}/users/${encodeURIComponent(userId)}/connections`),

    /** Connection ids currently in a named group. */
    getGroupConnections: (groupName: string) =>
      client.get<string[]>(`${ADMIN_SIGNALR_BASE}/groups/${encodeURIComponent(groupName)}/connections`),
  };
}
