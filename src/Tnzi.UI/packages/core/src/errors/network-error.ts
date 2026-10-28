/**
 * @tnzi/core/errors/network-error
 *
 * Network-level error classes for transport failures, timeouts, and aborts.
 */

import {
  ABORT_ERROR_BRAND,
  NETWORK_ERROR_BRAND,
  TIMEOUT_ERROR_BRAND,
  brandError,
  hasErrorBrand,
} from './brand';

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
    brandError(this, NETWORK_ERROR_BRAND);
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
    brandError(this, TIMEOUT_ERROR_BRAND);
  }
}

/**
 * Request was explicitly aborted (e.g. via AbortController).
 */
export class AbortError extends NetworkError {
  constructor() {
    super('Request was aborted');
    this.name = 'AbortError';
    brandError(this, ABORT_ERROR_BRAND);
  }
}

/**
 * Type guard: check if an error is a NetworkError (or subclass), whichever
 * `@tnzi/core` sub-path created it (each bundles its own copy of the class, so
 * `instanceof` alone misses errors from another entry; see `errors/brand`).
 */
export function isNetworkError(error: unknown): error is NetworkError {
  return error instanceof NetworkError || hasErrorBrand(error, NETWORK_ERROR_BRAND);
}

/** Type guard: check if an error is a TimeoutError, from any `@tnzi/core` sub-path. */
export function isTimeoutError(error: unknown): error is TimeoutError {
  return error instanceof TimeoutError || hasErrorBrand(error, TIMEOUT_ERROR_BRAND);
}

/** Type guard: check if an error is an AbortError, from any `@tnzi/core` sub-path. */
export function isAbortError(error: unknown): error is AbortError {
  return error instanceof AbortError || hasErrorBrand(error, ABORT_ERROR_BRAND);
}
