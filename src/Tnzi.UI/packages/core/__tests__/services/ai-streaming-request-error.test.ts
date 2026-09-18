import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { streamChat, ChatStreamRequestError } from '../../src/services/ai/streaming';

/**
 * A transport that snapshots the bearer token (`streamChat` is a raw fetch,
 * not an HttpClient request) has to tell an expired token apart from a server
 * failure to run the refresh-and-retry dance. The error carries the HTTP
 * status so callers do not parse the message text for it.
 */
describe('streamChat request errors', () => {
  let fetchSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function response(status: number, body = 'error body'): Response {
    return {
      ok: status >= 200 && status < 300,
      status,
      statusText: 'Error',
      body: new ReadableStream<Uint8Array>({ start: (c) => c.close() }),
      headers: new Headers(),
      text: async () => body,
    } as unknown as Response;
  }

  it('reports a non-OK response as ChatStreamRequestError carrying the status', async () => {
    let errorCaught: unknown = null;
    fetchSpy.mockResolvedValue(response(401));

    const result = await streamChat({
      url: 'http://localhost/api/chat/stream',
      body: { message: 'Hello' },
      onError: (e) => { errorCaught = e; },
    });

    expect(result.completed).toBe(false);
    expect(result.error).toBeInstanceOf(ChatStreamRequestError);
    expect((result.error as ChatStreamRequestError).status).toBe(401);
    expect(result.error?.message).toBe('Stream request failed: 401 error body');
    expect(errorCaught).toBe(result.error);
  });

  it('keeps the status on every non-OK response, not only 401', async () => {
    fetchSpy.mockResolvedValue(response(503, 'down'));

    const result = await streamChat({ url: 'http://localhost/api/chat/stream', body: { message: 'x' } });

    expect(result.error).toBeInstanceOf(ChatStreamRequestError);
    expect((result.error as ChatStreamRequestError).status).toBe(503);
  });

  it('is not raised for network failures (no response to carry a status)', async () => {
    fetchSpy.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await streamChat({ url: 'http://localhost/api/chat/stream', body: { message: 'x' } });

    expect(result.error).not.toBeNull();
    expect(result.error).not.toBeInstanceOf(ChatStreamRequestError);
  });
});
