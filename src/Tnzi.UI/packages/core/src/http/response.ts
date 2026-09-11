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
export function normalizeApiResult<T>(raw: Record<string, unknown> | null | undefined): ApiResult<T> {
  // A body that is not an object is not an envelope: `null`, a bare scalar, a
  // raw string. That is still perfectly valid JSON, so reporting it as a parse
  // failure (what dereferencing `raw.code` used to produce) is wrong - carry
  // the body through as the payload instead.
  if (raw === null || raw === undefined || typeof raw !== 'object') {
    return {
      succeeded: true,
      success: true,
      code: 200,
      data: (raw ?? undefined) as T,
    };
  }

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
 *
 * Type predicate: on the true branch `data` is proven present, so
 * `if (isSuccess(res)) { res.data.foo }` type-checks while the same access
 * outside the guard does not. That is the whole point - `data` is optional
 * precisely because a failed envelope carries none.
 */
export function isSuccess<T>(
  result: ApiResult<T> | null | undefined
): result is ApiResult<T> & { data: T } {
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
 * Note: The `as T` cast in normalizeApiResult is intentional - callers should validate
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
 * `HttpClient` never rejects on a business failure - it RESOLVES an
 * `ApiResult { succeeded: false, message }`. A call site that bare-awaits a
 * void endpoint (`await client.delete(...)`) therefore swallows the refusal.
 * Wrap every discarded-result write with this helper so business refusals
 * (403, 409 delete vetoes, validation failures) surface as thrown errors.
 *
 * Unlike {@link unwrapData}, this tolerates a legitimately empty `data`
 * payload on success (void endpoints return no body) - it only reads the
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
 * Unwrap **without checking whether the request succeeded**: returns
 * `result.data` when `result` looks like an `ApiResult` envelope (has `data` +
 * `succeeded`/`success`), otherwise passes `result` through unchanged.
 *
 * @remarks
 * ★★ **The "Unchecked" in the name is the whole point.** A business refusal
 * (400 + a failed envelope) is *resolved* by `HttpClient`, not thrown, so this
 * function turns it into `null` and hands it back as if it were data. On a write
 * that produces a green "saved" toast over a row that was never created. This was
 * a live defect across 16 bridges and 155 call sites before it was swept.
 *
 * Pick by what a failure has to do:
 * - a **write**, or any read whose failure must surface -> {@link unwrapOk}
 * - failure already handled by the caller (a list that renders empty, a lookup
 *   that may legitimately come back missing) -> this function
 * - a value that is definitely an envelope and must never be null -> {@link unwrapData}
 *
 * It exists because `useXxxApi` methods are inconsistent about returning the full
 * envelope versus the bare payload, so callers cannot assume either.
 */
export function unwrapUnchecked<T>(result: ApiResult<T> | T): T {
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
 * @deprecated Renamed to {@link unwrapUnchecked}. The old name read like the
 * default way to unwrap a result, which is exactly the mistake it invites: it
 * does **not** check whether the request succeeded. Use `unwrapOk` for writes,
 * `unwrapUnchecked` when a failure is genuinely handled elsewhere.
 */
export const unwrapResult = unwrapUnchecked;

/**
 * Unwrap a **write** result: assert the envelope reports success, then return
 * its payload (or the bare value when the server answered without an envelope).
 *
 * `HttpClient` resolves a business refusal (400 + failed envelope) instead of
 * rejecting, and {@link unwrapUnchecked} alone turns that refusal into `null`. A bridge
 * that hands `null` back to `useCrudPage.submit` gets a green "saved" toast, a
 * closed form and a refreshed list that does not contain the row - the user's
 * input is gone and nothing says why. Every write path must go through this
 * helper (or call `ensureOk` first); reads whose failure is handled elsewhere
 * may use `unwrapUnchecked`.
 */
export function unwrapOk<T>(result: ApiResult<T> | T, fallbackMessage = 'Request failed'): T {
  ensureOk(result, fallbackMessage);
  return unwrapUnchecked<T>(result);
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
