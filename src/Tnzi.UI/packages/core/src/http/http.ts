/**
 * Http Client - HTTP client for Tnzi.NET backend
 */

import type { ApiResult, HttpMethod, RequestOptions, UploadOptions } from '../types/api';
import { createFailedApiResult, createFailedApiResultFromError } from '../errors/api-error';
import { TimeoutError } from '../errors/network-error';
import { useLogger } from '../adapters/logger';
import { normalizeApiResult } from './response';
import type { HttpResponseContext, HttpResponseMiddleware } from './middleware';

/**
 * Default request timeout in milliseconds.
 * Applies to JSON API requests, uploads, and the token refresh call.
 * `download()` is exempt (large files may take longer); SSE streaming
 * (`streamChat`) does not go through HttpClient and is unaffected.
 */
export const DEFAULT_REQUEST_TIMEOUT = 30000;

/**
 * `ApiResult.errorCode` value set when a request fails because the
 * client-side timeout elapsed (distinguishes timeouts from network errors
 * and caller-initiated aborts).
 */
export const REQUEST_TIMEOUT_ERROR_CODE = 'REQUEST_TIMEOUT';

/** Detect the DOM AbortError raised by fetch when its signal aborts. */
function isDomAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError';
}

/**
 * Retry configuration for failed requests
 */
export interface RetryConfig {
  /** Maximum number of retries (default: 0 = no retry) */
  maxRetries?: number;
  /** Base delay in ms between retries (default: 1000) */
  baseDelay?: number;
  /** HTTP status codes to retry on (default: [408, 429, 500, 502, 503, 504]) */
  retryableStatuses?: number[];
  /** Whether to use exponential backoff (default: true) */
  exponentialBackoff?: boolean;
}

/**
 * Http Client configuration
 */
export interface HttpClientConfig {
  /** Base URL for HTTP requests */
  baseUrl: string;
  /** Default headers */
  defaultHeaders?: Record<string, string>;
  /**
   * Request timeout in milliseconds (default: 30000). Set to 0 to disable.
   * Applies to JSON API requests, uploads, and the token refresh call.
   * `download()` is exempt unless an explicit per-request timeout is given;
   * SSE streaming (`streamChat`) does not go through HttpClient.
   */
  timeout?: number;
  /**
   * Token refresh function. When configured and a 401 response is received,
   * the client will call this function to obtain a new token and automatically
   * retry the failed request once. Concurrent 401 responses share the same
   * refresh call (mutex pattern).
   */
  refreshTokenFn?: () => Promise<string>;
  /** Unauthorized callback (401). Called when no refreshTokenFn is configured, or when refresh fails. */
  onUnauthorized?: () => void;
  /**
   * Token expired callback.
   * @deprecated Use `onUnauthorized` instead. A 401 response already implies token expiration.
   */
  onTokenExpired?: () => void;
  /** Request interceptor */
  requestInterceptor?: (config: RequestConfig) => RequestConfig | Promise<RequestConfig>;
  /** Response interceptor */
  responseInterceptor?: <T>(response: ApiResult<T>) => ApiResult<T> | Promise<ApiResult<T>>;
  /** Response middleware pipeline */
  responseMiddlewares?: HttpResponseMiddleware[];
  /** Error interceptor */
  errorInterceptor?: (error: Error) => void;
  /** Retry configuration for failed requests */
  retry?: RetryConfig;
  /**
   * Whether to deduplicate concurrent identical GET requests (default: true).
   * When enabled, multiple simultaneous GET requests to the same URL will share
   * a single network call instead of making redundant requests.
   */
  deduplicateGets?: boolean;
}

/**
 * Internal request config
 */
interface RequestConfig {
  url: string;
  method: HttpMethod;
  headers?: Record<string, string>;
  params?: Record<string, unknown> | object;
  body?: unknown;
  timeout?: number;
  signal?: AbortSignal;
  withCredentials?: boolean;
  /** Auth-flow request: return 401 as-is, no refresh-retry, no onUnauthorized. */
  skipAuthRefresh?: boolean;
}

/** Default HTTP status codes that are safe to retry */
const DEFAULT_RETRYABLE_STATUSES = [408, 429, 500, 502, 503, 504];

/**
 * HttpClient class
 */
