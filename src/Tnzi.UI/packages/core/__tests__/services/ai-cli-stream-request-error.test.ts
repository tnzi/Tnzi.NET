import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { streamCliRun } from '../../src/services/ai/cli-stream';
import { StreamRequestError } from '../../src/services/ai/stream-request-error';
import { ChatStreamRequestError } from '../../src/services/ai/streaming';

/**
 * `streamCliRun` is the second raw-fetch transport in `services/ai`. The 401
 * refresh dance documented for `streamChat` keys off a typed error carrying the
 * HTTP status; a consumer following that pattern for a CLI run live feed must
 * get the same signal, not a plain `Error` whose status is buried in the text.
 */
describe('streamCliRun request errors', () => {
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

  it('reports a non-OK response as StreamRequestError carrying the status', async () => {
    let errorCaught: unknown = null;
    fetchSpy.mockResolvedValue(response(401));

    const result = await streamCliRun({
      url: 'http://localhost/api/admin/ai/cli-runs/r1/stream',
      onError: (e) => { errorCaught = e; },
    });

    expect(result.completed).toBe(false);
    expect(result.error).toBeInstanceOf(StreamRequestError);
    expect((result.error as StreamRequestError).status).toBe(401);
    expect(result.error?.message).toBe('CLI run stream failed: 401 error body');
    expect(errorCaught).toBe(result.error);
  });

  it('keeps the status on every non-OK response, not only 401', async () => {
    fetchSpy.mockResolvedValue(response(503, 'down'));

    const result = await streamCliRun({ url: 'http://localhost/api/admin/ai/cli-runs/r1/stream' });

    expect(result.error).toBeInstanceOf(StreamRequestError);
    expect((result.error as StreamRequestError).status).toBe(503);
  });

  it('is not raised for network failures (no response to carry a status)', async () => {
    fetchSpy.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await streamCliRun({ url: 'http://localhost/api/admin/ai/cli-runs/r1/stream' });

    expect(result.error).not.toBeNull();
    expect(result.error).not.toBeInstanceOf(StreamRequestError);
  });

  it('shares the base class with streamChat so one branch covers both transports', () => {
    const chat = new ChatStreamRequestError(401, 'x');
    expect(chat).toBeInstanceOf(StreamRequestError);
    expect(chat.status).toBe(401);
    // The chat error keeps its own name and message shape for existing consumers.
    expect(chat.name).toBe('ChatStreamRequestError');
    expect(chat.message).toBe('Stream request failed: 401 x');
  });
});
