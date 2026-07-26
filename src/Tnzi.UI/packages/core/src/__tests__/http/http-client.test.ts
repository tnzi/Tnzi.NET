import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { HttpClient, createHttpClient } from '../../http/http';

// Mock fetch globally
const mockFetch = vi.fn();
vi.stubGlobal('fetch', mockFetch);

function jsonResponse(data: Record<string, unknown>, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    json: () => Promise.resolve(data),
    blob: () => Promise.resolve(new Blob()),
    headers: new Headers(),
  } as unknown as Response;
}

function apiSuccessResponse<T>(data: T) {
  return jsonResponse({ succeeded: true, data, code: 200 });
}

function apiErrorResponse(message: string, code = 500) {
  return jsonResponse({ succeeded: false, message, code }, code);
}

describe('HttpClient', () => {
  let client: HttpClient;

  beforeEach(() => {
    mockFetch.mockReset();
    client = new HttpClient({ baseUrl: 'http://localhost:5000/api' });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ------------------------------------------
  // Constructor
  // ------------------------------------------

  describe('constructor', () => {
    it('should create client with config', () => {
      expect(client).toBeDefined();
    });

    it('should use createHttpClient factory', () => {
      const c = createHttpClient({ baseUrl: '/api' });
      expect(c).toBeInstanceOf(HttpClient);
    });
  });

  // ------------------------------------------
  // Token management
  // ------------------------------------------

  describe('token management', () => {
    it('should set and get access token', () => {
      client.setAccessToken('my-token');
      expect(client.getAccessToken()).toBe('my-token');
    });

    it('should include Authorization header when token is set', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ id: 1 }));
      client.setAccessToken('bearer-token');
      await client.get('/test');

      const [, fetchOptions] = mockFetch.mock.calls[0];
      expect(fetchOptions.headers['Authorization']).toBe('Bearer bearer-token');
    });

    it('should not include Authorization header when token is null', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ id: 1 }));
      await client.get('/test');

      const [, fetchOptions] = mockFetch.mock.calls[0];
      expect(fetchOptions.headers['Authorization']).toBeUndefined();
    });

    it('should clear token', () => {
      client.setAccessToken('token');
      client.setAccessToken(null);
      expect(client.getAccessToken()).toBeNull();
    });
  });

  // ------------------------------------------
  // HTTP methods
  // ------------------------------------------

  describe('HTTP methods', () => {
    beforeEach(() => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ id: 1, name: 'test' }));
    });

    it('GET should call fetch with GET method', async () => {
      await client.get('/users/1');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5000/api/users/1',
        expect.objectContaining({ method: 'GET' }),
      );
    });

    it('POST should call fetch with body', async () => {
      await client.post('/users', { name: 'test' });
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.method).toBe('POST');
      expect(opts.body).toBe(JSON.stringify({ name: 'test' }));
    });

    it('PUT should call fetch with body', async () => {
      await client.put('/users/1', { name: 'updated' });
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.method).toBe('PUT');
    });

    it('PATCH should call fetch with body', async () => {
      await client.patch('/users/1', { name: 'patched' });
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.method).toBe('PATCH');
    });

    it('DELETE should call fetch with DELETE method', async () => {
      await client.delete('/users/1');
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.method).toBe('DELETE');
    });
  });

  // ------------------------------------------
  // URL building
  // ------------------------------------------

  describe('URL building', () => {
    beforeEach(() => {
      mockFetch.mockResolvedValue(apiSuccessResponse(null));
    });

    it('should prepend baseUrl for relative paths', async () => {
      await client.get('/users');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5000/api/users',
        expect.anything(),
      );
    });

    it('should use absolute URL directly', async () => {
      await client.get('http://other-host/data');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://other-host/data',
        expect.anything(),
      );
    });

    it('should append query params', async () => {
      await client.get('/users', { params: { page: 1, size: 20 } });
      const url = mockFetch.mock.calls[0][0];
      expect(url).toContain('page=1');
      expect(url).toContain('size=20');
    });

    it('should handle array params', async () => {
      await client.get('/users', { params: { ids: [1, 2, 3] } });
      const url: string = mockFetch.mock.calls[0][0];
      expect(url).toContain('ids=1');
      expect(url).toContain('ids=2');
      expect(url).toContain('ids=3');
    });

    it('should skip null/undefined params', async () => {
      await client.get('/users', { params: { name: null, age: undefined, status: 'active' } });
      const url: string = mockFetch.mock.calls[0][0];
      expect(url).not.toContain('name');
      expect(url).not.toContain('age');
      expect(url).toContain('status=active');
    });
  });

  // ------------------------------------------
  // Response normalization
  // ------------------------------------------

  describe('response normalization', () => {
    it('should return normalized ApiResult on success', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ id: 1 }));
      const result = await client.get<{ id: number }>('/test');
      expect(result.succeeded).toBe(true);
      expect(result.data).toEqual({ id: 1 });
    });

    it('should return normalized ApiResult on API error', async () => {
      mockFetch.mockResolvedValue(apiErrorResponse('Not found', 404));
      const result = await client.get('/test');
      expect(result.succeeded).toBe(false);
      expect(result.message).toBe('Not found');
    });

    it('should return failed result on network error', async () => {
      mockFetch.mockRejectedValue(new Error('Network error'));
      const result = await client.get('/test');
      expect(result.succeeded).toBe(false);
    });
  });

  // ------------------------------------------
  // 401 handling
  // ------------------------------------------

  describe('401 handling', () => {
    it('should call onUnauthorized on first 401', async () => {
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.get('/protected');
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
    });

    it('should deduplicate 401 callbacks', async () => {
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.get('/a');
      await c.get('/b');
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
    });

    it('should reset deduplication guard after setAccessToken', async () => {
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.get('/a');
      c.setAccessToken('new-token');
      await c.get('/b');
      expect(onUnauthorized).toHaveBeenCalledTimes(2);
    });
  });

  // ------------------------------------------
  // skipAuthRefresh (auth-flow requests)
  // ------------------------------------------

  describe('skipAuthRefresh', () => {
    it('should return 401 as-is without calling refreshTokenFn', async () => {
      const refreshFn = vi.fn().mockResolvedValue('new-token');
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      const result = await c.post('/auth/login', { u: 'x' }, { skipAuthRefresh: true });
      expect(result.code).toBe(401);
      expect(refreshFn).not.toHaveBeenCalled();
      expect(mockFetch).toHaveBeenCalledTimes(1);
    });

    it('should not trigger onUnauthorized for auth-flow 401s', async () => {
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.post('/auth/login', { u: 'x' }, { skipAuthRefresh: true });
      expect(onUnauthorized).not.toHaveBeenCalled();
    });

    it('should not deadlock when a skipAuthRefresh request 401s during an active refresh', async () => {
      // Regression: the logout/refresh POST issued from inside refreshTokenFn
      // used to re-enter tryRefreshAndRetry, await its own _refreshPromise,
      // and stall until the refresh timeout. With skipAuthRefresh the inner
      // call settles immediately and the whole cycle finishes fast.
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      const onUnauthorized = vi.fn();
      let innerResult: { code: number } | null = null;
      const c: HttpClient = new HttpClient({
        baseUrl: '/api',
        onUnauthorized,
        refreshTokenFn: async () => {
          // Simulates AuthStateManager's cleanup POST during a failed refresh.
          innerResult = await c.post('/auth/logout', undefined, { skipAuthRefresh: true });
          throw new Error('refresh failed');
        },
      });
      const result = await c.get('/protected');
      expect(result.code).toBe(401);
      expect(innerResult!.code).toBe(401);
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
    });
  });

  // ------------------------------------------
  // addUnauthorizedListener (multicast)
  // ------------------------------------------

  describe('addUnauthorizedListener', () => {
    it('should fire listeners alongside onUnauthorized', async () => {
      const onUnauthorized = vi.fn();
      const listener = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
      c.addUnauthorizedListener(listener);
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.get('/protected');
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
      expect(listener).toHaveBeenCalledTimes(1);
    });

    it('should fire listeners even without a config-level handler', async () => {
      const listener = vi.fn();
      const c = new HttpClient({ baseUrl: '/api' });
      c.addUnauthorizedListener(listener);
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.get('/protected');
      expect(listener).toHaveBeenCalledTimes(1);
    });

    it('should still run listeners when the config handler throws', async () => {
      const listener = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        onUnauthorized: () => {
          throw new Error('consumer handler exploded');
        },
      });
      c.addUnauthorizedListener(listener);
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      const result = await c.get('/protected');
      expect(result.code).toBe(401);
      expect(listener).toHaveBeenCalledTimes(1);
    });

    it('should support unsubscribe and share the dedup guard', async () => {
      const listener = vi.fn();
      const c = new HttpClient({ baseUrl: '/api' });
      const off = c.addUnauthorizedListener(listener);
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      await c.get('/a');
      await c.get('/b'); // deduplicated: same auth cycle
      expect(listener).toHaveBeenCalledTimes(1);
      off();
      c.setAccessToken('fresh'); // resets the dedup guard
      await c.get('/c');
      expect(listener).toHaveBeenCalledTimes(1);
    });

    it('should keep the once-per-cycle guard when the handler clears the token', async () => {
      // createTnziClient wires onUnauthorized -> auth.clearAuth() -> setAccessToken(null).
      // Re-arming the guard there reopened it mid-notification, so every queued
      // 401 fired the listeners again.
      const listener = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        onUnauthorized: () => c.setAccessToken(null),
      });
      c.addUnauthorizedListener(listener);
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      await Promise.all([c.get('/a'), c.get('/b'), c.get('/c')]);

      expect(listener).toHaveBeenCalledTimes(1);
    });

    it('should re-arm the guard once a new token is installed', async () => {
      const listener = vi.fn();
      const c = new HttpClient({ baseUrl: '/api' });
      c.addUnauthorizedListener(listener);
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      await c.get('/a');
      c.setAccessToken(null);
      await c.get('/b');
      expect(listener).toHaveBeenCalledTimes(1);

      c.setAccessToken('new-session');
      await c.get('/c');
      expect(listener).toHaveBeenCalledTimes(2);
    });
  });

  // ------------------------------------------
  // Request interceptor
  // ------------------------------------------

  describe('request interceptor', () => {
    it('should apply request interceptor', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse(null));
      const c = new HttpClient({
        baseUrl: '/api',
        requestInterceptor: (config) => ({
          ...config,
          headers: { ...config.headers, 'X-Custom': 'value' },
        }),
      });
      await c.get('/test');
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.headers['X-Custom']).toBe('value');
    });
  });

  // ------------------------------------------
  // Response interceptor
  // ------------------------------------------

  describe('response interceptor', () => {
    it('should apply response interceptor', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ original: true }));
      const c = new HttpClient({
        baseUrl: '/api',
        responseInterceptor: (response) => ({
          ...response,
          data: { ...response.data, intercepted: true },
        }),
      });
      const result = await c.get<{ original: boolean; intercepted: boolean }>('/test');
      expect(result.data?.intercepted).toBe(true);
    });
  });

  // ------------------------------------------
  // Error interceptor
  // ------------------------------------------

  describe('error interceptor', () => {
    it('should call error interceptor on network error', async () => {
      const errorInterceptor = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', errorInterceptor });
      mockFetch.mockRejectedValue(new Error('Network failure'));
      await c.get('/test');
      expect(errorInterceptor).toHaveBeenCalledWith(expect.any(Error));
    });
  });

  // ------------------------------------------
  // Retry
  // ------------------------------------------

  describe('retry', () => {
    it('should retry on retryable status codes', async () => {
      const c = new HttpClient({
        baseUrl: '/api',
        retry: { maxRetries: 2, baseDelay: 1 },
      });
      mockFetch
        .mockResolvedValueOnce(jsonResponse({ succeeded: false, code: 503 }, 503))
        .mockResolvedValueOnce(jsonResponse({ succeeded: false, code: 503 }, 503))
        .mockResolvedValueOnce(apiSuccessResponse({ id: 1 }));

      const result = await c.get<{ id: number }>('/test');
      expect(mockFetch).toHaveBeenCalledTimes(3);
      expect(result.succeeded).toBe(true);
    });

    it('should not retry on non-retryable status codes', async () => {
      const c = new HttpClient({
        baseUrl: '/api',
        retry: { maxRetries: 2, baseDelay: 1 },
      });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 404 }, 404));

      await c.get('/test');
      expect(mockFetch).toHaveBeenCalledTimes(1);
    });

    it('should stop retrying after maxRetries', async () => {
      const c = new HttpClient({
        baseUrl: '/api',
        retry: { maxRetries: 1, baseDelay: 1 },
      });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 500 }, 500));

      await c.get('/test');
      expect(mockFetch).toHaveBeenCalledTimes(2); // 1 original + 1 retry
    });
  });

  // ------------------------------------------
  // Download
  // ------------------------------------------

  describe('download', () => {
    it('should return blob on success', async () => {
      const blob = new Blob(['test'], { type: 'text/plain' });
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        blob: () => Promise.resolve(blob),
      });

      const result = await client.download('/file');
      expect(result.succeeded).toBe(true);
      expect(result.data).toBeInstanceOf(Blob);
    });

    it('should return failed result on error', async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        statusText: 'Not Found',
      });

      const result = await client.download('/missing');
      expect(result.succeeded).toBe(false);
    });

    it('should surface the ApiResult envelope message on a failed download', async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 400,
        statusText: 'Bad Request',
        text: () =>
          Promise.resolve(
            JSON.stringify({ code: 400, succeeded: false, message: 'Narrow the date range.' }),
          ),
      });

      const result = await client.download('/export');
      expect(result.succeeded).toBe(false);
      expect(result.code).toBe(400);
      expect(result.message).toBe('Narrow the date range.');
    });

    it('should fall back to the plain-text body when the error body is not JSON', async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        text: () => Promise.resolve('boom'),
      });

      const result = await client.download('/export');
      expect(result.succeeded).toBe(false);
      expect(result.message).toBe('boom');
    });
  });

  // ------------------------------------------
  // resolveUrl
  // ------------------------------------------

  describe('resolveUrl', () => {
    it('should resolve relative URL', () => {
      expect(client.resolveUrl('/users')).toBe('http://localhost:5000/api/users');
    });

    it('should resolve with params', () => {
      const url = client.resolveUrl('/users', { page: 1 });
      expect(url).toContain('page=1');
    });
  });

  // ------------------------------------------
  // Response middleware
  // ------------------------------------------

  describe('response middleware', () => {
    it('should apply middleware pipeline', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ count: 1 }));

      const middleware = vi.fn((result, _ctx) => ({
        ...result,
        data: { ...result.data, processed: true },
      }));

      const c = new HttpClient({
        baseUrl: '/api',
        responseMiddlewares: [middleware],
      });

      const result = await c.get<{ count: number; processed: boolean }>('/test');
      expect(middleware).toHaveBeenCalled();
      expect(result.data?.processed).toBe(true);
    });
  });

  // ------------------------------------------
  // normalizeApiResult: `success` field support
  // ------------------------------------------

  describe('normalizeApiResult success field', () => {
    it('should normalize response with succeeded:true and success:true', async () => {
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: true, success: true, code: 200, data: { id: 1 } }));
      const result = await client.get<{ id: number }>('/test');
      expect(result.succeeded).toBe(true);
      expect(result.success).toBe(true);
      expect(result.data?.id).toBe(1);
    });

    it('should normalize response with succeeded:false and success:false', async () => {
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, success: false, code: 400, message: 'Bad' }));
      const result = await client.get('/test');
      expect(result.succeeded).toBe(false);
      expect(result.success).toBe(false);
      expect(result.code).toBe(400);
    });
  });

  // ------------------------------------------
  // 401 auto-refresh + retry
  // ------------------------------------------

  describe('401 refresh and retry', () => {
    it('should refresh token and retry on 401 when refreshTokenFn is configured', async () => {
      const refreshFn = vi.fn().mockResolvedValue('new-token');
      const onUnauthorized = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        refreshTokenFn: refreshFn,
        onUnauthorized,
      });

      // First call returns 401, second call (retry) returns success
      mockFetch
        .mockResolvedValueOnce(jsonResponse({ succeeded: false, code: 401, message: 'Unauthorized' }, 401))
        .mockResolvedValueOnce(apiSuccessResponse({ id: 1 }));

      const result = await c.get<{ id: number }>('/protected');
      expect(refreshFn).toHaveBeenCalledTimes(1);
      expect(result.succeeded).toBe(true);
      expect(result.data?.id).toBe(1);
      expect(onUnauthorized).not.toHaveBeenCalled();
      expect(c.getAccessToken()).toBe('new-token');
    });

    it('should deduplicate concurrent refresh calls', async () => {
      let resolveRefresh: (value: string) => void;
      const refreshPromise = new Promise<string>(r => { resolveRefresh = r; });
      const refreshFn = vi.fn().mockReturnValue(refreshPromise);
      const c = new HttpClient({
        baseUrl: '/api',
        refreshTokenFn: refreshFn,
      });

      // All calls return 401 first, then success on retry
      mockFetch.mockImplementation(() =>
        Promise.resolve(
          mockFetch.mock.calls.length <= 2
            ? jsonResponse({ succeeded: false, code: 401 }, 401)
            : apiSuccessResponse({ ok: true })
        )
      );

      const p1 = c.get('/a');
      const p2 = c.get('/b');

      // Allow microtasks to run
      await vi.waitFor(() => expect(refreshFn).toHaveBeenCalledTimes(1));
      resolveRefresh!('refreshed-token');

      await Promise.all([p1, p2]);
      // Only one refresh call despite two 401s
      expect(refreshFn).toHaveBeenCalledTimes(1);
    });

    it('should call onUnauthorized when refreshTokenFn fails', async () => {
      const refreshFn = vi.fn().mockRejectedValue(new Error('refresh failed'));
      const onUnauthorized = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        refreshTokenFn: refreshFn,
        onUnauthorized,
      });

      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      const result = await c.get('/protected');
      expect(refreshFn).toHaveBeenCalledTimes(1);
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
      expect(result.succeeded).toBe(false);
    });

    it('should call onUnauthorized when no refreshTokenFn is configured', async () => {
      const onUnauthorized = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        onUnauthorized,
      });

      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      await c.get('/protected');
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
    });

    it('should not attempt refresh on retry 401 (prevent infinite loop)', async () => {
      const refreshFn = vi.fn().mockResolvedValue('new-token');
      const onUnauthorized = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        refreshTokenFn: refreshFn,
        onUnauthorized,
      });

      // Both original and retry return 401
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      const result = await c.get('/protected');
      expect(refreshFn).toHaveBeenCalledTimes(1); // Only one refresh attempt
      expect(result.succeeded).toBe(false);
    });

    it('should call onUnauthorized when refresh succeeds but retry still 401s', async () => {
      // Scenario: backend gave us a "new" token (or the same one) via
      // refreshTokenFn but it's still rejected by the protected endpoint
      // - e.g. session was server-side revoked, stale token returned, or
      // clock skew. Before the fix, the inner executeWithRetry returned
      // the 401 result and the outer caller never invoked onUnauthorized,
      // so the consumer's session-expired handler (router push to /login,
      // toast, etc.) never fired and the page sat in an API-error loop.
      const refreshFn = vi.fn().mockResolvedValue('still-stale-token');
      const onUnauthorized = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        refreshTokenFn: refreshFn,
        onUnauthorized,
      });

      // Original 401 → refresh "succeeds" → retry 401 → MUST notify.
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      const result = await c.get('/protected');
      expect(refreshFn).toHaveBeenCalledTimes(1);
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
      expect(result.succeeded).toBe(false);
      expect(result.code).toBe(401);
    });
  });

  // ------------------------------------------
  // GET request deduplication
  // ------------------------------------------

  describe('GET deduplication', () => {
    it('should deduplicate concurrent identical GET requests', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ items: [] }));

      const [r1, r2, r3] = await Promise.all([
        client.get('/users'),
        client.get('/users'),
        client.get('/users'),
      ]);

      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(r1.succeeded).toBe(true);
      expect(r2.succeeded).toBe(true);
      expect(r3.succeeded).toBe(true);
    });

    it('should NOT deduplicate different GET URLs', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({}));

      await Promise.all([
        client.get('/users'),
        client.get('/roles'),
      ]);

      expect(mockFetch).toHaveBeenCalledTimes(2);
    });

    it('should NOT deduplicate POST requests', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({}));

      await Promise.all([
        client.post('/users', { name: 'a' }),
        client.post('/users', { name: 'b' }),
      ]);

      expect(mockFetch).toHaveBeenCalledTimes(2);
    });

    it('should make new request after previous GET completes', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ v: 1 }));
      await client.get('/data');

      mockFetch.mockResolvedValue(apiSuccessResponse({ v: 2 }));
      const result = await client.get('/data');

      expect(mockFetch).toHaveBeenCalledTimes(2);
      expect(result.data).toEqual({ v: 2 });
    });

    it('should skip deduplication when deduplicateGets is false', async () => {
      const c = new HttpClient({ baseUrl: '/api', deduplicateGets: false });
      mockFetch.mockResolvedValue(apiSuccessResponse({}));

      await Promise.all([c.get('/users'), c.get('/users')]);

      expect(mockFetch).toHaveBeenCalledTimes(2);
    });

    it('should NOT deduplicate GETs that carry an abort signal', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({}));
      const a = new AbortController();
      const b = new AbortController();

      await Promise.all([
        client.get('/users', { signal: a.signal }),
        client.get('/users', { signal: b.signal }),
      ]);

      // Sharing one fetch across two independent controllers means either
      // caller's abort cancels the other's request.
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });

    it('should not let an aborted GET poison the next one (cancel-then-refetch)', async () => {
      // Reproduces the DataQueryController shape: abort the in-flight request,
      // then synchronously issue a fresh one for the same URL.
      const first = new AbortController();
      mockFetch.mockImplementationOnce((_url: string, opts: { signal?: AbortSignal }) =>
        new Promise((_resolve, reject) => {
          opts.signal?.addEventListener('abort', () =>
            reject(new DOMException('The operation was aborted.', 'AbortError')),
          );
        }),
      );
      const pending = client.get('/users', { signal: first.signal });

      first.abort();
      mockFetch.mockResolvedValue(apiSuccessResponse({ items: ['fresh'] }));
      const second = new AbortController();
      const result = await client.get('/users', { signal: second.signal });

      await pending; // the aborted one resolves as a failed result, as designed
      expect(result.succeeded).toBe(true);
      expect(result.data).toEqual({ items: ['fresh'] });
      expect(second.signal.aborted).toBe(false);
    });

    it('should still deduplicate signal-free GETs alongside signal-bearing ones', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({}));
      const controller = new AbortController();

      await Promise.all([
        client.get('/users'),
        client.get('/users'),
        client.get('/users', { signal: controller.signal }),
      ]);

      // Two signal-free callers share one fetch; the signal-bearing one is its own.
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });

  // ------------------------------------------
  // Request timeout
  // ------------------------------------------

  /** Mock fetch that never settles unless its abort signal fires (rejects with DOM AbortError). */
  function hangingFetch() {
    mockFetch.mockImplementation((_url: string, opts: { signal?: AbortSignal }) =>
      new Promise((_resolve, reject) => {
        opts.signal?.addEventListener('abort', () =>
          reject(new DOMException('The operation was aborted.', 'AbortError')),
        );
      }),
    );
  }

  describe('request timeout', () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it('should fail with REQUEST_TIMEOUT when the default 30s timeout elapses', async () => {
      vi.useFakeTimers();
      hangingFetch();

      const p = client.get('/slow');
      await vi.advanceTimersByTimeAsync(30000);
      const result = await p;

      expect(result.succeeded).toBe(false);
      expect(result.code).toBe(408);
      expect(result.errorCode).toBe('REQUEST_TIMEOUT');
    });

    it('should honor per-request timeout override', async () => {
      vi.useFakeTimers();
      hangingFetch();

      const p = client.get('/slow', { timeout: 50 });
      await vi.advanceTimersByTimeAsync(50);
      const result = await p;

      expect(result.succeeded).toBe(false);
      expect(result.errorCode).toBe('REQUEST_TIMEOUT');
    });

    it('per-request timeout larger than default should not fire early', async () => {
      vi.useFakeTimers();
      hangingFetch();

      let settled = false;
      const p = client.get('/slow', { timeout: 60000 }).then((r) => {
        settled = true;
        return r;
      });

      await vi.advanceTimersByTimeAsync(30000);
      expect(settled).toBe(false);

      await vi.advanceTimersByTimeAsync(30000);
      const result = await p;
      expect(result.errorCode).toBe('REQUEST_TIMEOUT');
    });

    it('should honor instance-level timeout config', async () => {
      vi.useFakeTimers();
      hangingFetch();

      const c = new HttpClient({ baseUrl: '/api', timeout: 100 });
      const p = c.get('/slow');
      await vi.advanceTimersByTimeAsync(100);
      const result = await p;

      expect(result.errorCode).toBe('REQUEST_TIMEOUT');
    });

    it('timeout: 0 should disable the timeout (no signal attached)', async () => {
      mockFetch.mockResolvedValue(apiSuccessResponse({ ok: true }));
      const c = new HttpClient({ baseUrl: '/api', timeout: 0 });

      const result = await c.get('/no-timeout');

      expect(result.succeeded).toBe(true);
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.signal).toBeUndefined();
    });

    it('should pass a TimeoutError to errorInterceptor on timeout', async () => {
      vi.useFakeTimers();
      hangingFetch();
      const errorInterceptor = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', timeout: 10, errorInterceptor });

      const p = c.get('/slow');
      await vi.advanceTimersByTimeAsync(10);
      await p;

      expect(errorInterceptor).toHaveBeenCalledTimes(1);
      expect(errorInterceptor.mock.calls[0][0].name).toBe('TimeoutError');
    });

    it('user abort should NOT be reported as timeout', async () => {
      hangingFetch();
      const ac = new AbortController();

      const p = client.get('/slow', { signal: ac.signal });
      ac.abort();
      const result = await p;

      expect(result.succeeded).toBe(false);
      expect(result.errorCode).not.toBe('REQUEST_TIMEOUT');
      expect(result.code).toBe(500); // legacy mapping for non-timeout failures unchanged
    });

    it('timed-out GET should be removed from dedup map so the next GET retries', async () => {
      vi.useFakeTimers();
      hangingFetch();

      const p = client.get('/list', { timeout: 20 });
      await vi.advanceTimersByTimeAsync(20);
      const r1 = await p;
      expect(r1.errorCode).toBe('REQUEST_TIMEOUT');

      vi.useRealTimers();
      mockFetch.mockResolvedValue(apiSuccessResponse({ ok: 1 }));
      const r2 = await client.get('/list');

      expect(r2.succeeded).toBe(true);
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });

  // ------------------------------------------
  // Download timeout exemption
  // ------------------------------------------

  describe('download timeout exemption', () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it('download should not attach the default timeout signal', async () => {
      const blob = new Blob(['x']);
      mockFetch.mockResolvedValue({ ok: true, status: 200, blob: () => Promise.resolve(blob) });

      const result = await client.download('/big-file');

      expect(result.succeeded).toBe(true);
      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.signal).toBeUndefined();
    });

    it('download should pass the user signal through unchanged', async () => {
      const blob = new Blob(['x']);
      mockFetch.mockResolvedValue({ ok: true, status: 200, blob: () => Promise.resolve(blob) });
      const ac = new AbortController();

      await client.download('/big-file', { signal: ac.signal });

      const [, opts] = mockFetch.mock.calls[0];
      expect(opts.signal).toBe(ac.signal);
    });

    it('download should honor an explicit per-request timeout', async () => {
      vi.useFakeTimers();
      hangingFetch();

      const p = client.download('/big-file', { timeout: 50 });
      await vi.advanceTimersByTimeAsync(50);
      const result = await p;

      expect(result.succeeded).toBe(false);
      expect(result.code).toBe(408);
      expect(result.errorCode).toBe('REQUEST_TIMEOUT');
    });
  });

  // ------------------------------------------
  // Refresh timeout and mutex reset
  // ------------------------------------------

  describe('refresh timeout and mutex reset', () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it('should settle the waiter with the original 401 when refreshTokenFn never settles', async () => {
      vi.useFakeTimers();
      const refreshFn = vi.fn().mockReturnValue(new Promise<string>(() => {}));
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn, onUnauthorized });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401, message: 'Unauthorized' }, 401));

      const p = c.get('/protected');
      await vi.advanceTimersByTimeAsync(30000);
      const result = await p;

      expect(refreshFn).toHaveBeenCalledTimes(1);
      expect(result.succeeded).toBe(false);
      expect(result.code).toBe(401);
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
    });

    it('should reset the refresh mutex after timeout so later requests can retry refresh', async () => {
      vi.useFakeTimers();
      const refreshFn = vi.fn()
        .mockReturnValueOnce(new Promise<string>(() => {})) // first refresh: hangs forever
        .mockResolvedValueOnce('recovered-token'); // second refresh: succeeds
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn, onUnauthorized });

      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));
      const p1 = c.get('/first');
      await vi.advanceTimersByTimeAsync(30000);
      const r1 = await p1;
      expect(r1.code).toBe(401);

      vi.useRealTimers();
      mockFetch.mockReset();
      mockFetch
        .mockResolvedValueOnce(jsonResponse({ succeeded: false, code: 401 }, 401))
        .mockResolvedValueOnce(apiSuccessResponse({ id: 7 }));

      const r2 = await c.get('/second');

      expect(refreshFn).toHaveBeenCalledTimes(2); // mutex was reset, refresh retried
      expect(r2.succeeded).toBe(true);
      expect(c.getAccessToken()).toBe('recovered-token');
    });

    it('concurrent waiters during a hung refresh all receive their original 401', async () => {
      vi.useFakeTimers();
      const refreshFn = vi.fn().mockReturnValue(new Promise<string>(() => {}));
      const onUnauthorized = vi.fn();
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn, onUnauthorized });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      const p1 = c.get('/a');
      const p2 = c.get('/b');
      await vi.advanceTimersByTimeAsync(30000);
      const [r1, r2] = await Promise.all([p1, p2]);

      expect(refreshFn).toHaveBeenCalledTimes(1); // mutex: single refresh call shared
      expect(r1.code).toBe(401);
      expect(r2.code).toBe(401);
      expect(onUnauthorized).toHaveBeenCalledTimes(1); // dedup guard intact
    });

    it('refresh timeout should follow the instance timeout config', async () => {
      vi.useFakeTimers();
      const refreshFn = vi.fn().mockReturnValue(new Promise<string>(() => {}));
      const c = new HttpClient({
        baseUrl: '/api',
        timeout: 100,
        refreshTokenFn: refreshFn,
        onUnauthorized: vi.fn(),
      });
      mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 401 }, 401));

      const p = c.get('/x');
      await vi.advanceTimersByTimeAsync(100);
      const result = await p;

      expect(result.code).toBe(401);
    });

    it('refresh resolving normally is unaffected by the timeout wrapper', async () => {
      const refreshFn = vi.fn().mockResolvedValue('fresh-token');
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn });
      mockFetch
        .mockResolvedValueOnce(jsonResponse({ succeeded: false, code: 401 }, 401))
        .mockResolvedValueOnce(apiSuccessResponse({ id: 1 }));

      const result = await c.get<{ id: number }>('/protected');

      expect(result.succeeded).toBe(true);
      expect(c.getAccessToken()).toBe('fresh-token');
    });
  });

  // ------------------------------------------
  // Upload 401 handling
  // ------------------------------------------

  describe('uploadFormData 401 handling', () => {
    /**
     * Minimal XHR stand-in. `queue` supplies one `{status, body}` per send, so a
     * test can script "401 then 200" across the refresh-retry.
     */
    function stubXhr(queue: Array<{ status: number; body: unknown }>): { sends: number } {
      const state = { sends: 0 };
      class FakeXhr {
        status = 0;
        responseText = '';
        timeout = 0;
        withCredentials = false;
        upload = { onprogress: null as unknown };
        onload: (() => void) | null = null;
        onerror: (() => void) | null = null;
        ontimeout: (() => void) | null = null;
        open() {}
        setRequestHeader() {}
        send() {
          const next = queue[state.sends] ?? queue[queue.length - 1];
          state.sends += 1;
          this.status = next.status;
          this.responseText = JSON.stringify(next.body);
          queueMicrotask(() => this.onload?.());
        }
      }
      vi.stubGlobal('XMLHttpRequest', FakeXhr);
      return state;
    }

    afterEach(() => {
      vi.unstubAllGlobals();
      vi.stubGlobal('fetch', mockFetch);
    });

    it('refreshes and retries the upload once on 401', async () => {
      const sends = stubXhr([
        { status: 401, body: { succeeded: false, code: 401, message: 'expired' } },
        { status: 200, body: { succeeded: true, code: 200, data: { id: 'f1' } } },
      ]);
      const refreshFn = vi.fn().mockResolvedValue('fresh-token');
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn });

      const result = await c.uploadFormData<{ id: string }>('/files/upload', new FormData());

      expect(refreshFn).toHaveBeenCalledTimes(1);
      expect(sends.sends).toBe(2);
      expect(result.succeeded).toBe(true);
      expect(result.data).toEqual({ id: 'f1' });
    });

    it('notifies onUnauthorized when the upload refresh fails', async () => {
      stubXhr([{ status: 401, body: { succeeded: false, code: 401 } }]);
      const onUnauthorized = vi.fn();
      const c = new HttpClient({
        baseUrl: '/api',
        refreshTokenFn: vi.fn().mockRejectedValue(new Error('no refresh token')),
        onUnauthorized,
      });

      const result = await c.uploadFormData('/files/upload', new FormData());

      expect(onUnauthorized).toHaveBeenCalledTimes(1);
      expect(result.code).toBe(401);
    });

    it('returns a 401 as-is for auth-flow uploads (skipAuthRefresh)', async () => {
      const sends = stubXhr([{ status: 401, body: { succeeded: false, code: 401 } }]);
      const refreshFn = vi.fn().mockResolvedValue('fresh-token');
      const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn });

      const result = await c.uploadFormData('/files/upload', new FormData(), {
        skipAuthRefresh: true,
      });

      expect(refreshFn).not.toHaveBeenCalled();
      expect(sends.sends).toBe(1);
      expect(result.code).toBe(401);
    });
  });
});
