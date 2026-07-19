/**
 * HTTP Response Utilities
 */

import type { ApiResult, PagedList } from '../types/index';
import { HttpError, getApiResultErrorMessage } from '../errors/api-error';

/**
 * Normalize API response from backend.
 *
 * The backend serializes with `JsonNamingPolicy.CamelCase`. This function also
 * handles PascalCase keys for backward compatibility with older endpoints.
 */
export function normalizeApiResult<T>(raw: Record<string, unknown>): ApiResult<T> {
  const code = (raw.code ?? raw.Code ?? 200) as number;
  const succeededRaw = (raw.succeeded ?? raw.Succeeded) as boolean | undefined;
  const success = (raw.success ?? raw.Success ?? (code >= 200 && code < 300)) as boolean;
  const succeeded = succeededRaw ?? success;
  return {
    succeeded: Boolean(succeeded),
    success: Boolean(success),
    code,
    data: (raw.data ?? raw.Data) as T,
    message: (raw.message ?? raw.Message) as string | undefined,
    errorCode: (raw.errorCode ?? raw.ErrorCode) as string | undefined,
    errorDetails: (raw.errorDetails ?? raw.ErrorDetails) as Record<string, unknown> | undefined,
  };
}

/**
 * Check if HTTP result is successful.
 * Handles null/undefined safely.
 */
export function isSuccess<T>(result: ApiResult<T> | null | undefined): boolean {
  if (!result) return false;
  return result.succeeded === true;
}

/**
 * Check if HTTP result has failed.
 * Handles null/undefined safely.
 */
export function isFailed<T>(result: ApiResult<T> | null | undefined): boolean {
  return !isSuccess(result);
}

/**
 * Get error message from HTTP result.
 */
export function getErrorMessage<T>(result: ApiResult<T>): string {
  return getApiResultErrorMessage(result);
}

/**
 * Get the application-specific error code from a result, if any.
 */
export function getErrorCode<T>(result: ApiResult<T>): string | undefined {
  return result.errorCode;
}

/**
 * Unwrap data from a successful result.
 * Throws HttpError if the result represents a failure or if data is null/undefined.
 *
 * Note: The `as T` cast in normalizeApiResult is intentional — callers should validate
 * via schema middleware or use this function which guards against null data.
 */
export function unwrapData<T>(result: ApiResult<T>): T {
  if (isSuccess(result)) {
    if (result.data === undefined || result.data === null) {
      throw new HttpError(result);
    }
    return result.data;
  }
  throw new HttpError(result);
}

/**
 * Assert an `ApiResult` envelope reports success; throw otherwise. No-op for
 * non-envelope values.
 *
 * `HttpClient` never rejects on a business failure — it RESOLVES an
 * `ApiResult { succeeded: false, message }`. A call site that bare-awaits a
 * void endpoint (`await client.delete(...)`) therefore swallows the refusal.
 * Wrap every discarded-result write with this helper so business refusals
 * (403, 409 delete vetoes, validation failures) surface as thrown errors.
 *
 * Unlike {@link unwrapData}, this tolerates a legitimately empty `data`
 * payload on success (void endpoints return no body) — it only reads the
 * success flag. Non-envelope values (already-unwrapped `T`, `undefined`) pass
 * through silently.
 */
export function ensureOk(result: unknown, fallbackMessage = 'Request failed'): void {
  if (
    result &&
    typeof result === 'object' &&
    ('succeeded' in (result as object) || 'success' in (result as object))
  ) {
    const envelope = result as { succeeded?: boolean; success?: boolean; message?: string | null };
    const ok = envelope.succeeded ?? envelope.success;
    if (!ok) throw new Error(envelope.message || fallbackMessage);
  }
}

/**
 * Tolerant result unwrapper: returns `result.data` when `result` looks like an
 * `ApiResult` envelope (has `data` + `succeeded`/`success`), otherwise passes
 * `result` through unchanged.
 *
 * Complements the strict {@link unwrapData} (which throws on failure / null
 * data): use `unwrapResult` when the value may already be unwrapped `T` — e.g.
 * behind `useXxxApi` methods that are inconsistent about returning the full
 * envelope vs. the bare payload. Does NOT assert success; pair with
 * {@link ensureOk} when a failure must throw.
 */
export function unwrapResult<T>(result: ApiResult<T> | T): T {
  if (
    result &&
    typeof result === 'object' &&
    'data' in (result as object) &&
    ('succeeded' in (result as object) || 'success' in (result as object))
  ) {
    return (result as ApiResult<T>).data as T;
  }
  return result as T;
}

/**
 * Extract data from HTTP result (returns null on failure).
 */
export function extractData<T>(result: ApiResult<T>): T | null {
  if (isSuccess(result)) {
    return result.data;
  }
  return null;
}

/**
 * Extract data or throw error.
 * @deprecated Use `unwrapData` instead.
 */
export function extractDataOrThrow<T>(result: ApiResult<T>): T {
  return unwrapData(result);
}

/**
 * Create empty paged list
 */
export function emptyPaged<T>(): ApiResult<PagedList<T>> {
  return {
    succeeded: true,
    success: true,
    code: 200,
    data: {
      pageIndex: 1,
      pageSize: 10,
      totalCount: 0,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
      items: [],
    },
  };
}
