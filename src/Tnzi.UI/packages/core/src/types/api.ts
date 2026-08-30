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
  /**
   * 响应数据。
   *
   * Optional on purpose: a failed envelope carries no payload (the client fills
   * it with `undefined`), and void endpoints return none on success either.
   * Narrow with `isSuccess(result)` from `@tnzi/core/http` - it is a type
   * predicate that proves `data` is present, so `result.data` is only reachable
   * once success has actually been checked.
   */
  data?: T;
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
  /** Request timeout in milliseconds (overrides the client config; 0 disables the timeout) */
  timeout?: number;
  /** Abort signal for cancellation */
  signal?: AbortSignal;
  /** Include credentials (cookies) */
  withCredentials?: boolean;
  /**
   * Marks a request that belongs to the auth flow itself (login, token
   * refresh, logout). A 401 on such a request is returned to the caller
   * as-is: it never triggers the client's token-refresh-and-retry logic
   * nor the `onUnauthorized` callback. Without this, the refresh/logout
   * calls issued DURING a refresh re-enter the refresh mutex and stall
   * every queued request until the refresh timeout elapses.
   */
  skipAuthRefresh?: boolean;
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

/**
 * Reorder request body, shared by every module's `reorder` endpoint.
 *
 * `ids` is the visible order, not necessarily the full set: the server merges
 * it back by slot, so records outside the submitted positions keep theirs. The
 * scope of a reorder (parent folder, setting group, ...) travels as a query
 * parameter on the endpoint, not in this body - it locates the sequence rather
 * than being part of the payload.
 */
export interface ReorderRequest {
  /** Record ids in their new order. No duplicates; all must be inside the scope. */
  ids: string[];
}
