import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';

/**
 * Payload of the `Settings.Changed` SignalR event, broadcast by the backend
 * (Tnzi.System `SettingChangedEventHandler`) to ALL connected clients whenever a
 * Global-scope setting is written. Carries only the changed key (never the value,
 * so no secret leaks) — clients decide by key prefix whether to hot-refresh the
 * affected config (e.g. `Chat:*` → re-fetch chat config, `Appearance:AdminTheme`
 * → reload the global theme) without a manual page reload.
 */
export interface SettingsChangedPayload {
  /** The changed setting key, e.g. `Chat:AllowInvisible` or `Appearance:AdminTheme`. */
  key: string;
  /** True when the setting was removed (reverted to its default). */
  isRemoval: boolean;
}

export interface SettingsRealtimeClientOptions {
  /** Hub URL, e.g. `/hubs/settings` (relative — resolved against origin / vite proxy). */
  url: string;
  /** Returns the current JWT (called on each (re)connect). */
  accessTokenFactory: () => string;
}

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
 * Generic realtime client for the `/hubs/settings` hub — mirrors
 * `createChatSignalRClient` but for deployment-config change notifications.
 * Auto-reconnects; the token is read on every (re)connect via `accessTokenFactory`.
 */
export function createSettingsRealtimeClient(opts: SettingsRealtimeClientOptions): SettingsRealtimeClient {
  const connection = new HubConnectionBuilder()
    .withUrl(opts.url, { accessTokenFactory: opts.accessTokenFactory })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  return {
    connection,
    isConnected: () => (connection.state as unknown as string) === 'Connected',
    async start() { if ((connection.state as unknown as string) !== 'Connected') await connection.start(); },
    async stop() { await connection.stop(); },
    on(event, handler) { connection.on(event, handler); },
    off(event, handler) { connection.off(event, handler); },
  };
}
