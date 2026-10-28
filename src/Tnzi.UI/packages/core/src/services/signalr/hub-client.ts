/**
 * @tnzi/core/services/signalr/hub-client
 *
 * Thin generic wrapper around a `@microsoft/signalr` HubConnection, shared by
 * every framework hub client (chat, settings, ...). Each hub only differs by
 * its event-name union, so the connection plumbing lives here once.
 */

import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { readExpiryClaim } from '../identity/session-claims';

/** Backoff for the first attempts after a drop or a failed start (ms). */
const RECONNECT_DELAYS = [0, 2000, 5000, 10000, 30000];
/** After that schedule, retry at this interval for as long as the client is started (ms). */
const RECONNECT_STEADY_DELAY_MS = 30_000;
/**
 * Random spread added to the steady interval, so the clients a redeploy
 * disconnected together do not keep reconnecting in lockstep (ms).
 */
const RECONNECT_JITTER_MS = 5_000;
/** A token this close to its expiry is refreshed before a (re)connect presents it (ms). */
const TOKEN_REFRESH_SKEW_MS = 30_000;

function reconnectDelay(previousRetryCount: number): number {
  return previousRetryCount < RECONNECT_DELAYS.length
    ? RECONNECT_DELAYS[previousRetryCount]!
    : RECONNECT_STEADY_DELAY_MS + Math.floor(Math.random() * RECONNECT_JITTER_MS);
}

export interface HubClientOptions {
  /** Hub URL, e.g. `/hubs/chat` (relative - resolved against origin / vite proxy). */
  url: string;
  /** Returns the current JWT (called on each (re)connect). May be async. */
  accessTokenFactory: () => string | Promise<string>;
  /**
   * Obtains a fresh access token, e.g. `() => httpClient.refreshAccessToken()`.
   *
   * Called before a (re)connect when the token from `accessTokenFactory` has
   * expired (per its `exp` claim). Without it, a page that makes no REST call
   * while the token runs out (a laptop asleep past the token lifetime, a chat
   * left open) reconnects with the expired token, the negotiate answers 401,
   * and realtime never comes back. A `null` result keeps the current token.
   */
  refreshAccessToken?: () => Promise<string | null>;
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

async function resolveAccessToken(opts: HubClientOptions): Promise<string> {
  const token = await opts.accessTokenFactory();
  if (!opts.refreshAccessToken) return token;
  const expiresAt = readExpiryClaim(token);
  if (expiresAt === null || expiresAt - Date.now() > TOKEN_REFRESH_SKEW_MS) return token;
  const fresh = await opts.refreshAccessToken().catch(() => null);
  return fresh || token;
}

/**
 * Build a hub client that stays connected for as long as it is started.
 *
 * Recovery: after a drop the connection keeps reconnecting (the schedule above,
 * then every ~30 s) instead of giving up after the last delay, and a failed
 * `start()` (the hub unreachable at sign-in) is retried in the background on the
 * same schedule. `start()` still rejects with that first failure so the caller
 * knows; `stop()` ends every retry.
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
    .withUrl(opts.url, { accessTokenFactory: () => resolveAccessToken(opts) })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (context) => reconnectDelay(context.previousRetryCount),
    })
    .configureLogging(LogLevel.Warning)
    .build();

  let startPromise: Promise<void> | null = null;
  /** The caller wants the connection up: set by start(), cleared by stop(). */
  let wanted = false;
  let retryTimer: ReturnType<typeof setTimeout> | null = null;
  let retryCount = 0;

  const cancelRetry = (): void => {
    if (retryTimer) clearTimeout(retryTimer);
    retryTimer = null;
  };

  const connect = async (): Promise<void> => {
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
    try {
      await startPromise;
      retryCount = 0;
    } catch (error) {
      scheduleRetry();
      throw error;
    }
  };

  function scheduleRetry(): void {
    if (!wanted || retryTimer) return;
    retryCount += 1;
    retryTimer = setTimeout(() => {
      retryTimer = null;
      if (wanted) void connect().catch(() => undefined);
    }, reconnectDelay(retryCount));
  }

  // The reconnect policy never gives up, so this fires only when the
  // connection closes without reconnecting (the server closed it, or stop()).
  connection.onclose(() => {
    if (wanted) scheduleRetry();
  });

  return {
    connection,
    isConnected: () => connection.state === HubConnectionState.Connected,
    async start(): Promise<void> {
      wanted = true;
      cancelRetry();
      await connect();
    },
    async stop(): Promise<void> {
      wanted = false;
      cancelRetry();
      retryCount = 0;
      await connection.stop();
    },
    on(event, handler) { connection.on(event, handler); },
    off(event, handler) { connection.off(event, handler); },
  };
}
