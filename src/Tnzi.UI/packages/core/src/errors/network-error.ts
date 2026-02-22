/**
 * @tnzi/core/errors/network-error
 *
 * Network-level error classes for transport failures, timeouts, and aborts.
 */

/**
 * Base class for network-level errors (connection failures, DNS errors, etc.).
 * Distinct from ApiError which represents server-returned error responses.
 */
export class NetworkError extends Error {
  public readonly cause?: Error;

  constructor(message: string, options?: { cause?: Error }) {
    super(message);
    this.name = 'NetworkError';
    this.cause = options?.cause;
  }
}

/**
 * Request timed out before receiving a response.
 */
export class TimeoutError extends NetworkError {
  constructor(timeoutMs?: number) {
    super(
      timeoutMs
        ? `Request timed out after ${timeoutMs}ms`
        : 'Request timed out',
    );
    this.name = 'TimeoutError';
  }
}

/**
 * Request was explicitly aborted (e.g. via AbortController).
 */
export class AbortError extends NetworkError {
  constructor() {
    super('Request was aborted');
    this.name = 'AbortError';
  }
}

/** Type guard: check if an error is a NetworkError (or subclass). */
export function isNetworkError(error: unknown): error is NetworkError {
  return error instanceof NetworkError;
}

/** Type guard: check if an error is a TimeoutError. */
export function isTimeoutError(error: unknown): error is TimeoutError {
  return error instanceof TimeoutError;
}

/** Type guard: check if an error is an AbortError. */
export function isAbortError(error: unknown): error is AbortError {
  return error instanceof AbortError;
}
