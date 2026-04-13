/**
 * API Response Types - Aligned with Tnzi.NET backend ApiResult<T>
 */

/**
 * Standard API response wrapper from Tnzi.NET backend.
 *
 * The backend serializes with `JsonNamingPolicy.CamelCase`, producing:
 * `succeeded`, `success`, `code`, `data`, `message`, `errorCode`, `errorDetails`.
 *
 * Use the helper functions `isSuccess()`, `isFailed()`, `normalizeApiResult()`
 * from `@tnzi/core/http` instead of checking fields directly.
 */
export interface ApiResult<T = unknown> {
  /** 操作是否成功 (BaseResult.Succeeded) */
  succeeded: boolean;
  /** HTTP 2xx 计算属性 (ApiResult.Success) */
  success: boolean;
  /** HTTP 状态码 (Result.Code) */
  code: number;
  /** 响应数据 */
  data: T;
  /** 消息 */
  message?: string;
  /** 业务错误码 */
  errorCode?: string;
  /** 错误详情 */
  errorDetails?: Record<string, unknown>;
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
