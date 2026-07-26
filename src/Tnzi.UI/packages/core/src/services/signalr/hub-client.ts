/**
 * @tnzi/core/services/signalr/hub-client
 *
 * Thin generic wrapper around a `@microsoft/signalr` HubConnection, shared by
 * every framework hub client (chat, settings, ...). Each hub only differs by
 * its event-name union, so the connection plumbing lives here once.
 */

import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';

/** Reconnect backoff shared by all framework hubs (ms). */
const RECONNECT_DELAYS = [0, 2000, 5000, 10000, 30000];

export interface HubClientOptions {
  /** Hub URL, e.g. `/hubs/chat` (relative - resolved against origin / vite proxy). */
  url: string;
  /** Returns the current JWT (called on each (re)connect). */
  accessTokenFactory: () => string;
}

/**
 * A started/stoppable hub connection with typed `on`/`off`.
 */
export interface HubClient<TEvent extends string> {
  start(): Promise<void>;
  stop(): Promise<void>;
  isConnected(): boolean;
  on(event: TEvent, handler: (payload: unknown) => void): void;
  off(event: TEvent, handler: (payload: unknown) => void): void;
  readonly connection: HubConnection;
}

/**
 * Build a hub client with auto-reconnect and a safe `start()`.
 *
 * `start()` is guarded on `Disconnected`, NOT on "not Connected":
 * `HubConnectionState` has five values, and calling `connection.start()` while
 * the connection is Connecting / Reconnecting / Disconnecting throws
 * "Cannot start a HubConnection that is not in the 'Disconnected' state.".
 * Overlapping callers additionally share one in-flight start promise, so a
 * component that mounts twice in a tick cannot race itself into that error.
 */
export function createHubClient<TEvent extends string>(opts: HubClientOptions): HubClient<TEvent> {
  const connection = new HubConnectionBuilder()
    .withUrl(opts.url, { accessTokenFactory: opts.accessTokenFactory })
    .withAutomaticReconnect(RECONNECT_DELAYS)
    .configureLogging(LogLevel.Warning)
    .build();

  let startPromise: Promise<void> | null = null;

  return {
    connection,
    isConnected: () => connection.state === HubConnectionState.Connected,
    async start(): Promise<void> {
      if (connection.state === HubConnectionState.Connected) return;
      // Connecting / Reconnecting: the connection is already on its way, and
      // starting again would throw. Join the pending attempt when there is one.
      if (connection.state !== HubConnectionState.Disconnected) {
        await startPromise;
        return;
      }
      startPromise ??= connection.start().finally(() => {
        startPromise = null;
      });
      await startPromise;
    },
    async stop(): Promise<void> {
      await connection.stop();
    },
    on(event, handler) { connection.on(event, handler); },
    off(event, handler) { connection.off(event, handler); },
  };
}
