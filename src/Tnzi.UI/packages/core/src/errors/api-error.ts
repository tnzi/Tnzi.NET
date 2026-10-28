/**
 * @tnzi/core/errors/api-error
 *
 * Unified API/HTTP error models.
 */

import type { ApiResult } from '../types/api';
import { API_ERROR_BRAND, HTTP_ERROR_BRAND, brandError, hasErrorBrand } from './brand';

export interface ApiErrorOptions {
  message: string;
  code?: number;
  errorCode?: string;
  details?: Record<string, unknown>;
}

export interface FailedApiResultOptions {
  message: string;
  code?: number;
  errorCode?: string;
  details?: Record<string, unknown>;
}

export class ApiError extends Error {
  public readonly code?: number;
  public readonly errorCode?: string;
  public readonly details?: Record<string, unknown>;

  constructor(
    options: ApiErrorOptions
  ) {
    super(options.message);
    this.name = 'ApiError';
    this.code = options.code;
    this.errorCode = options.errorCode;
    this.details = options.details;
    brandError(this, API_ERROR_BRAND);
  }
}

export class HttpError extends ApiError {
  /** HTTP status code from the response (e.g. 400, 404, 500). */
  public readonly statusCode: number;

  constructor(result: ApiResult<unknown>) {
    const statusCode = result.code ?? 500;
    super({
      message: getApiResultErrorMessage(result),
      code: statusCode,
      errorCode: result.errorCode,
      details: result.errorDetails,
    });
    this.name = 'HttpError';
    this.statusCode = statusCode;
    brandError(this, HTTP_ERROR_BRAND);
  }
}

export function getApiResultErrorMessage(result: Pick<ApiResult<unknown>, 'message' | 'errorCode' | 'code'>): string {
  if (result.message) return result.message;
  if (result.errorCode) return result.errorCode;
  return `Error ${result.code}`;
}

export function createFailedApiResult<T>(
  options: FailedApiResultOptions
): ApiResult<T> {
  const code = options.code ?? 400;
  return {
    succeeded: false,
    success: false,
    code,
    // No payload on a failure - `ApiResult.data` is optional for exactly this.
    data: undefined,
    message: options.message,
    errorCode: options.errorCode,
    errorDetails: options.details,
  };
}

export function createFailedApiResultFromError<T>(
  error: unknown,
  options?: Omit<FailedApiResultOptions, 'message'>
): ApiResult<T> {
  const message = error instanceof Error ? error.message : String(error);
  return createFailedApiResult<T>({
    message,
    code: options?.code ?? 500,
    errorCode: options?.errorCode,
    details: options?.details,
  });
}

/**
 * Whether `error` is an {@link ApiError} (an {@link HttpError} included).
 *
 * Prefer this over `instanceof`: each `@tnzi/core` sub-path bundles its own copy
 * of the class, so an error thrown by one entry is not an `instanceof` the class
 * exported by another. The guards recognise every copy (see `errors/brand`).
 */
export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError || hasErrorBrand(error, API_ERROR_BRAND);
}

/**
 * Whether `error` is an {@link HttpError}, whichever `@tnzi/core` sub-path created
 * it. Prefer this over `instanceof` for the reason given on {@link isApiError}.
 */
export function isHttpError(error: unknown): error is HttpError {
  return error instanceof HttpError || hasErrorBrand(error, HTTP_ERROR_BRAND);
}

