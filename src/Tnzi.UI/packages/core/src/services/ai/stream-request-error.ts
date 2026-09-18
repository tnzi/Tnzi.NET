/**
 * A streaming endpoint answered with a non-2xx status before any frame.
 *
 * Shared by every raw-`fetch` SSE transport in this package (`streamChat`,
 * `streamCliRun`). Those calls bypass `HttpClient`, so the client's 401
 * refresh-and-retry never sees them - the caller has to run that dance itself
 * (see `HttpClient.refreshAccessToken` / `reportUnauthorized`), and it needs
 * the HTTP status to tell an expired bearer token apart from a server failure
 * without parsing the message text. One base class means one `instanceof`
 * branch covers both transports.
 */
export class StreamRequestError extends Error {
  readonly status: number;

  constructor(status: number, detail: string, label = 'Stream request') {
    super(`${label} failed: ${status} ${detail}`);
    this.name = 'StreamRequestError';
    this.status = status;
  }
}
