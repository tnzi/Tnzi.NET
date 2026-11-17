import { describe, it, expect, vi, beforeEach } from 'vitest';
import { watch } from 'vue';

/**
 * `useChatThreads.refreshThread`: the open thread read again and a run started elsewhere (the
 * app's server sending a queued message) followed, without the view going empty in between.
 */
type StreamOpts = Record<string, unknown>;
let streamRunner: (opts: StreamOpts) => Promise<{ text: string; reasoning?: string }>;
class ChatStreamRequestError extends Error {
  constructor(readonly status: number, readonly detail = '') {
    super(`Stream request failed: ${status} ${detail}`);
  }
}
vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: (opts: StreamOpts) => streamRunner(opts),
  ChatStreamRequestError,
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');

type Cb<T> = (v: T) => void;

const firstPage = [
  { id: 'u1', role: 'user', content: 'first', creationTime: '2026-11-07T10:00:00Z', order: 1 },
  { id: 'a1', role: 'assistant', content: 'one', creationTime: '2026-11-07T10:00:01Z', order: 2 },
];
const queuedPage = [
  ...firstPage,
  { id: 'u2', role: 'user', content: 'queued question', creationTime: '2026-11-07T10:01:00Z', order: 3 },
];

function deps(pages: unknown[][], runs: unknown[][]) {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi: {
      getList: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0 } })),
      getMessages: vi.fn(async () => ({ succeeded: true, data: { messages: pages.shift() ?? queuedPage, hasMore: false } })),
      getAttachment: vi.fn(async () => ({ succeeded: true, data: new Blob(['x']) })),
      delete: vi.fn(async () => ({ succeeded: true })),
    },
    chatApi: {
      getChatStreamUrl: () => '/api/chat/stream',
      getActiveRuns: vi.fn(async () => ({ succeeded: true, data: runs.shift() ?? [] })),
      getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
    },
  };
}

beforeEach(() => {
  sessionStorage.clear();
  streamRunner = async () => ({ text: '' });
});

describe('refreshThread', () => {
  it('shows the thread as the server has it and follows the run started there', async () => {
    const d = deps([firstPage, queuedPage], [[], [{ runId: 'r-2', userMessageId: 'u2', status: 'Running' }]]);
    const calls: StreamOpts[] = [];
    let finish: (() => void) | undefined;
    streamRunner = async (opts) => {
      calls.push(opts);
      (opts.onDelta as Cb<string>)('the answer');
      await new Promise<void>((r) => (finish = r));
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'a2', runId: 'r-2' });
      return { text: 'the answer' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    await vi.waitFor(() => expect(d.chatApi.getActiveRuns).toHaveBeenCalledTimes(1));
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1']);

    const lengths: number[] = [];
    const stopWatching = watch(chat.messages, (m) => lengths.push(m.length), { flush: 'sync' });
    await chat.refreshThread();
    await vi.waitFor(() => expect(calls).toHaveLength(1));
    stopWatching();

    expect(lengths.every((n) => n > 0)).toBe(true);
    expect(calls[0].url).toBe('/api/chat/runs/r-2/stream?fromSequence=0');
    expect(chat.activeThreadId.value).toBe('t1');
    expect(chat.activeRunId.value).toBe('r-2');
    expect(chat.isStreaming.value).toBe(true);
    expect(chat.messages.value.slice(0, 3).map((m) => m.id)).toEqual(['u1', 'a1', 'u2']);

    finish?.();
    await vi.waitFor(() => expect(chat.isStreaming.value).toBe(false));
    expect(chat.messages.value[chat.messages.value.length - 1]).toMatchObject({ id: 'a2', content: 'the answer' });
  });

  it('with no run going only shows the newer transcript', async () => {
    const d = deps([firstPage, queuedPage], [[], []]);
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    await chat.refreshThread();
    await vi.waitFor(() => expect(d.chatApi.getActiveRuns).toHaveBeenCalledTimes(2));
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2']);
    expect(chat.isStreaming.value).toBe(false);
  });

  it('does nothing without a saved open thread', async () => {
    const d = deps([], []);
    const chat = useChatThreads(d as never);
    await chat.refreshThread();
    expect(d.threadApi.getMessages).not.toHaveBeenCalled();
  });

  it('asked while a turn streams here, it is kept and done when that turn ends (a turn the server started meanwhile is not missed)', async () => {
    const d = deps([firstPage, queuedPage], [[], []]);
    let release: (() => void) | undefined;
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1', threadId: 't1' });
      await new Promise<void>((r) => (release = r));
      return { text: '' };
    };
    const turn = chat.send('now');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));
    await chat.refreshThread();
    expect(d.threadApi.getMessages).toHaveBeenCalledTimes(1);

    chat.abort();
    release?.();
    await turn;
    await vi.waitFor(() => expect(d.threadApi.getMessages).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2']));
  });

  it('a refresh kept for a thread the user then left is dropped', async () => {
    const d = deps([firstPage, firstPage], [[], [], []]);
    let release: (() => void) | undefined;
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1', threadId: 't1' });
      await new Promise<void>((r) => (release = r));
      return { text: '' };
    };
    const turn = chat.send('now');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));
    await chat.refreshThread();

    chat.newChat();
    release?.();
    await turn;
    await new Promise((r) => setTimeout(r, 20));
    expect(d.threadApi.getMessages).toHaveBeenCalledTimes(1);
  });

  it('a failed read leaves the screen as it was and reports it', async () => {
    const onError = vi.fn();
    const d = deps([firstPage], [[]]);
    const chat = useChatThreads({ ...(d as object), onError } as never);
    await chat.selectThread('t1');
    d.threadApi.getMessages.mockImplementationOnce(async () => {
      throw new Error('offline');
    });
    await chat.refreshThread();
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1']);
    expect(onError).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ operation: 'load-thread' }));
  });

  it('lets a turn waiting on background tasks go, without cancelling it, and follows the new run', async () => {
    const tasks = [{ id: 'b1', description: 'Build', kind: 'local_bash' }];
    const d = deps([[], queuedPage], [[], [{ runId: 'r-2', userMessageId: 'u2', status: 'Running' }]]);
    let waitingReleased = false;
    const requests: StreamOpts[] = [];
    streamRunner = async (opts) => {
      requests.push(opts);
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      if (requests.length === 1) {
        onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'u1', threadId: 't1' });
        (opts.onDelta as Cb<string>)('one');
        onEvent({ eventType: 'turn_completed', eventData: { assistantMessageId: 'a1', backgroundTasks: tasks } });
        await new Promise<void>((resolve) => {
          (opts.signal as AbortSignal).addEventListener('abort', () => {
            waitingReleased = true;
            resolve();
          });
        });
        return { text: 'one' };
      }
      (opts.onDelta as Cb<string>)('the queued answer');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'a2', runId: 'r-2' });
      return { text: 'the queued answer' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    await vi.waitFor(() => expect(d.chatApi.getActiveRuns).toHaveBeenCalledTimes(1));
    const first = chat.send('first');
    await vi.waitFor(() => expect(chat.messages.value.some((m) => m.status === 'waiting')).toBe(true));

    await chat.refreshThread();
    await first;
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    await vi.waitFor(() => expect(chat.isStreaming.value).toBe(false));

    expect(waitingReleased).toBe(true);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(requests[1].url).toBe('/api/chat/runs/r-2/stream?fromSequence=0');
    expect(chat.messages.value.some((m) => m.status === 'waiting')).toBe(false);
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2', 'a2']);
  });
});
