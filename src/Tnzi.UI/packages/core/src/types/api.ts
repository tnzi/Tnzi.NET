/**
 * API Response Types - Aligned with Tnzi.NET backend ApiResult<T>
 */

/**
 * Standard API response wrapper from Tnzi.NET backend.
 *
 * Both PascalCase and camelCase properties exist for backward compatibility:
 * - The backend serializes with PascalCase (`Code`, `Success`) by default.
 * - The frontend normalizes to camelCase (`code`, `succeeded`) for idiomatic JS.
 * - Both sets are kept in sync so consumers can use either convention.
 *
 * Use the helper functions `isSuccess()`, `isFailed()`, `normalizeApiResult()`
 * from `@tnzi/core/http` instead of checking fields directly.
 */
export interface ApiResult<T = unknown> {
  /** Whether the operation succeeded (camelCase alias of `Success`). */
  succeeded: boolean;
  /** Human-readable message. */
  message?: string;
  /** HTTP status code (camelCase alias of `Code`). */
  code?: number;
  /** Application-specific error code. */
  errorCode?: string;
  /** Additional error details. */
  errorDetails?: Record<string, unknown>;
  /** Response data payload. */
  data: T;
  /** @deprecated Use `code` instead. Kept for backward compatibility with PascalCase backends. */
  Code: number;
  /** @deprecated Use `succeeded` instead. Kept for backward compatibility with PascalCase backends. */
  Success: boolean;
}

/**
 * Simplified API result without data payload
 */
export type ApiResultEmpty = ApiResult<void>;

/**
 * HTTP Method types
 */
export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

/**
 * Request configuration options
 */
export interface RequestOptions {
  /** Request headers */
  headers?: Record<string, string>;
  /** Query parameters */
  params?: Record<string, unknown> | object;
  /** Optional request body (used by some DELETE endpoints) */
  body?: unknown;
  /** Request timeout in milliseconds */
  timeout?: number;
  /** Abort signal for cancellation */
  signal?: AbortSignal;
  /** Include credentials (cookies) */
  withCredentials?: boolean;
}

/**
 * File upload progress callback
 */
export type UploadProgressCallback = (progress: number, loaded: number, total: number) => void;

/**
 * File upload options
 */
export interface UploadOptions extends RequestOptions {
  /** Progress callback */
  onProgress?: UploadProgressCallback;
  /** Additional form data fields */
  additionalData?: Record<string, string | Blob>;
}
