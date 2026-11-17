import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { watch } from 'vue';

/**
 * `useChatThreads` when the person moves away from a turn in progress (another conversation,
 * New chat) and when they press Stop. Only Stop cancels anything on the server: moving away
 * closes the stream on screen, a run goes on, and coming back picks it up again.
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

const transcripts: Record<string, Array<Record<string, unknown>>> = {
  t1: [
    { id: 'u1', role: 'user', content: 'build it', creationTime: '2026-11-04T10:00:00Z', order: 1 },
  ],
  t2: [
    { id: 'u9', role: 'user', content: 'other', creationTime: '2026-11-04T09:00:00Z', order: 1 },
    { id: 'a9', role: 'assistant', content: 'other answer', creationTime: '2026-11-04T09:00:01Z', order: 2 },
  ],
};

/** Runs still going, by thread. */
let activeRuns: Record<string, Array<Record<string, unknown>>> = {};

function deps() {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi: {
      getList: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0 } })),
      getMessages: vi.fn(async (id: string) => ({ succeeded: true, data: { messages: transcripts[id] ?? [], hasMore: false } })),
      getAttachment: vi.fn(async () => ({ succeeded: true, data: new Blob(['x']) })),
      delete: vi.fn(async () => ({ succeeded: true })),
    },
    chatApi: {
      getChatStreamUrl: () => '/api/chat/stream',
      getActiveRuns: vi.fn(async (threadId: string) => ({ succeeded: true, data: activeRuns[threadId] ?? [] })),
      getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
    },
  };
}

/** Resolves when the request is closed. */
const closed = (opts: StreamOpts) =>
  new Promise<void>((resolve) => (opts.signal as AbortSignal).addEventListener('abort', () => resolve()));

/** Every status ever written to a row, to show none was marked stopped along the way. */
function statusesOf(chat: { messages: { value: Array<{ status?: string }> } }): Set<string | undefined> {
  const seen = new Set<string | undefined>();
  watch(() => chat.messages.value, (rows) => rows.forEach((m) => seen.add(m.status)), { flush: 'sync' });
  return seen;
}

let visibility: DocumentVisibilityState = 'visible';
function setVisibility(next: DocumentVisibilityState): void {
  visibility = next;
  document.dispatchEvent(new Event('visibilitychange'));
}

beforeEach(() => {
  activeRuns = {};
  streamRunner = async () => ({ text: 'done' });
  sessionStorage.clear();
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
  visibility = 'visible';
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('moving to another conversation', () => {
  it('closes the stream of a run in progress without cancelling it, and coming back shows it still going', async () => {
    const d = deps();
    const calls: StreamOpts[] = [];
    let firstClosed: Promise<void> | undefined;
    streamRunner = async (opts) => {
      calls.push(opts);
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      if (calls.length === 1) {
        firstClosed = closed(opts);
        onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'u1', threadId: 't1', sequence: 1 });
        (opts.onDelta as Cb<string>)('Working on it');
        await firstClosed;
        return { text: '' };
      }
      // Coming back: the run's stream from the start, still writing.
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'u1', threadId: 't1' });
      (opts.onDelta as Cb<string>)('Working on it, still');
      await closed(opts);
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const statuses = statusesOf(chat);

    const turn = chat.send('build it');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));
    activeRuns = { t1: [{ runId: 'r-1', userMessageId: 'u1', status: 'Running' }] };

    await chat.selectThread('t2');
    await firstClosed;
    await turn;

    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(chat.activeThreadId.value).toBe('t2');
    expect(chat.isStreaming.value).toBe(false);
    expect(chat.messages.value.map((m) => m.id)).toEqual(['u9', 'a9']);

    await chat.selectThread('t1');
    await vi.waitFor(() => expect(calls).toHaveLength(2));
    expect(calls[1].url).toBe('/api/chat/runs/r-1/stream?fromSequence=0');
    const last = chat.messages.value[chat.messages.value.length - 1];
    expect(last).toMatchObject({ role: 'assistant', content: 'Working on it, still', isStreaming: true, status: 'streaming' });
    expect(chat.isStreaming.value).toBe(true);
    expect(chat.activeRunId.value).toBe('r-1');
    expect(statuses.has('stopped')).toBe(false);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
  });

  it('New chat closes the stream of a run in progress without cancelling it', async () => {
    const d = deps();
    let wasClosed = false;
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1', threadId: 't1' });
      await closed(opts);
      wasClosed = true;
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const statuses = statusesOf(chat);
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));

    chat.newChat();
    await turn;

    expect(wasClosed).toBe(true);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(chat.messages.value).toEqual([]);
    expect(chat.activeRunId.value).toBeUndefined();
    expect(chat.isStreaming.value).toBe(false);
    expect(statuses.has('stopped')).toBe(false);
  });

  it('a run recovering from a lost connection is not cancelled either, and its recovery ends', async () => {
    const d = deps();
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1', threadId: 't1', sequence: 1 });
      visibility = 'hidden';
      (opts.onError as Cb<Error>)(new TypeError('Load failed'));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(calls).toHaveLength(1));

    chat.newChat();
    await turn;
    setVisibility('visible');
    await new Promise((r) => setTimeout(r, 0));

    expect(calls).toHaveLength(1);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(chat.isStreaming.value).toBe(false);
  });

  it('a new conversation left mid-run keeps its sidebar row, under the thread the server named', async () => {
    const d = deps();
    // The server has the thread once it names it.
    d.threadApi.getList.mockImplementation(async () => ({
      succeeded: true,
      data: { items: [{ id: 't-new', title: 'a brand new task', lastActivityTime: '2026-11-04T10:00:00Z' }], totalCount: 1 },
    }) as never);
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-new' });
      await closed(opts);
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('a brand new task');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));
    // The conversation became that thread when the run started.
    expect(chat.activeThreadId.value).toBe('t-new');

    chat.newChat();

    expect(chat.activeThreadId.value).toBeUndefined();
    expect(chat.threads.value.map((t) => t.id)).toEqual(['t-new']);
    expect(chat.threads.value[0].pending).toBeFalsy();
    expect(chat.threads.value[0].title).toBe('a brand new task');
    await turn;
    expect(d.threadApi.getList).toHaveBeenCalled();
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
  });
});

