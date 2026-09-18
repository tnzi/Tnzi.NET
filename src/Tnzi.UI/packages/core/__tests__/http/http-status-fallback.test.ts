import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { HttpClient } from '../../src/http/http';
import { normalizeApiResult } from '../../src/http/response';

/**
 * The fetch path used to trust the JSON body over the HTTP status: whenever
 * `response.json()` parsed, `normalizeApiResult` filled in `code: 200` for a
 * body without envelope fields and derived `succeeded` from that. A 4xx/5xx
 * whose body is JSON but not an ApiResult (ProblemDetails, a gateway's
 * `{"message":"rate limited"}`, a consumer action returning `BadRequest(obj)`)
 * therefore resolved as `succeeded: true / data: undefined`: `unwrapOk` passed,
 * the admin form showed a green "saved" toast and closed, and a 401 with such a
 * body never entered the refresh cycle. The XHR upload path already applied the
 * right rule (body wins only when it carries `code`). This pins the fetch path
 * and the helper to the same rule.
 */

const mockFetch = vi.fn();

function jsonResponse(body: unknown, status: number): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    json: () => Promise.resolve(body),
    headers: new Headers(),
  } as unknown as Response;
}

describe('normalizeApiResult with an HTTP status', () => {
  it('derives failure from a non-2xx status when the body has no envelope fields', () => {
    const result = normalizeApiResult<unknown>({ title: 'One or more validation errors occurred.', status: 400 }, 400);
    expect(result.succeeded).toBe(false);
    expect(result.success).toBe(false);
    expect(result.code).toBe(400);
    expect(result.message).toBe('One or more validation errors occurred.');
    expect(result.errorDetails).toEqual({ title: 'One or more validation errors occurred.', status: 400 });
  });

  it('prefers message, then title, then detail, then a generic HTTP line', () => {
    expect(normalizeApiResult({ message: 'm', title: 't', detail: 'd' }, 503).message).toBe('m');
    expect(normalizeApiResult({ title: 't', detail: 'd' }, 503).message).toBe('t');
    expect(normalizeApiResult({ detail: 'd' }, 503).message).toBe('d');
    expect(normalizeApiResult({ foo: 1 }, 503).message).toBe('HTTP 503');
  });

  it('applies the status rule to a null or scalar body too', () => {
    expect(normalizeApiResult(null, 502).succeeded).toBe(false);
    expect(normalizeApiResult(null, 502).code).toBe(502);
    expect(normalizeApiResult('gateway timeout' as never, 504)).toMatchObject({
      succeeded: false,
      code: 504,
      message: 'HTTP 504',
    });
  });

  it('lets an envelope body win over the status, as today', () => {
    const result = normalizeApiResult({ succeeded: false, code: 400, message: 'refused' }, 500);
    expect(result.code).toBe(400);
    expect(result.message).toBe('refused');
    const ok = normalizeApiResult({ succeeded: true, code: 200, data: 1 }, 500);
    expect(ok.succeeded).toBe(true);
  });

  it('passes a 2xx non-envelope object through as the payload, as today', () => {
    const result = normalizeApiResult<{ id: number }>({ id: 7 }, 200);
    expect(result.succeeded).toBe(true);
    expect(result.code).toBe(200);
  });

  it('is unchanged when no status is given', () => {
    const result = normalizeApiResult<{ id: number }>({ id: 7 });
    expect(result.succeeded).toBe(true);
    expect(result.code).toBe(200);
  });
});

describe('HttpClient fetch path: HTTP status vs non-envelope JSON body', () => {
  let client: HttpClient;

  beforeEach(() => {
    mockFetch.mockReset();
    vi.stubGlobal('fetch', mockFetch);
    client = new HttpClient({ baseUrl: '/api' });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('a 400 ProblemDetails body resolves as a failed envelope with the HTTP code', async () => {
    mockFetch.mockResolvedValue(
      jsonResponse({ title: 'One or more validation errors occurred.', status: 400, errors: { name: ['Required'] } }, 400)
    );
    const result = await client.post('/things', { name: '' });
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(400);
    expect(result.message).toBe('One or more validation errors occurred.');
  });

  it('a 429 gateway JSON page resolves as failed 429 with its message', async () => {
    mockFetch.mockResolvedValue(jsonResponse({ message: 'API rate limit exceeded' }, 429));
    const result = await client.get('/things');
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(429);
    expect(result.message).toBe('API rate limit exceeded');
  });

  it('a 503 with a JSON body resolves as failed 503', async () => {
    mockFetch.mockResolvedValue(jsonResponse({ message: 'upstream unavailable' }, 503));
    const result = await client.get('/things');
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(503);
  });

  it('a 401 with a non-envelope JSON body enters the refresh cycle and reports unauthorized when the retry still fails', async () => {
    const refreshTokenFn = vi.fn().mockResolvedValue('fresh-token');
    const onUnauthorized = vi.fn();
    const c = new HttpClient({ baseUrl: '/api', refreshTokenFn, onUnauthorized });
    c.setAccessToken('stale');
    mockFetch.mockResolvedValue(jsonResponse({ error: 'invalid_token' }, 401));

    const result = await c.get('/things');

    expect(refreshTokenFn).toHaveBeenCalledTimes(1);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(401);
  });

  it('a 200 with a non-envelope object still resolves as success (existing behaviour)', async () => {
    mockFetch.mockResolvedValue(jsonResponse({ id: 7 }, 200));
    const result = await client.get<{ id: number }>('/raw');
    expect(result.succeeded).toBe(true);
    expect(result.code).toBe(200);
  });

  it('a 500 whose body IS an envelope with code 400 keeps the envelope code', async () => {
    mockFetch.mockResolvedValue(jsonResponse({ succeeded: false, code: 400, message: 'refused' }, 500));
    const result = await client.get('/things');
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(400);
    expect(result.message).toBe('refused');
  });
});
