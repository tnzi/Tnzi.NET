import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

/**
 * `useChatThreads` when a turn's stream breaks for a network reason (a phone froze the page
 * in the background, the connection dropped): the turn is not failed on that alone. The hook
 * asks the server how it stands (a run's stream is picked up after the last event seen, or
 * the thread is read back), again when the page comes back, and Retry asks before it re-runs.
 */
type StreamOpts = Record<string, unknown>;
type Cb<T> = (v: T) => void;
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

/** What `streamChat` does with one frame: the event, then its delta, then done. */
function emit(opts: StreamOpts, event: Record<string, unknown>): void {
  (opts.onEvent as Cb<Record<string, unknown>>)(event);
  if (typeof event.delta === 'string') (opts.onDelta as Cb<string>)(event.delta);
  if (event.isDone) (opts.onDone as Cb<Record<string, unknown>>)(event);
}

/** The fetch of a frozen page rejects like this when the page comes back. */
const loadFailed = () => new TypeError('Load failed');

function deps(overrides: { threadApi?: Record<string, unknown>; chatApi?: Record<string, unknown> } = {}) {
  const threadApi = {
    getList: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0 } })),
    getMessages: vi.fn(async () => ({
      succeeded: true,
      data: {
        messages: [
          { id: 'u1', role: 'user', content: 'first', creationTime: '2026-10-25T10:00:00Z', order: 1 },
          { id: 'a1', role: 'assistant', content: 'one', creationTime: '2026-10-25T10:00:01Z', order: 2 },
        ],
        hasMore: false,
      },
    })),
    getAttachment: vi.fn(async () => ({ succeeded: true, data: new Blob(['x']) })),
    delete: vi.fn(async () => ({ succeeded: true })),
    ...overrides.threadApi,
  };
  const chatApi = {
    getChatStreamUrl: () => '/api/chat/stream',
    getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] as unknown[] })),
    getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
    cancelRun: vi.fn(async () => ({ succeeded: true })),
    ...overrides.chatApi,
  };
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi,
    chatApi,
  };
}

let visibility: DocumentVisibilityState = 'visible';
function setVisibility(next: DocumentVisibilityState): void {
  visibility = next;
  document.dispatchEvent(new Event('visibilitychange'));
}