describe('a turn with no run named yet, on moving away', () => {
  it("an external agent's request stays open until its run is named, then closes; nothing is cancelled", async () => {
    const d = deps();
    let name: (() => void) | undefined;
    let opts0: StreamOpts | undefined;
    streamRunner = async (opts) => {
      opts0 = opts;
      await new Promise<void>((r) => (name = r));
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-2', threadId: 't1' });
      (opts.onDelta as Cb<string>)('not for this screen');
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const turn = chat.send('go');
    await vi.waitFor(() => expect(name).toBeDefined());

    chat.newChat();
    expect((opts0!.signal as AbortSignal).aborted).toBe(false);

    name?.();
    await turn;
    expect((opts0!.signal as AbortSignal).aborted).toBe(true);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(chat.messages.value).toEqual([]);
  });

  it("a built-in agent's request stays open so the server finishes the turn; it never reaches the screen", async () => {
    const d = deps();
    let finish: (() => void) | undefined;
    let opts0: StreamOpts | undefined;
    streamRunner = async (opts) => {
      opts0 = opts;
      (opts.onEvent as Cb<Record<string, unknown>>)({ threadId: 't1' });
      (opts.onDelta as Cb<string>)('An answer');
      await new Promise<void>((r) => (finish = r));
      (opts.onDelta as Cb<string>)(' that goes on');
      (opts.onDone as Cb<Record<string, unknown>>)({ threadId: 't1', assistantMessageId: 'a-late' });
      return { text: 'An answer that goes on' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const turn = chat.send('hello');
    await vi.waitFor(() => expect(finish).toBeDefined());

    await chat.selectThread('t2');
    expect((opts0!.signal as AbortSignal).aborted).toBe(false);
    const listLoads = d.threadApi.getList.mock.calls.length;

    finish?.();
    await turn;

    expect(chat.messages.value.map((m) => m.id)).toEqual(['u9', 'a9']);
    expect(chat.activeThreadId.value).toBe('t2');
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    // The list shows the conversation as the server has it now.
    expect(d.threadApi.getList.mock.calls.length).toBeGreaterThan(listLoads);
  });

  it('coming back to that conversation before its built-in turn ends shows the reply once it has', async () => {
    const d = deps();
    let finish: (() => void) | undefined;
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ threadId: 't1' });
      await new Promise<void>((r) => (finish = r));
      return { text: 'answer' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const turn = chat.send('hello');
    await vi.waitFor(() => expect(finish).toBeDefined());
    await chat.selectThread('t2');
    await chat.selectThread('t1');
    const reads = d.threadApi.getMessages.mock.calls.length;

    finish?.();
    await turn;
    await vi.waitFor(() => expect(d.threadApi.getMessages.mock.calls.length).toBe(reads + 1));
    expect(d.threadApi.getMessages).toHaveBeenLastCalledWith('t1', expect.anything());
  });
});

describe('Stop', () => {
  it('cancels a run named after it was pressed, as soon as the stream names it', async () => {
    const d = deps();
    let name: (() => void) | undefined;
    let opts0: StreamOpts | undefined;
    streamRunner = async (opts) => {
      opts0 = opts;
      await new Promise<void>((r) => (name = r));
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-3', threadId: 't1' });
      (opts.onDelta as Cb<string>)('late text');
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const turn = chat.send('go');
    await vi.waitFor(() => expect(name).toBeDefined());

    chat.abort();
    // The turn is over on screen at once.
    expect(chat.isStreaming.value).toBe(false);
    const reply = chat.messages.value[chat.messages.value.length - 1];
    expect(reply.status).toBe('stopped');
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();

    name?.();
    await turn;
    expect(d.chatApi.cancelRun).toHaveBeenCalledWith('r-3');
    expect((opts0!.signal as AbortSignal).aborted).toBe(true);
    // What arrived after the stop does not reach the stopped reply.
    expect(chat.messages.value[chat.messages.value.length - 1]).toMatchObject({ status: 'stopped', content: '' });
  });

  it("closes a built-in agent's request at its first sign, which is what stops it there", async () => {
    const d = deps();
    let first: (() => void) | undefined;
    let opts0: StreamOpts | undefined;
    streamRunner = async (opts) => {
      opts0 = opts;
      await new Promise<void>((r) => (first = r));
      (opts.onDelta as Cb<string>)('a word');
      return { text: 'a word' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('hi');
    await vi.waitFor(() => expect(first).toBeDefined());

    chat.abort();
    first?.();
    await turn;

    expect((opts0!.signal as AbortSignal).aborted).toBe(true);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(chat.messages.value[1]).toMatchObject({ status: 'stopped', content: '' });
  });

  it('then moving away loses nothing: the run is still cancelled when it is named', async () => {
    const d = deps();
    let name: (() => void) | undefined;
    streamRunner = async (opts) => {
      await new Promise<void>((r) => (name = r));
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-4', threadId: 't1' });
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    const turn = chat.send('go');
    await vi.waitFor(() => expect(name).toBeDefined());

    chat.abort();
    chat.newChat();
    name?.();
    await turn;
    expect(d.chatApi.cancelRun).toHaveBeenCalledWith('r-4');
  });
});
