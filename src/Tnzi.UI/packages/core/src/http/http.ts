/**
 * Http Client - HTTP client for Tnzi.NET backend
 */

import type { ApiResult, HttpMethod, RequestOptions, UploadOptions } from '../types/api';
import { createFailedApiResult, createFailedApiResultFromError } from '../errors/api-error';
import { normalizeApiResult } from './response';
import type { HttpResponseContext, HttpResponseMiddleware } from './middleware';

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
  /** Request timeout in milliseconds */
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
  /** Map of inflight GET requests for deduplication */
  private _inflightGets = new Map<string, Promise<ApiResult<unknown>>>();

  constructor(config: HttpClientConfig) {
    this.config = {
      timeout: 30000,
      defaultHeaders: {
        'Content-Type': 'application/json',
      },
      ...config,
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
        resolve(createFailedApiResultFromError<T>(
          new Error('Upload timeout'),
        ));
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

      if (options?.timeout) {
        xhr.timeout = options.timeout;
      } else if (this.config.timeout) {
        xhr.timeout = this.config.timeout;
      }

      if (options?.withCredentials) {
        xhr.withCredentials = true;
      }

      xhr.send(formData);
    });
  }

  /**
   * Download file. Returns ApiResult<Blob> for consistent error handling.
   */
  async download(url: string, options?: RequestOptions): Promise<ApiResult<Blob>> {
    try {
      const fullUrl = this.buildUrl(url, options?.params);
      const headers = this.buildHeaders(options?.headers);

      const response = await fetch(fullUrl, {
        method: 'GET',
        headers,
        signal: options?.signal,
        credentials: options?.withCredentials ? 'include' : 'same-origin',
      });

      if (!response.ok) {
        return createFailedApiResult<Blob>({
          message: `Download failed: ${response.status} ${response.statusText}`,
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
      return createFailedApiResultFromError<Blob>(error);
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

      // Handle 401 with auto-refresh (only if not already a retry after refresh)
      if (lastResult.code === 401 && !isRetryAfterRefresh) {
        const refreshResult = await this.tryRefreshAndRetry<T>(config);
        if (refreshResult) {
          return refreshResult;
        }
        // Refresh not available or failed — return the 401 result
        return lastResult;
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
      // Mutex: if a refresh is already in progress, wait for it
      if (!this._refreshPromise) {
        this._refreshPromise = this.config.refreshTokenFn();
      }
      const newToken = await this._refreshPromise;
      this.setAccessToken(newToken);

      // Retry the original request once with new token
      return await this.executeWithRetry<T>(config, true);
    } catch {
      this.notifyUnauthorized();
      return null;
    } finally {
      this._refreshPromise = null;
    }
  }

  /**
   * Trigger the unauthorized handler (deduplicated — only fires once per auth cycle).
   */
  private notifyUnauthorized(): void {
    if (!this.unauthorizedHandled) {
      this.unauthorizedHandled = true;
      const handler = this.config.onUnauthorized ?? this.config.onTokenExpired;
      handler?.();
    }
  }

  /**
   * Execute a single HTTP request (no retry logic)
   */
  private async executeRequest<T>(config: RequestConfig): Promise<ApiResult<T>> {
    try {
      const fullUrl = this.buildUrl(config.url, config.params);
      const headers = this.buildHeaders(config.headers);

      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), config.timeout ?? 30000);

      // Combine abort signals if caller provided one
      let signal: AbortSignal;
      if (config.signal) {
        signal = 'any' in AbortSignal
          ? AbortSignal.any([controller.signal, config.signal])
          : this.combineSignals(controller.signal, config.signal);
      } else {
        signal = controller.signal;
      }

      const response = await fetch(fullUrl, {
        method: config.method,
        headers,
        body: config.body ? JSON.stringify(config.body) : undefined,
        signal,
        credentials: config.withCredentials ? 'include' : 'same-origin',
      });

      let data: ApiResult<T>;
      try {
        data = normalizeApiResult<T>(await response.json());
      } catch {
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
      } finally {
        clearTimeout(timeoutId);
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
      // Apply error interceptor
      if (this.config.errorInterceptor) {
        this.config.errorInterceptor(error as Error);
      }

      // Return normalized failed result
      return createFailedApiResultFromError<T>(error);
    }
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