/** The first frames of a turn answered by an external CLI run, in a new conversation. */
function runStarted(opts: StreamOpts): void {
  emit(opts, { eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-new', sequence: 1 });
  emit(opts, { delta: 'Hel', threadId: 't-new', sequence: 2 });
}

const reply = (chat: { messages: { value: Array<Record<string, unknown>> } }) =>
  chat.messages.value[chat.messages.value.length - 1];

beforeEach(() => {
  streamRunner = async () => ({ text: 'done' });
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
  visibility = 'visible';
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('a run whose stream breaks mid-turn', () => {
  it('picks the run up after the last event seen and keeps showing it in progress', async () => {
    const d = deps();
    const calls: StreamOpts[] = [];
    let finish: (() => void) | undefined;
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: 'Hel' };
      }
      // Still running on the server: what it writes keeps arriving.
      emit(opts, { eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-new' });
      emit(opts, { delta: 'lo', threadId: 't-new', sequence: 3 });
      await new Promise<void>((r) => (finish = r));
      emit(opts, { delta: ' world', threadId: 't-new', sequence: 4 });
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a', userMessageId: 'srv-u', runId: 'r-1' });
      return { text: 'lo world' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('long task');

    await vi.waitFor(() => expect(calls).toHaveLength(2));
    expect(calls[1].method).toBe('GET');
    expect(calls[1].url).toBe('/api/chat/runs/r-1/stream?fromSequence=2');
    await vi.waitFor(() => expect(reply(chat).content).toBe('Hello'));
    expect(reply(chat).status).toBe('streaming');
    expect(chat.isStreaming.value).toBe(true);
    expect(chat.activeRunId.value).toBe('r-1');

    finish?.();
    await turn;
    expect(reply(chat)).toMatchObject({ id: 'srv-a', content: 'Hello world', status: 'done' });
    expect(reply(chat).error ?? null).toBeNull();
    expect(chat.isStreaming.value).toBe(false);
    expect(chat.activeThreadId.value).toBe('t-new');
  });

  it('a run that finished while the page was hidden shows its answer when the page is shown', async () => {
    const d = deps();
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        visibility = 'hidden';
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: 'Hel' };
      }
      // Long finished: the rest is replayed from storage, then the run's outcome.
      emit(opts, { eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-new' });
      emit(opts, { delta: 'lo', threadId: 't-new', sequence: 3 });
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a', runId: 'r-1' });
      return { text: 'lo' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('long task');

    // Hidden: nothing is asked yet, and the turn is not shown as failed.
    await vi.waitFor(() => expect(calls).toHaveLength(1));
    await Promise.resolve();
    expect(calls).toHaveLength(1);
    expect(reply(chat).status).toBe('streaming');
    expect(reply(chat).error).toBeFalsy();

    setVisibility('visible');
    await turn;
    expect(calls).toHaveLength(2);
    expect(calls[1].url).toBe('/api/chat/runs/r-1/stream?fromSequence=2');
    expect(reply(chat)).toMatchObject({ id: 'srv-a', content: 'Hello', status: 'done' });
    expect(reply(chat).error ?? null).toBeNull();
  });

  it('a stream that ends without the run outcome is picked up again too', async () => {
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        return { text: 'Hel' };
      }
      emit(opts, { delta: 'lo', sequence: 3 });
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a' });
      return { text: 'lo' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('long task');
    expect(calls[1].url).toBe('/api/chat/runs/r-1/stream?fromSequence=2');
    expect(reply(chat)).toMatchObject({ content: 'Hello', status: 'done' });
  });

  it('a run the server no longer has: the thread says how the turn ended', async () => {
    const d = deps({
      threadApi: {
        getMessages: vi.fn(async () => ({
          succeeded: true,
          data: {
            messages: [
              { id: 'srv-u', role: 'user', content: 'long task', creationTime: '2026-10-25T10:00:00Z', order: 1 },
              { id: 'srv-a', role: 'assistant', content: 'Hello', creationTime: '2026-10-25T10:00:09Z', order: 2 },
            ],
            hasMore: false,
          },
        })),
      },
    });
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: 'Hel' };
      }
      (opts.onError as Cb<Error>)(new ChatStreamRequestError(404, JSON.stringify({ message: 'Run not found.' })));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.send('long task');

    expect(d.threadApi.getMessages).toHaveBeenCalledWith('t-new', expect.anything());
    expect(chat.messages.value.map((m) => m.id)).toEqual(['srv-u', 'srv-a']);
    expect(reply(chat).content).toBe('Hello');
    expect(reply(chat).status).not.toBe('error');
    expect(chat.activeThreadId.value).toBe('t-new');
    expect(chat.isStreaming.value).toBe(false);
  });
});

describe('a turn without a run whose stream breaks', () => {
  it('reads the thread back and shows the stored answer instead of an error', async () => {
    const d = deps();
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    d.threadApi.getMessages.mockResolvedValueOnce({
      succeeded: true,
      data: {
        messages: [
          { id: 'u1', role: 'user', content: 'first', creationTime: '2026-10-25T10:00:00Z', order: 1 },
          { id: 'a1', role: 'assistant', content: 'one', creationTime: '2026-10-25T10:00:01Z', order: 2 },
          { id: 'u2', role: 'user', content: 'second', creationTime: '2026-10-25T10:01:00Z', order: 3 },
          { id: 'a2', role: 'assistant', content: 'two', creationTime: '2026-10-25T10:01:05Z', order: 4 },
        ],
        hasMore: false,
      },
    });
    streamRunner = async (opts) => {
      emit(opts, { delta: 't', threadId: 't1' });
      (opts.onError as Cb<Error>)(loadFailed());
      return { text: 't' };
    };
    await chat.send('second');

    expect(d.chatApi.getActiveRuns).toHaveBeenCalledWith('t1');
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2', 'a2']);
    expect(reply(chat)).toMatchObject({ content: 'two' });
    expect(reply(chat).status).not.toBe('error');
  });

  it('a run the page never heard of is followed from the start', async () => {
    const d = deps();
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    // The reattach on open found nothing; this turn's run only shows up on the re-sync.
    await vi.waitFor(() => expect(d.chatApi.getActiveRuns).toHaveBeenCalledTimes(1));
    d.chatApi.getActiveRuns.mockResolvedValueOnce({ succeeded: true, data: [{ runId: 'r-7', userMessageId: null, status: 'Running' }] });
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: '' };
      }
      emit(opts, { delta: 'from the run' });
      emit(opts, { isDone: true, threadId: 't1', assistantMessageId: 'a2' });
      return { text: 'from the run' };
    };
    await chat.send('second');

    expect(calls[1].url).toBe('/api/chat/runs/r-7/stream?fromSequence=0');
    expect(reply(chat)).toMatchObject({ id: 'a2', content: 'from the run', status: 'done' });
  });
});

describe('a server that cannot be reached', () => {
  it('shows the network error only after the re-sync fails, and resolves the turn once the page is back online', async () => {
    vi.useFakeTimers();
    const d = deps();
    const calls: StreamOpts[] = [];
    let reachable = false;
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: 'Hel' };
      }
      if (!reachable) {
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: '' };
      }
      emit(opts, { delta: 'lo', sequence: 3 });
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a' });
      return { text: 'lo' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('long task');

    // The first re-sync failed: still in progress while the second one waits.
    await vi.waitFor(() => expect(calls).toHaveLength(2));
    expect(reply(chat).status).toBe('streaming');
    await vi.advanceTimersByTimeAsync(2_000);
    await turn;
    expect(calls).toHaveLength(3);
    expect(reply(chat)).toMatchObject({ status: 'error', error: 'Load failed', content: 'Hel' });
    expect(chat.isStreaming.value).toBe(false);
    // The conversation is the server's thread, so the list and Retry address it.
    expect(chat.activeThreadId.value).toBe('t-new');

    reachable = true;
    window.dispatchEvent(new Event('online'));
    await vi.waitFor(() => expect(reply(chat).status).toBe('done'));
    expect(calls[3].url).toBe('/api/chat/runs/r-1/stream?fromSequence=2');
    expect(reply(chat)).toMatchObject({ id: 'srv-a', content: 'Hello', error: null });
  });

  it('Retry on that reply asks the server and does not send the turn again', async () => {
    vi.useFakeTimers();
    const calls: StreamOpts[] = [];
    let reachable = false;
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: 'Hel' };
      }
      if (!reachable) {
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: '' };
      }
      emit(opts, { delta: 'lo', sequence: 3 });
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a' });
      return { text: 'lo' };
    };
    const chat = useChatThreads(deps() as never);
    const turn = chat.send('long task');
    await vi.advanceTimersByTimeAsync(2_000);
    await turn;
    expect(reply(chat).status).toBe('error');

    reachable = true;
    await chat.regenerate();
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(1);
    expect(calls[calls.length - 1].method).toBe('GET');
    expect(reply(chat)).toMatchObject({ content: 'Hello', status: 'done' });
  });

  it('Retry re-runs the turn once the server says it has no reply and no run for it', async () => {
    vi.useFakeTimers();
    let reachable = false;
    const d = deps({
      chatApi: {
        getActiveRuns: vi.fn(async () => (reachable ? { succeeded: true, data: [] } : { succeeded: false, code: 0, message: 'Network error' })),
      },
    });
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        (opts.onError as Cb<Error>)(loadFailed());
        return { text: '' };
      }
      emit(opts, { delta: 'answer' });
      emit(opts, { isDone: true, threadId: 't1', assistantMessageId: 'a2', userMessageId: 'u2' });
      return { text: 'answer' };
    };
    const turn = chat.send('second');
    await vi.advanceTimersByTimeAsync(2_000);
    await turn;
    expect(reply(chat).status).toBe('error');

    // Reachable now: the thread has the question but no reply, and no run is on it.
    reachable = true;
    d.threadApi.getMessages.mockResolvedValueOnce({
      succeeded: true,
      data: {
        messages: [
          { id: 'u1', role: 'user', content: 'first', creationTime: '2026-10-25T10:00:00Z', order: 1 },
          { id: 'a1', role: 'assistant', content: 'one', creationTime: '2026-10-25T10:00:01Z', order: 2 },
          { id: 'u2', role: 'user', content: 'second', creationTime: '2026-10-25T10:01:00Z', order: 3 },
        ],
        hasMore: false,
      },
    });
    await chat.regenerate();

    expect(calls).toHaveLength(2);
    expect(calls[1].body).toMatchObject({ threadId: 't1', regenerate: true });
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2', 'a2']);
    expect(reply(chat)).toMatchObject({ content: 'answer', status: 'done' });
  });
});

