import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { HttpClient } from '../../src/http/http';

/**
 * Refresh + unauthorized entry points for transports that bypass `request()`.
 *
 * SSE streams (`streamChat`) and any other raw-`fetch` transport copy the
 * bearer token out of the client and never come back through `request()`, so
 * the 401 refresh path and the session-expired notification are both invisible
 * to them. After the access token expired, every send failed inline with a bare
 * 401 and the session-expired listener never fired - the refresh token was still
 * valid, so nothing moved the user to login either. These two public entry
 * points are how such a transport joins the same auth cycle instead of
 * re-implementing it.
 */

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

const unauthorized = () => jsonResponse({ succeeded: false, code: 401 }, 401);
const success = () => jsonResponse({ succeeded: true, data: { ok: true }, code: 200 });

describe('HttpClient.refreshAccessToken', () => {
  beforeEach(() => {
    mockFetch.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('returns the new token and stores it on the client', async () => {
    const refreshFn = vi.fn().mockResolvedValue('fresh-token');
    const onUnauthorized = vi.fn();
    const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn, onUnauthorized });
    c.setAccessToken('stale-token');

    await expect(c.refreshAccessToken()).resolves.toBe('fresh-token');
    expect(c.getAccessToken()).toBe('fresh-token');
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('shares the refresh mutex with an in-flight 401 retry', async () => {
    let resolveRefresh: (value: string) => void;
    const refreshPromise = new Promise<string>((r) => { resolveRefresh = r; });
    const refreshFn = vi.fn().mockReturnValue(refreshPromise);
    const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn });
    mockFetch.mockImplementation(() =>
      Promise.resolve(mockFetch.mock.calls.length <= 1 ? unauthorized() : success()),
    );

    const jsonCall = c.get('/a');
    await vi.waitFor(() => expect(refreshFn).toHaveBeenCalledTimes(1));
    const streamRefresh = c.refreshAccessToken();
    resolveRefresh!('shared-token');

    await expect(streamRefresh).resolves.toBe('shared-token');
    await jsonCall;
    expect(refreshFn).toHaveBeenCalledTimes(1);
  });

  it('returns null and notifies unauthorized when the refresh fails', async () => {
    const refreshFn = vi.fn().mockRejectedValue(new Error('refresh failed'));
    const onUnauthorized = vi.fn();
    const listener = vi.fn();
    const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn, onUnauthorized });
    c.addUnauthorizedListener(listener);

    await expect(c.refreshAccessToken()).resolves.toBeNull();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('returns null and notifies unauthorized without a refreshTokenFn', async () => {
    const onUnauthorized = vi.fn();
    const c = new HttpClient({ baseUrl: '/api', onUnauthorized });

    await expect(c.refreshAccessToken()).resolves.toBeNull();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it('leaves the mutex free for a later attempt after a failure', async () => {
    const refreshFn = vi.fn()
      .mockRejectedValueOnce(new Error('first fails'))
      .mockResolvedValueOnce('second-token');
    const c = new HttpClient({ baseUrl: '/api', refreshTokenFn: refreshFn });

    await expect(c.refreshAccessToken()).resolves.toBeNull();
    c.setAccessToken('re-armed');
    await expect(c.refreshAccessToken()).resolves.toBe('second-token');
    expect(refreshFn).toHaveBeenCalledTimes(2);
  });
});

describe('HttpClient.reportUnauthorized', () => {
  beforeEach(() => {
    mockFetch.mockReset();
  });

  it('fires the handler and listeners once per auth cycle', () => {
    const onUnauthorized = vi.fn();
    const listener = vi.fn();
    const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
    c.addUnauthorizedListener(listener);

    c.reportUnauthorized();
    c.reportUnauthorized();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledTimes(1);

    // A new token starts a new cycle and re-arms the guard.
    c.setAccessToken('next-session');
    c.reportUnauthorized();
    expect(onUnauthorized).toHaveBeenCalledTimes(2);
  });

  it('shares the dedup guard with the 401 path', async () => {
    const onUnauthorized = vi.fn();
    const c = new HttpClient({ baseUrl: '/api', onUnauthorized });
    mockFetch.mockResolvedValue(unauthorized());

    await c.get('/protected');
    c.reportUnauthorized();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