export class HttpClient {
  private readonly config: HttpClientConfig;
  private accessToken: string | null = null;
  /** Guards against duplicate 401 callbacks from concurrent requests */
  private unauthorizedHandled = false;
  /** Mutex: pending token refresh promise for deduplication */
  private _refreshPromise: Promise<string> | null = null;
  /** Additional unauthorized subscribers (multicast alongside config.onUnauthorized) */
  private readonly _unauthorizedListeners = new Set<() => void>();
  /** Map of inflight GET requests for deduplication */
  private _inflightGets = new Map<string, Promise<ApiResult<unknown>>>();

  constructor(config: HttpClientConfig) {
    this.config = {
      defaultHeaders: {
        'Content-Type': 'application/json',
      },
      ...config,
      // Normalize after the spread so an explicitly-passed `timeout: undefined`
      // cannot silently disable the default. Use `timeout: 0` to disable.
      timeout: config.timeout ?? DEFAULT_REQUEST_TIMEOUT,
    };
  }

  /**
   * Set access token. Resets the 401 deduplication guard so subsequent
   * unauthorized responses will trigger the callback again.
   */
  setAccessToken(token: string | null): void {
    this.accessToken = token;
    this.unauthorizedHandled = false;
  }

  /**
   * Get access token
   */
  getAccessToken(): string | null {
    return this.accessToken;
  }

  /**
   * GET request
   */
  async get<T>(url: string, options?: RequestOptions): Promise<ApiResult<T>> {
    return this.request<T>('GET', url, options);
  }

  /**
   * POST request
   */
  async post<T>(url: string, data?: unknown, options?: RequestOptions): Promise<ApiResult<T>> {
    return this.request<T>('POST', url, { ...options, body: data });
  }

  /**
   * PUT request
   */
  async put<T>(url: string, data?: unknown, options?: RequestOptions): Promise<ApiResult<T>> {
    return this.request<T>('PUT', url, { ...options, body: data });
  }

  /**
   * PATCH request
   */
  async patch<T>(url: string, data?: unknown, options?: RequestOptions): Promise<ApiResult<T>> {
    return this.request<T>('PATCH', url, { ...options, body: data });
  }

  /**
   * DELETE request
   */
  async delete<T>(url: string, options?: RequestOptions): Promise<ApiResult<T>> {
    return this.request<T>('DELETE', url, { ...options, body: options?.body });
  }

  /**
   * Upload file
   */
  async upload<T>(url: string, file: File, options?: UploadOptions): Promise<ApiResult<T>> {
    const formData = new FormData();
    formData.append('file', file);

    if (options?.additionalData) {
      for (const [key, value] of Object.entries(options.additionalData)) {
        formData.append(key, value);
      }
    }

    return this.uploadFormData<T>(url, formData, options);
  }