describe('the page coming back while a run streams', () => {
  it('a stream that sat in a hidden page is picked up again after the last event', async () => {
    let now = 1_000_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      if (calls.length === 1) {
        runStarted(opts);
        // A connection that went dead without the fetch ever saying so: only closing it ends it.
        await new Promise<void>((r) => (opts.signal as AbortSignal).addEventListener('abort', () => r()));
        return { text: 'Hel' };
      }
      emit(opts, { delta: 'lo', sequence: 3 });
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a' });
      return { text: 'lo' };
    };
    const chat = useChatThreads(deps() as never);
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));

    setVisibility('hidden');
    now += 60_000;
    setVisibility('visible');
    await turn;

    expect(calls[1].url).toBe('/api/chat/runs/r-1/stream?fromSequence=2');
    expect(reply(chat)).toMatchObject({ content: 'Hello', status: 'done' });
  });

  it('a short trip away leaves a live stream alone', async () => {
    let now = 1_000_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    const calls: StreamOpts[] = [];
    let finish: (() => void) | undefined;
    streamRunner = async (opts) => {
      calls.push(opts);
      runStarted(opts);
      await new Promise<void>((r) => (finish = r));
      emit(opts, { isDone: true, threadId: 't-new', assistantMessageId: 'srv-a' });
      return { text: 'Hel' };
    };
    const chat = useChatThreads(deps() as never);
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));

    setVisibility('hidden');
    now += 1_000;
    setVisibility('visible');
    window.dispatchEvent(new Event('focus'));
    finish?.();
    await turn;
    expect(calls).toHaveLength(1);
    expect(reply(chat).status).toBe('done');
  });

  it('Stop while the recovery waits for the page ends it: nothing is asked afterwards', async () => {
    const d = deps();
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      runStarted(opts);
      visibility = 'hidden';
      (opts.onError as Cb<Error>)(loadFailed());
      return { text: 'Hel' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(calls).toHaveLength(1));

    chat.abort();
    await turn;
    setVisibility('visible');
    await Promise.resolve();
    expect(calls).toHaveLength(1);
    expect(d.chatApi.cancelRun).toHaveBeenCalledWith('r-1');
    expect(reply(chat).status).toBe('stopped');
  });
});
