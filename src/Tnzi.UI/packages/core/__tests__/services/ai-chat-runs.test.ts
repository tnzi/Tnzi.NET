import { describe, it, expect, vi, afterEach } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import { useChatApi, useThreadApi } from '../../src/services/ai/api';
import { streamChat } from '../../src/services/ai/streaming';
import { AiChatErrorCodes } from '../../src/services/ai/metadata';

/**
 * The chat client calls a conversation with an external CLI agent needs: finding a run
 * after a refresh, reattaching to its stream, cancelling it on the server, paging a thread
 * back, and reading an attachment.
 */
function mockClient() {
  const calls: Array<{ method: string; url: string; options?: unknown }> = [];
  const ok = <T>(data: T) => Promise.resolve({ succeeded: true, data, code: 200 });
  const client = {
    get: vi.fn((url: string, options?: unknown) => {
      calls.push({ method: 'GET', url, options });
      return ok(null);
    }),
    post: vi.fn((url: string) => {
      calls.push({ method: 'POST', url });
      return ok(null);
    }),
    download: vi.fn((url: string) => {
      calls.push({ method: 'DOWNLOAD', url });
      return ok(new Blob(['x']));
    }),
    resolveUrl: vi.fn((path: string) => `http://localhost/api${path}`),
    calls,
  };
  return client as unknown as HttpClient & { calls: typeof calls };
}

describe('useChatApi run endpoints', () => {
  it('lists the active runs of a thread', async () => {
    const client = mockClient();
    await useChatApi(client).getActiveRuns('t-1');
    expect(client.calls).toEqual([{ method: 'GET', url: '/chat/threads/t-1/runs', options: undefined }]);
  });

  it('builds the reattach URL from the start or after a sequence', () => {
    const api = useChatApi(mockClient());
    expect(api.getRunStreamUrl('r-1')).toBe('http://localhost/api/chat/runs/r-1/stream?fromSequence=0');
    expect(api.getRunStreamUrl('r-1', 12)).toBe('http://localhost/api/chat/runs/r-1/stream?fromSequence=12');
  });

  it('cancels a run on the server', async () => {
    const client = mockClient();
    await useChatApi(client).cancelRun('r-1');
    expect(client.calls).toEqual([{ method: 'POST', url: '/chat/runs/r-1/cancel' }]);
  });
});

describe('useThreadApi paging and attachments', () => {
  it('pages messages back from an order', async () => {
    const client = mockClient();
    await useThreadApi(client).getMessages('t-1', { beforeOrder: 40, limit: 20 });
    expect(client.calls[0]).toEqual({
      method: 'GET', url: '/threads/t-1/messages', options: { params: { limit: 20, beforeOrder: 40 } },
    });
  });

  it('asks for the newest page when no order is given', async () => {
    const client = mockClient();
    await useThreadApi(client).getMessages('t-1');
    expect(client.calls[0].options).toEqual({ params: { limit: 50 } });
  });

  it('reads an attachment through the client', async () => {
    const client = mockClient();
    await useThreadApi(client).getAttachment('t-1', 'a-1');
    expect(client.calls).toEqual([{ method: 'DOWNLOAD', url: '/threads/t-1/attachments/a-1' }]);
  });
});

describe('streamChat reattach', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the run stream without a body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('data: {"eventType":"run_started","runId":"r-1"}\n\ndata: [DONE]\n\n', { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const events: unknown[] = [];

    const result = await streamChat({ url: 'http://x/stream', method: 'GET', onEvent: (e) => events.push(e) });

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe('GET');
    expect(init.body).toBeUndefined();
    expect((init.headers as Record<string, string>)['Content-Type']).toBeUndefined();
    expect(events).toEqual([{ eventType: 'run_started', runId: 'r-1' }]);
    expect(result.completed).toBe(true);
  });

  it('still POSTs the body by default', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('data: [DONE]\n\n', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await streamChat({ url: 'http://x/stream', body: { message: 'hi', metadata: { targets: ['pc-1'] } } });

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ message: 'hi', metadata: { targets: ['pc-1'] } });
  });
});

describe('AiChatErrorCodes', () => {
  it('names the usage limit code the server sends', () => {
    expect(AiChatErrorCodes.CliUsageLimit).toBe('AI_CLI_USAGE_LIMIT');
  });
});
