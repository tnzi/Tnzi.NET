/**
 * HTTP Response Utilities
 */

import type { ApiResult, PagedList } from '../types/index';
import { HttpError, getApiResultErrorMessage } from '../errors/api-error';

/**
 * Normalize API response from backend.
 *
 * The canonical (primary) format uses **camelCase** properties: `succeeded`, `code`, `data`, `message`.
 * The **PascalCase** aliases (`Success`, `Code`, `Data`, `Message`) are populated solely for
 * backward compatibility with older code that reads PascalCase fields.
 * New code should always consume the camelCase properties.
 *
 * Backend may return either convention; this function merges both into a unified ApiResult.
 */
export function normalizeApiResult<T>(raw: Record<string, unknown>): ApiResult<T> {
  const code = (raw.Code ?? raw.code ?? 200) as number;
  const succeeded = raw.succeeded ?? raw.success ?? raw.Success ?? (code >= 200 && code < 300);
  return {
    succeeded: Boolean(succeeded),
    Success: Boolean(succeeded),
    code,
    Code: code,
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
  return result.succeeded === true || result.Success === true;
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
    data: {
      pageIndex: 1,
      pageSize: 10,
      totalCount: 0,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
      items: [],
    },
    code: 200,
    Code: 200,
    Success: true,
  };
}
