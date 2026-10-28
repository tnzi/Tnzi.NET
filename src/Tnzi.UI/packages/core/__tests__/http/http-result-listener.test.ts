import { describe, it, expect, beforeEach, vi } from 'vitest';
import { HttpClient } from '../../src/http/http';
import type { ApiResult } from '../../src/types/api';

/**
 * `addResultListener` is how a cross-cutting layer (the admin shell's billing
 * gate) hears about a server-wide refusal that any endpoint may answer with.
 * It must see the FINAL result of each request once, on every transport.
 */

const mockFetch = vi.fn();
vi.stubGlobal('fetch', mockFetch);

function jsonResponse(data: Record<string, unknown>, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    text: () => Promise.resolve(JSON.stringify(data)),
    blob: () => Promise.resolve(new Blob(['x'])),
    headers: new Headers(),
  } as unknown as Response;
}

const suspended = { succeeded: false, code: 402, message: 'Suspended', errorCode: 'SUBSCRIPTION_SUSPENDED' };

describe('HttpClient.addResultListener', () => {
  let client: HttpClient;

  beforeEach(() => {
    mockFetch.mockReset();
    client = new HttpClient({ baseUrl: 'http://localhost/api' });
  });

  it('reports the final result of a JSON request with its method and path', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse(suspended, 402));
    const seen: Array<[ApiResult<unknown>, { method: string; url: string }]> = [];
    client.addResultListener((r, c) => seen.push([r, c]));

    await client.post('/orders', { a: 1 });

    expect(seen).toHaveLength(1);
    expect(seen[0][0].code).toBe(402);
    expect(seen[0][0].errorCode).toBe('SUBSCRIPTION_SUSPENDED');
    expect(seen[0][1]).toEqual({ method: 'POST', url: '/orders' });
  });

  it('reports a deduplicated GET once, not once per caller', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ succeeded: true, code: 200, data: 1 }));
    const listener = vi.fn();
    client.addResultListener(listener);

    await Promise.all([client.get('/a'), client.get('/a'), client.get('/a')]);

    expect(mockFetch).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('reports downloads, including a refused one', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse(suspended, 402));
    const listener = vi.fn();
    client.addResultListener(listener);

    const result = await client.download('/invoices/1/pdf');

    expect(result.succeeded).toBe(false);
    expect(listener).toHaveBeenCalledTimes(1);
    expect(listener.mock.calls[0][0].errorCode).toBe('SUBSCRIPTION_SUSPENDED');
    expect(listener.mock.calls[0][1]).toEqual({ method: 'GET', url: '/invoices/1/pdf' });
  });

  it('isolates a throwing listener and leaves the result untouched', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ succeeded: true, code: 200, data: 'ok' }));
    const after = vi.fn();
    client.addResultListener(() => {
      throw new Error('boom');
    });
    client.addResultListener(after);

    const result = await client.get<string>('/x');

    expect(result.data).toBe('ok');
    expect(after).toHaveBeenCalledTimes(1);
  });

  it('stops reporting after unsubscribe', async () => {
    mockFetch.mockResolvedValue(jsonResponse({ succeeded: true, code: 200, data: 1 }));
    const listener = vi.fn();
    const off = client.addResultListener(listener);
    off();

    await client.post('/x');

    expect(listener).not.toHaveBeenCalled();
  });

  it('does not report requests refused before sending', async () => {
    const listener = vi.fn();
    client.addResultListener(listener);

    await client.get('/files/../admin');

    expect(mockFetch).not.toHaveBeenCalled();
    expect(listener).not.toHaveBeenCalled();
  });
});