  /**
   * Upload form data with progress tracking.
   * Always resolves with ApiResult<T> instead of rejecting on errors.
   */
  async uploadFormData<T>(
    url: string,
    formData: FormData,
    options?: UploadOptions
  ): Promise<ApiResult<T>> {
    return new Promise((resolve) => {
      const xhr = new XMLHttpRequest();

      const fullUrl = this.buildUrl(url, options?.params);
      const timeoutMs = options?.timeout ?? this.config.timeout ?? 0;

      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable && options?.onProgress) {
          const progress = Math.round((event.loaded / event.total) * 100);
          options.onProgress(progress, event.loaded, event.total);
        }
      };

      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const response = normalizeApiResult<T>(JSON.parse(xhr.responseText));
            resolve(response);
          } catch {
            resolve(createFailedApiResultFromError<T>(
              new Error('Invalid JSON response'),
              { code: xhr.status }
            ));
          }
        } else {
          // Try to parse response as ApiResult, fallback to createFailedApiResult
          try {
            const raw = JSON.parse(xhr.responseText);
            if (raw && typeof raw === 'object' && ('Code' in raw || 'code' in raw)) {
              resolve(normalizeApiResult<T>(raw));
              return;
            }
          } catch {
            // Response is not valid JSON or not an ApiResult, create a failed result
          }
          resolve(createFailedApiResult<T>({
            message: `Upload failed: ${xhr.status} ${xhr.statusText}`,
            code: xhr.status,
          }));
        }
      };

      xhr.onerror = () => {
        resolve(createFailedApiResultFromError<T>(
          new Error('Network error during upload'),
        ));
      };

      xhr.ontimeout = () => {
        resolve(createFailedApiResult<T>({
          message: `Upload timed out after ${timeoutMs}ms`,
          code: 408,
          errorCode: REQUEST_TIMEOUT_ERROR_CODE,
        }));
      };

      xhr.open('POST', fullUrl);

      // Set authorization header
      if (this.accessToken) {
        xhr.setRequestHeader('Authorization', `Bearer ${this.accessToken}`);
      }

      // Set custom headers (excluding Content-Type for FormData)
      if (this.config.defaultHeaders) {
        for (const [key, value] of Object.entries(this.config.defaultHeaders)) {
          if (key.toLowerCase() !== 'content-type') {
            xhr.setRequestHeader(key, value);
          }
        }
      }

      if (options?.headers) {
        for (const [key, value] of Object.entries(options.headers)) {
          xhr.setRequestHeader(key, value);
        }
      }

      if (timeoutMs > 0) {
        xhr.timeout = timeoutMs;
      }

      if (options?.withCredentials) {
        xhr.withCredentials = true;
      }

      xhr.send(formData);
    });
  }

  /**
   * Download file. Returns ApiResult<Blob> for consistent error handling.
   *
   * Downloads are exempt from the default request timeout — large files can
   * legitimately take longer than {@link DEFAULT_REQUEST_TIMEOUT}. An explicit
   * per-request `timeout` is still honored when provided.
   *
   * Defaults to GET; pass `method: 'POST'` with `body` for export endpoints
   * that take a query payload (e.g. filtered CSV exports).
   */
  async download(url: string, options?: RequestOptions & { method?: 'GET' | 'POST' }): Promise<ApiResult<Blob>> {
    const timeoutMs = options?.timeout ?? 0;
    const timeout = this.createTimeoutSignal(timeoutMs, options?.signal);
    const method = options?.method ?? 'GET';
    try {
      const fullUrl = this.buildUrl(url, options?.params);
      const headers = this.buildHeaders(options?.headers) as Record<string, string>;
      const hasBody = options?.body !== undefined && options.body !== null;
      if (hasBody && !headers['Content-Type']) {
        headers['Content-Type'] = 'application/json';
      }

      const response = await fetch(fullUrl, {
        method,
        headers,
        body: hasBody ? JSON.stringify(options!.body) : undefined,
        signal: timeout.signal,
        credentials: options?.withCredentials ? 'include' : 'same-origin',
      });

      if (!response.ok) {
        // Failed downloads carry the server's ApiResult envelope (or plain text);
        // surface its message so actionable guidance (e.g. "narrow the date
        // range") reaches the caller instead of a bare status line.
        let message = `Download failed: ${response.status} ${response.statusText}`;
        try {
          const text = await response.text();
          if (text) {
            try {
              const body = JSON.parse(text) as { message?: string };
              if (body?.message) message = body.message;
            } catch {
              message = text.slice(0, 500);
            }
          }
        } catch {
          // keep the generic message when the body is unreadable
        }
        return createFailedApiResult<Blob>({
          message,
          code: response.status,
        });
      }

      const blob = await response.blob();
      return {
        succeeded: true,
        success: true,
        code: 200,
        data: blob,
      } as ApiResult<Blob>;
    } catch (error) {
      if (timeout.timedOut() && isDomAbortError(error)) {
        return createFailedApiResult<Blob>({
          message: `${new TimeoutError(timeoutMs).message}: ${method} ${url}`,
          code: 408,
          errorCode: REQUEST_TIMEOUT_ERROR_CODE,
        });
      }
      return createFailedApiResultFromError<Blob>(error);
    } finally {
      timeout.dispose();
    }
  }

  /**
   * Resolve relative URL to absolute URL using configured baseUrl.
   */
  resolveUrl(url: string, params?: Record<string, unknown> | object): string {
    return this.buildUrl(url, params);
  }

  /**
   * Core request method with GET deduplication, retry, and 401 refresh support.
   */
  private async request<T>(
    method: RequestConfig['method'],
    url: string,
    options?: RequestOptions & { body?: unknown }
  ): Promise<ApiResult<T>> {
    let config: RequestConfig = {
      url,
      method,
      headers: options?.headers,
      params: options?.params,
      body: options?.body,
      timeout: options?.timeout ?? this.config.timeout,
      signal: options?.signal,
      withCredentials: options?.withCredentials,
      skipAuthRefresh: options?.skipAuthRefresh,
    };

    // Apply request interceptor
    if (this.config.requestInterceptor) {
      config = await this.config.requestInterceptor(config);
    }

    // GET deduplication: reuse inflight promise for identical GET URLs
    if (method === 'GET' && this.config.deduplicateGets !== false) {
      const dedupKey = this.buildUrl(config.url, config.params);
      const inflight = this._inflightGets.get(dedupKey);
      if (inflight) {
        // Return shallow clone to prevent shared mutation across callers
        return inflight.then(r => ({ ...r })) as Promise<ApiResult<T>>;
      }

      const promise = this.executeWithRetry<T>(config, false);
      this._inflightGets.set(dedupKey, promise as Promise<ApiResult<unknown>>);
      promise.finally(() => this._inflightGets.delete(dedupKey));
      return promise;
    }

    return this.executeWithRetry<T>(config, false);
  }

  /**
   * Execute request with retry logic and 401 refresh support.
   * @param isRetryAfterRefresh - true if this is a retry after token refresh (prevents infinite loop)
   */
  private async executeWithRetry<T>(config: RequestConfig, isRetryAfterRefresh: boolean): Promise<ApiResult<T>> {
    const retryConfig = this.config.retry;
    const maxRetries = retryConfig?.maxRetries ?? 0;
    const baseDelay = retryConfig?.baseDelay ?? 1000;
    const retryableStatuses = retryConfig?.retryableStatuses ?? DEFAULT_RETRYABLE_STATUSES;
    const exponentialBackoff = retryConfig?.exponentialBackoff !== false;

    let lastResult: ApiResult<T> | undefined;

    for (let attempt = 0; attempt <= maxRetries; attempt++) {
      // Wait before retry (skip delay on first attempt)
      if (attempt > 0) {
        const delay = exponentialBackoff
          ? baseDelay * Math.pow(2, attempt - 1)
          : baseDelay;
        await this.sleep(delay);
      }

      lastResult = await this.executeRequest<T>(config);

      // Handle 401 with auto-refresh (only if not already a retry after
      // refresh, and never for auth-flow requests themselves: the refresh
      // and logout calls issued during a refresh cycle would otherwise
      // re-enter the refresh mutex and deadlock until its timeout).
      if (lastResult.code === 401 && !isRetryAfterRefresh && !config.skipAuthRefresh) {
        const refreshResult = await this.tryRefreshAndRetry<T>(config);
        if (refreshResult && refreshResult.code !== 401) {
          return refreshResult;
        }
        // Either refresh threw (refreshTokenFn rejected → tryRefreshAndRetry
        // already called notifyUnauthorized), or refresh "succeeded" but the
        // retry still 401'd (stale token / backend revoked session).
        // In the second case we must notify here too — otherwise the only
        // signal the consumer's session-expired handler ever sees is the
        // first scenario, and a backend-side revoke would leave the user
        // stuck on an API-error loop without redirect.
        if (refreshResult?.code === 401) {
          this.notifyUnauthorized();
        }
        return refreshResult ?? lastResult;
      }

      // Check if we should retry on other status codes
      const statusCode = lastResult.code;
      if (attempt < maxRetries && retryableStatuses.includes(statusCode)) {
        continue;
      }

      return lastResult;
    }

    // Should not reach here, but return last result as safety
    return lastResult!;
  }

  /**
   * Try to refresh token and retry the request.
   * Uses mutex pattern: concurrent 401s share the same refresh promise.
   * Returns the retry result on success, or null if refresh is not available or failed.
   */
  private async tryRefreshAndRetry<T>(config: RequestConfig): Promise<ApiResult<T> | null> {
    if (!this.config.refreshTokenFn) {
      this.notifyUnauthorized();
      return null;
    }

    try {
      // Mutex: if a refresh is already in progress, wait for it.
      // The refresh call is wrapped with a timeout so a refreshTokenFn that
      // never settles cannot deadlock the mutex — without it, every queued
      // request would await a forever-pending promise and freeze the app.
      if (!this._refreshPromise) {
        this._refreshPromise = this.withRefreshTimeout(this.config.refreshTokenFn());
      }
      const newToken = await this._refreshPromise;
      this.setAccessToken(newToken);

      // Retry the original request once with new token
      return await this.executeWithRetry<T>(config, true);
    } catch {
      // Refresh failed, threw, or timed out — waiters fall back to the
      // original 401 result (executeWithRetry returns `lastResult` on null).
      this.notifyUnauthorized();
      return null;
    } finally {
      // Reset the mutex so subsequent requests can attempt a fresh refresh.
      this._refreshPromise = null;
    }
  }

  /**
   * Wrap the consumer-provided refresh promise with the client timeout so a
   * `refreshTokenFn()` that never settles cannot permanently block the
   * refresh mutex. Follows the instance `timeout` (0 disables, like requests).
   */
  private withRefreshTimeout(promise: Promise<string>): Promise<string> {
    const timeoutMs = this.config.timeout ?? DEFAULT_REQUEST_TIMEOUT;
    if (timeoutMs <= 0) {
      return promise;
    }
    return new Promise<string>((resolve, reject) => {
      const timer = setTimeout(() => reject(new TimeoutError(timeoutMs)), timeoutMs);
      // Attach handlers to the original promise so a late settlement after
      // the timeout neither leaks the timer nor raises an unhandled rejection.
      promise.then(
        (value) => {
          clearTimeout(timer);
          resolve(value);
        },
        (error) => {
          clearTimeout(timer);
          reject(error);
        },
      );
    });
  }

  /**
   * Subscribe to session-expired notifications in ADDITION to the
   * config-level `onUnauthorized` callback. Lets framework layers (e.g.
   * `@tnzi/ui-admin`'s built-in login redirect) react to an unrecoverable
   * 401 without displacing the consumer's own handler. Listeners share the
   * same dedup guard as `onUnauthorized` (once per auth cycle) and fire
   * after it. Returns an unsubscribe function.
   */
  addUnauthorizedListener(listener: () => void): () => void {
    this._unauthorizedListeners.add(listener);
    return () => this._unauthorizedListeners.delete(listener);
  }

  /**
   * Trigger the unauthorized handler (deduplicated — only fires once per auth cycle).
   */
  private notifyUnauthorized(): void {
    if (this.unauthorizedHandled) {
      return;
    }
    this.unauthorizedHandled = true;
    const handler = this.config.onUnauthorized ?? this.config.onTokenExpired;
    // Isolate each callback so one throwing handler cannot silence the rest
    // (the redirect listener must still run when the consumer handler throws).
    try {
      handler?.();
    } catch (error) {
      useLogger().error('onUnauthorized handler threw:', error);
    }
    for (const listener of this._unauthorizedListeners) {
      try {
        listener();
      } catch (error) {
        useLogger().error('Unauthorized listener threw:', error);
      }
    }
  }

  /**
   * Execute a single HTTP request (no retry logic).
   * Enforces the request timeout (per-request override > instance config >
   * {@link DEFAULT_REQUEST_TIMEOUT}; 0 disables) so a hung connection always
   * settles instead of leaving callers awaiting forever.
   */
  private async executeRequest<T>(config: RequestConfig): Promise<ApiResult<T>> {
    const timeoutMs = config.timeout ?? this.config.timeout ?? DEFAULT_REQUEST_TIMEOUT;
    const timeout = this.createTimeoutSignal(timeoutMs, config.signal);

    try {
      const fullUrl = this.buildUrl(config.url, config.params);
      const isFormData = config.body instanceof FormData;
      const headers = this.buildHeaders(config.headers);
      if (isFormData) {
        // Let the browser set multipart/form-data with the correct boundary;
        // a forced application/json Content-Type would corrupt the upload.
        delete (headers as Record<string, string>)['Content-Type'];
        delete (headers as Record<string, string>)['content-type'];
      }

      const response = await fetch(fullUrl, {
        method: config.method,
        headers,
        body: isFormData
          ? (config.body as FormData)
          : config.body
            ? JSON.stringify(config.body)
            : undefined,
        signal: timeout.signal,
        credentials: config.withCredentials ? 'include' : 'same-origin',
      });

      let data: ApiResult<T>;
      try {
        data = normalizeApiResult<T>(await response.json());
      } catch (parseError) {
        // An abort during body read (timeout or caller cancellation) is not
        // a JSON problem — rethrow and let the outer catch classify it.
        if (isDomAbortError(parseError)) {
          throw parseError;
        }
        // Non-JSON response (e.g., 502/503 HTML pages)
        if (!response.ok) {
          data = createFailedApiResult<T>({
            message: `HTTP ${response.status} ${response.statusText}`,
            code: response.status,
          });
        } else {
          data = createFailedApiResult<T>({
            message: 'Invalid JSON response',
            code: response.status,
          });
        }
      }

      // Apply response interceptor
      if (this.config.responseInterceptor) {
        data = await this.config.responseInterceptor(data);
      }

      // Apply response middlewares
      if (this.config.responseMiddlewares?.length) {
        const context: HttpResponseContext = {
          method: config.method,
          url: config.url,
          fullUrl,
          status: response.status,
        };
        for (const middleware of this.config.responseMiddlewares) {
          data = await middleware(data, context);
        }
      }

      return data;
    } catch (error) {
      // Classify client-side timeouts distinctly from network errors and
      // caller-initiated aborts so consumers can react accordingly.
      if (timeout.timedOut() && isDomAbortError(error)) {
        const timeoutError = new TimeoutError(timeoutMs);
        this.config.errorInterceptor?.(timeoutError);
        return createFailedApiResult<T>({
          message: `${timeoutError.message}: ${config.method} ${config.url}`,
          code: 408,
          errorCode: REQUEST_TIMEOUT_ERROR_CODE,
        });
      }

      // Apply error interceptor
      if (this.config.errorInterceptor) {
        this.config.errorInterceptor(error as Error);
      }

      // Return normalized failed result
      return createFailedApiResultFromError<T>(error);
    } finally {
      // Always clear the pending timer — previously it leaked whenever
      // fetch itself rejected (network error / abort before response).
      timeout.dispose();
    }
  }

  /**
   * Create an abort signal that fires after `timeoutMs`, merged with an
   * optional caller-provided signal. The returned `dispose()` must be called
   * once the request settles to avoid timer leaks; `timedOut()` reports
   * whether the abort was caused by the timeout (vs caller cancellation).
   * A `timeoutMs` of 0 (or less) disables the timeout entirely and passes
   * the caller signal through untouched.
   */
  private createTimeoutSignal(timeoutMs: number, userSignal?: AbortSignal): {
    signal: AbortSignal | undefined;
    timedOut: () => boolean;
    dispose: () => void;
  } {
    if (timeoutMs <= 0) {
      return { signal: userSignal, timedOut: () => false, dispose: () => {} };
    }

    const controller = new AbortController();
    let fired = false;
    const timer = setTimeout(() => {
      fired = true;
      controller.abort();
    }, timeoutMs);

    const signal = userSignal
      ? 'any' in AbortSignal
        ? AbortSignal.any([controller.signal, userSignal])
        : this.combineSignals(controller.signal, userSignal)
      : controller.signal;

    return {
      signal,
      timedOut: () => fired,
      dispose: () => clearTimeout(timer),
    };
  }

  /**
   * Combine two AbortSignals into one (fallback for browsers without AbortSignal.any)
   */
  private combineSignals(signal1: AbortSignal, signal2: AbortSignal): AbortSignal {
    const combined = new AbortController();

    const onAbort = () => {
      combined.abort();
      signal1.removeEventListener('abort', onAbort);
      signal2.removeEventListener('abort', onAbort);
    };
    signal1.addEventListener('abort', onAbort);
    signal2.addEventListener('abort', onAbort);

    // If either signal is already aborted, abort immediately
    if (signal1.aborted || signal2.aborted) {
      onAbort();
    }

    return combined.signal;
  }

  /**
   * Sleep for the specified duration in milliseconds
   */
  private sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  /**
   * Build full URL with query params
   */
  private buildUrl(url: string, params?: Record<string, unknown> | object): string {
    const fullUrl = url.startsWith('http') ? url : `${this.config.baseUrl}${url}`;

    if (!params || Object.keys(params).length === 0) {
      return fullUrl;
    }

    const searchParams = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined && value !== null) {
        if (Array.isArray(value)) {
          for (const item of value) {
            searchParams.append(key, String(item));
          }
        } else if (value instanceof Date) {
          searchParams.append(key, value.toISOString());
        } else {
          searchParams.append(key, String(value));
        }
      }
    }

    const separator = fullUrl.includes('?') ? '&' : '?';
    return `${fullUrl}${separator}${searchParams.toString()}`;
  }

  /**
   * Build headers with auth token
   */
  private buildHeaders(customHeaders?: Record<string, string>): HeadersInit {
    const headers: Record<string, string> = {
      ...this.config.defaultHeaders,
      ...customHeaders,
    };

    if (this.accessToken) {
      headers['Authorization'] = `Bearer ${this.accessToken}`;
    }

    return headers;
  }
}

/**
 * Create HTTP client instance
 */
export function createHttpClient(config: HttpClientConfig): HttpClient {
  return new HttpClient(config);
}
