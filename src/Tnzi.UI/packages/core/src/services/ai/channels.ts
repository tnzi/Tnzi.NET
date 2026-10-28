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
  /**
   * Display only: IM replies are always sent whole, so the built-in adapters report `false`.
   * Streaming happens on the WebSocket gateway (`chat.stream` frames), not on IM channels.
   */
  supportsStreaming: boolean;
}

/** Tnzi.AI.Channels module status + per-adapter capabilities. */
export interface ChannelModuleStatusDto {
  enabled: boolean;
  maxConcurrency: number;
  /** Minimum interval between `chat.stream` frames on the `/ws/gateway` WebSocket (IM replies are not streamed). */
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
  connectedAt: string;
}

/** The backend `SessionScope` enum, serialized by member name. */
export type GatewaySessionScope = 'Global' | 'PerPeer' | 'PerChannelPeer' | 'PerThread';

/** Where a session-binding rule comes from. */
export type SessionBindingRuleSource = 'config' | 'database';

/**
 * A session-binding rule (ISessionBinder 4-scope binding). The list holds the rules the binder routes
 * with: configuration rules (`AI:Channels:Gateway:BindingRules`) and database rules, highest priority
 * first, configuration first at equal priority.
 */
export interface SessionBindingRuleDto {
  /** Database rule id; configuration rules are not persisted and carry the empty GUID. */
  id: string;
  channel?: string | null;
  peerKind?: string | null;
  peerId?: string | null;
  agentId: string;
  scope: GatewaySessionScope;
  priority: number;
  isEnabled: boolean;
  source: SessionBindingRuleSource;
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
