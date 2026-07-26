import type { HubConnection } from '@microsoft/signalr';
import { createHubClient, type HubClientOptions } from '../signalr/hub-client';

/**
 * Payload of the `Settings.Changed` SignalR event, broadcast by the backend
 * (Tnzi.System `SettingChangedEventHandler`) to ALL connected clients whenever a
 * Global-scope setting is written. Carries only the changed key (never the value,
 * so no secret leaks) - clients decide by key prefix whether to hot-refresh the
 * affected config (e.g. `Chat:*` → re-fetch chat config, `Appearance:AdminTheme`
 * → reload the global theme) without a manual page reload.
 */
export interface SettingsChangedPayload {
  /** The changed setting key, e.g. `Chat:AllowInvisible` or `Appearance:AdminTheme`. */
  key: string;
  /** True when the setting was removed (reverted to its default). */
  isRemoval: boolean;
}

export type SettingsRealtimeClientOptions = HubClientOptions;

type SettingsEvent = 'Settings.Changed';

export interface SettingsRealtimeClient {
  start(): Promise<void>;
  stop(): Promise<void>;
  isConnected(): boolean;
  on(event: SettingsEvent, handler: (payload: unknown) => void): void;
  off(event: SettingsEvent, handler: (payload: unknown) => void): void;
  readonly connection: HubConnection;
}

/**
 * Realtime client for the `/hubs/settings` hub - deployment-config change
 * notifications. Shares the connection plumbing (auto-reconnect, state-safe
 * `start()`) with every other framework hub via {@link createHubClient}.
 */
export function createSettingsRealtimeClient(opts: SettingsRealtimeClientOptions): SettingsRealtimeClient {
  return createHubClient<SettingsEvent>(opts);
}
