/**
 * Channels + Gateway admin API wrappers - `/admin/channels/*` + `/admin/gateway/*`
 * exposed by `Tnzi.AI.Channels`.
 *
 * `Tnzi.AI.Channels` is an optional sub-module of `Tnzi.AI`; when not loaded the
 * backend returns 404. Callers are expected to tolerate that (the ui-admin
 * channels bridge surfaces empty values so the page renders an "unavailable"
 * state).
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** A single channel adapter (Telegram / Slack / Discord / Feishu / DingTalk). */
export interface ChannelAdapterDto {
  name: string;
  supportsStreaming: boolean;
}

/** Tnzi.AI.Channels module status + per-adapter capabilities. */
export interface ChannelModuleStatusDto {
  enabled: boolean;
  maxConcurrency: number;
  streamingThrottleMs: number;
  adapters: ChannelAdapterDto[];
}

/** IGateway control-plane status. */
export interface GatewayStatusDto {
  enabled: boolean;
  connectedWebSocketCount: number;
  activeSessionCount: number;
}

/** A live Gateway WebSocket connection. */
export interface GatewayConnectionInfo {
  connectionId: string;
  userId?: string | null;
  clientType: string;
  deviceNodeId?: string | null;
  connectedAt: string;
}

/** A session-binding rule (ISessionBinder 4-scope binding). */
export interface SessionBindingRuleDto {
  id: string;
  channel?: string | null;
  peerKind?: string | null;
  peerId?: string | null;
  agentId: string;
  /** SessionScope enum: 0=Global / 1=PerPeer / 2=PerChannelPeer / 3=PerThread */
  scope: number;
  priority: number;
  isEnabled: boolean;
  creationTime?: string | null;
  lastModificationTime?: string | null;
}

// ---------------------------------------------------------------------------
// Admin: Channels + Gateway API
// Routes: /admin/channels, /admin/gateway
// ---------------------------------------------------------------------------

/** Admin channels + gateway management API */
export function useChannelsAdminApi(client: HttpClient) {
  return {
    /** Get the Tnzi.AI.Channels module status (enabled, concurrency, adapters) */
    getStatus: () =>
      client.get<ChannelModuleStatusDto>('/admin/channels/status'),

    /** List the registered channel adapters */
    getAdapters: () =>
      client.get<ChannelAdapterDto[]>('/admin/channels/adapters'),

    /** Get the IGateway control-plane status */
    getGatewayStatus: () =>
      client.get<GatewayStatusDto>('/admin/gateway/status'),

    /** List the live Gateway WebSocket connections */
    getGatewayConnections: () =>
      client.get<GatewayConnectionInfo[]>('/admin/gateway/connections'),

    /** List the session-binding rules */
    getGatewayBindings: () =>
      client.get<SessionBindingRuleDto[]>('/admin/gateway/bindings'),
  };
}
