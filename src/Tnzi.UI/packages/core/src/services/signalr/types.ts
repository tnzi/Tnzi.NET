/**
 * SignalR Module Types — mirrors `Tnzi.SignalR.Dtos.*` on the .NET side.
 *
 * Models the connection/presence surface exposed by
 * `Tnzi.SignalR/Controllers/DefaultSignalRAdminController`
 * (`/admin/signalr/*`). `Tnzi.SignalR` is optional — every endpoint returns
 * 404 when the host app doesn't load it.
 */

/** Aggregate online-user / connection counts at a point in time. */
export interface SignalRStatsDto {
  onlineUserCount: number;
  totalConnectionCount: number;
  timestamp: string;
}

/** Metadata for a single live SignalR connection. */
export interface ConnectionInfoDto {
  connectionId: string;
  userId?: string | null;
  userName?: string | null;
  connectedAt?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  hubName?: string | null;
  groups: string[];
}

/** A single online user with their live connections. */
export interface OnlineUserDto {
  userId: string;
  connectionCount: number;
  connections: ConnectionInfoDto[];
}
