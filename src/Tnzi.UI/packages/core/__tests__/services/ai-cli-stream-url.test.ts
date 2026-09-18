import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useAdminCliRunApi, useCliRunApi } from '../../src/services/ai/cli';
import { streamCliRun, lastCliRunSequence } from '../../src/services/ai/cli-stream';
import type { HttpClient } from '../../src/http/http';

/**
 * `streamCliRun` fetches `options.url` verbatim and documents it as the full
 * URL built by `streamUrl`. The builders used to return a bare `/admin/ai/...`
 * path with no baseUrl, so following the shipped recipe sent the request to the
 * SPA root: a dev-server 404, or under the usual `try_files ... /index.html`
 * fallback a 200 text/html body with no `data:` lines - the feed "finished"
 * instantly with zero events and no error.
 */

function mockClient(): HttpClient {
  return {
    resolveUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
      const qs = params
        ? '?' + Object.entries(params).map(([k, v]) => `${k}=${String(v)}`).join('&')
        : '';
      return `/api${path}${qs}`;
    }),
  } as unknown as HttpClient;
}

describe('cli run stream URL builders', () => {
  it('admin streamUrl resolves through the client baseUrl and carries fromSequence', () => {
    const url = useAdminCliRunApi(mockClient()).streamUrl('run-1', 7);
    expect(url).toBe('/api/admin/ai/cli-runs/run-1/stream?fromSequence=7');
  });

  it('user streamUrl resolves through the client baseUrl and defaults fromSequence to 0', () => {
    const url = useCliRunApi(mockClient()).streamUrl('run-1');
    expect(url).toBe('/api/ai/cli-runs/run-1/stream?fromSequence=0');
  });
});

describe('streamCliRun transport', () => {
  let fetchSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function sseResponse(frames: string[]): Response {
    const encoder = new TextEncoder();
    return {
      ok: true,
      status: 200,
      statusText: 'OK',
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      body: new ReadableStream<Uint8Array>({
        start(controller) {
          for (const frame of frames) controller.enqueue(encoder.encode(frame));
          controller.close();
        },
      }),
    } as unknown as Response;
  }

  it('fetches exactly the url it was given', async () => {
    fetchSpy.mockResolvedValue(sseResponse([]));
    await streamCliRun({ url: '/api/admin/ai/cli-runs/run-1/stream?fromSequence=0' });
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(fetchSpy.mock.calls[0][0]).toBe('/api/admin/ai/cli-runs/run-1/stream?fromSequence=0');
  });

  it('parses data frames into text and completes on a clean end of body', async () => {
    const frames = [
      'data: {"type":"Text","content":"Hel"}\n\n',
      'data: {"type":"Text","content":"lo"}\n\n',
    ];
    fetchSpy.mockResolvedValue(sseResponse(frames));
    const result = await streamCliRun({ url: '/api/x' });
    expect(result.completed).toBe(true);
    expect(result.error).toBeNull();
    expect(result.text).toBe('Hello');
    expect(result.events).toHaveLength(2);
  });

  it('lastCliRunSequence reports where a live stream should resume from', () => {
    const messages = [{ sequence: 3 }, { sequence: 9 }, { sequence: 5 }] as unknown as Parameters<
      typeof lastCliRunSequence
    >[0];
    expect(lastCliRunSequence(messages)).toBe(9);
    expect(lastCliRunSequence([])).toBe(0);
  });
});
