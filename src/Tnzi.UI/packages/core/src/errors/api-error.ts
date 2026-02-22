/**
 * @tnzi/core/errors/api-error
 *
 * Unified API/HTTP error models.
 */

import type { ApiResult } from '../types/api';

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
  }
}

export class HttpError extends ApiError {
  /** HTTP status code from the response (e.g. 400, 404, 500). */
  public readonly statusCode: number;

  constructor(result: ApiResult<unknown>) {
    const statusCode = result.Code ?? result.code ?? 500;
    super({
      message: getApiResultErrorMessage(result),
      code: statusCode,
      errorCode: result.errorCode,
      details: result.errorDetails,
    });
    this.name = 'HttpError';
    this.statusCode = statusCode;
  }
}

export function getApiResultErrorMessage(result: Pick<ApiResult<unknown>, 'message' | 'errorCode' | 'Code'>): string {
  if (result.message) return result.message;
  if (result.errorCode) return result.errorCode;
  return `Error ${result.Code}`;
}

export function createFailedApiResult<T>(
  options: FailedApiResultOptions
): ApiResult<T> {
  const code = options.code ?? 400;
  return {
    succeeded: false,
    data: undefined as T,
    code,
    Code: code,
    Success: false,
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

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export function isHttpError(error: unknown): error is HttpError {
  return error instanceof HttpError;
}

