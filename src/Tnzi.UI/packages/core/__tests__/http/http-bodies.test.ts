import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { HttpClient } from '../../src/http/http';
import { normalizeApiResult } from '../../src/http/response';

/**
 * Bodies at the edges of "is there anything here": a request body that is
 * falsy but present (`false`, `0`, `''`), and a response that is a bare
 * document or carries no body at all.
 */

const mockFetch = vi.fn();

/**
 * A fetch Response whose body is `text` (empty by default). `json()` is
 * deliberately absent: the client reads the body as text, because only then
 * can it tell an empty body from a malformed one.
 */
function response(init: { status?: number; text?: () => Promise<string>; headers?: Record<string, string> }): Response {
  const status = init.status ?? 200;
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: 'X',
    text: init.text ?? (() => Promise.resolve('')),
    headers: new Headers(init.headers ?? {}),
  } as unknown as Response;
}

function body(value: unknown): () => Promise<string> {
  return () => Promise.resolve(JSON.stringify(value));
}

function envelope(data: unknown = null): Response {
  return response({ text: body({ succeeded: true, code: 200, data }) });
}

describe('HttpClient request bodies', () => {
  let client: HttpClient;

  beforeEach(() => {
    mockFetch.mockReset();
    vi.stubGlobal('fetch', mockFetch);
    client = new HttpClient({ baseUrl: '/api' });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it.each([
    ['false', false],
    ['0', 0],
    ['""', ''],
  ])('sends %s as a JSON body instead of dropping it', async (wire, body) => {
    mockFetch.mockResolvedValue(envelope());
    await client.put('/flags/x', body);
    const init = mockFetch.mock.calls[0]![1] as RequestInit;
    expect(init.body).toBe(wire);
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
  });

  it('still sends no body for undefined or null', async () => {
    mockFetch.mockResolvedValue(envelope());
    await client.post('/ping');
    await client.post('/ping', null);
    expect((mockFetch.mock.calls[0]![1] as RequestInit).body).toBeUndefined();
    expect((mockFetch.mock.calls[1]![1] as RequestInit).body).toBeUndefined();
  });
});

describe('HttpClient response bodies', () => {
  let client: HttpClient;

  beforeEach(() => {
    mockFetch.mockReset();
    vi.stubGlobal('fetch', mockFetch);
    client = new HttpClient({ baseUrl: '/api' });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('hands back a bare 2xx document as the payload', async () => {
    const challenge = { algorithm: 'SHA-256', challenge: 'abc', salt: 's', signature: 'sig' };
    mockFetch.mockResolvedValue(response({ text: body(challenge) }));
    const result = await client.get<typeof challenge>('/captcha/altcha/challenge');
    expect(result.succeeded).toBe(true);
    expect(result.data).toEqual(challenge);
  });

  it('treats a 204 with no body as a success', async () => {
    const text = vi.fn(() => Promise.resolve(''));
    mockFetch.mockResolvedValue(response({ status: 204, text }));
    const result = await client.delete('/things/1');
    expect(result.succeeded).toBe(true);
    expect(result.code).toBe(204);
    expect(result.data).toBeUndefined();
    expect(text).not.toHaveBeenCalled();
  });

  it('treats a 200 that declares a zero length as a success', async () => {
    mockFetch.mockResolvedValue(response({ status: 200, headers: { 'content-length': '0' } }));
    const result = await client.post('/things/1/touch');
    expect(result.succeeded).toBe(true);
  });

  it('treats a chunked empty 2xx (no Content-Length) as a success with no data', async () => {
    // Chunked transfer declares no length: only reading the body shows it is empty.
    mockFetch.mockResolvedValue(response({ status: 200, headers: { 'transfer-encoding': 'chunked' } }));
    const result = await client.post('/things/1/touch');
    expect(result).toMatchObject({ succeeded: true, code: 200, data: undefined });
  });

  it.each([200, 201, 202])('treats an empty %i streamed through a real Response as a success', async (status) => {
    const empty = new ReadableStream<Uint8Array>({ start: (controller) => controller.close() });
    const real = new Response(empty, { status });
    expect(real.headers.get('content-length')).toBeNull();
    mockFetch.mockResolvedValue(real);
    const result = await client.post('/things');
    expect(result).toMatchObject({ succeeded: true, code: status, data: undefined });
  });

  it('still fails a 200 whose body is not JSON', async () => {
    mockFetch.mockResolvedValue(response({ status: 200, text: () => Promise.resolve('<!doctype html><html></html>') }));
    const result = await client.get('/page');
    expect(result.succeeded).toBe(false);
    expect(result.message).toBe('Invalid JSON response');
  });

  it('still fails a chunked 200 whose body is truncated JSON', async () => {
    mockFetch.mockResolvedValue(new Response('{"succeeded":true,', { status: 200 }));
    const result = await client.get('/things');
    expect(result.succeeded).toBe(false);
    expect(result.message).toBe('Invalid JSON response');
  });

  it('still fails a non-2xx whose chunked body is empty', async () => {
    mockFetch.mockResolvedValue(response({ status: 502 }));
    const result = await client.get('/things');
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(502);
  });

  it('still fails a non-2xx with no body', async () => {
    mockFetch.mockResolvedValue(response({ status: 503, headers: { 'content-length': '0' } }));
    const result = await client.get('/things');
    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(503);
  });
});

describe('normalizeApiResult with a non-envelope body', () => {
  it('returns a 2xx object as the payload', () => {
    expect(normalizeApiResult({ id: 1, name: 'x' }, 200).data).toEqual({ id: 1, name: 'x' });
    expect(normalizeApiResult({ id: 1 }, 201)).toMatchObject({ succeeded: true, code: 201, data: { id: 1 } });
  });

  it('returns the object as the payload when no status is given', () => {
    expect(normalizeApiResult({ id: 7 }).data).toEqual({ id: 7 });
  });

  it('leaves envelopes as they are', () => {
    expect(normalizeApiResult({ succeeded: true, code: 200, data: { id: 1 } }, 200).data).toEqual({ id: 1 });
  });

  it('still fails a non-2xx object', () => {
    expect(normalizeApiResult({ title: 'Bad' }, 400)).toMatchObject({ succeeded: false, code: 400 });
  });
});

describe('upload response bodies', () => {
  class FakeXhr {
    static reply: { status: number; text: string } = { status: 200, text: '' };
    status = 0;
    statusText = '';
    responseText = '';
    timeout = 0;
    withCredentials = false;
    upload = { onprogress: null as unknown, onload: null as unknown };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    ontimeout: (() => void) | null = null;
    onabort: (() => void) | null = null;
    onprogress: (() => void) | null = null;
    open() {}
    setRequestHeader() {}
    send() {
      this.status = FakeXhr.reply.status;
      this.responseText = FakeXhr.reply.text;
      queueMicrotask(() => this.onload?.());
    }
  }

  beforeEach(() => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('treats an upload answered with 204 and no body as a success', async () => {
    FakeXhr.reply = { status: 204, text: '' };
    const result = await new HttpClient({ baseUrl: '/api' }).uploadFormData('/files', new FormData());
    expect(result.succeeded).toBe(true);
    expect(result.code).toBe(204);
  });

  it('hands back a bare upload document as the payload', async () => {
    FakeXhr.reply = { status: 200, text: JSON.stringify({ id: 'f1', size: 3 }) };
    const result = await new HttpClient({ baseUrl: '/api' }).uploadFormData<{ id: string }>('/files', new FormData());
    expect(result.succeeded).toBe(true);
    expect(result.data).toEqual({ id: 'f1', size: 3 });
  });
});
