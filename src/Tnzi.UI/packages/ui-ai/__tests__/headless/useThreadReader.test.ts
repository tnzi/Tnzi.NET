import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createApp, defineComponent, effectScope, h, inject, nextTick, ref } from 'vue';

/**
 * `useThreadReader`: one conversation read by id and followed while its turns run, with no
 * request to anything a reader must leave alone (the thread list, attention and read marks,
 * the message queue, approvals) and nothing provided for a composer.
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

const { useThreadReader } = await import('../../src/headless/useThreadReader');
const { CHAT_THREADS_KEY } = await import('../../src/headless/chat-approvals-key');

const opened = [
  { id: 'u1', role: 'user', content: 'Tidy the memory', creationTime: '2026-11-13T10:00:00Z', order: 1 },
  { id: 'a1', role: 'assistant', content: 'Tidied.', creationTime: '2026-11-13T10:00:05Z', order: 2 },
];
const withSecondTurn = [
  ...opened,
  { id: 'u2', role: 'user', content: 'Tidy again', creationTime: '2026-11-13T11:00:00Z', order: 3 },
];

/** The two APIs as proxies: every method the hook calls is recorded, whatever its name. */
function deps(pages: unknown[][], runs: unknown[][]) {
  const calls: string[] = [];
  const known: Record<string, (...args: unknown[]) => unknown> = {
    getMessages: async () => ({ succeeded: true, data: { messages: pages.shift() ?? withSecondTurn, hasMore: false } }),
    getActiveRuns: async () => ({ succeeded: true, data: runs.length > 1 ? runs.shift() : runs[0] ?? [] }),
    getRunStreamUrl: (runId: unknown, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
  };
  const api = (prefix: string) =>
    new Proxy({}, {
      get: (_target, name: string) => {
        const fn = vi.fn((...args: unknown[]) => {
          calls.push(`${prefix}.${name}`);
          return known[name] ? known[name](...args) : Promise.resolve({ succeeded: true, data: null });
        });
        return fn;
      },
    });
  return {
    calls,
    options: {
      http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
      threadApi: api('thread'),
      chatApi: api('chat'),
    },
  };
}

/** Requests a reader may make; anything else (list, attention, read, queue, permissions, cancel) fails the test. */
const READER_CALLS = new Set(['thread.getMessages', 'thread.getAttachment', 'chat.getActiveRuns', 'chat.getRunStreamUrl']);

/** A stream that writes `text` and ends when `finish` is called. */
function holdStream(text: string, assistantMessageId: string) {
  const streams: StreamOpts[] = [];
  const finishers: Array<() => void> = [];
  streamRunner = async (opts) => {
    streams.push(opts);
    (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r1', sequence: 1 });
    (opts.onDelta as Cb<string>)(text);
    const signal = opts.signal as AbortSignal;
    await new Promise<void>((resolve) => {
      finishers.push(resolve);
      signal.addEventListener('abort', () => resolve());
    });
    if (signal.aborted) return { text };
    (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId, runId: 'r1', sequence: 9 });
    return { text };
  };
  return { streams, finish: () => finishers.shift()?.() };
}

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
  streamRunner = async () => ({ text: '' });
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useThreadReader', () => {
  it('loads the conversation by id and asks for nothing a reader must leave alone', async () => {
    const d = deps([opened], [[]]);
    const scope = effectScope();
    const reader = scope.run(() => useThreadReader({ ...d.options, threadId: 't1', followPollMs: 0 } as never))!;
    await vi.waitFor(() => expect(reader.threadId.value).toBe('t1'));
    await vi.waitFor(() => expect(d.calls).toContain('chat.getActiveRuns'));
    expect(reader.messages.value.map((m) => m.id)).toEqual(['u1', 'a1']);
    expect(reader.loading.value).toBe(false);
    expect(d.calls.filter((c) => !READER_CALLS.has(c))).toEqual([]);
    // What it exposes reads; nothing sends, stops or edits.
    for (const name of ['send', 'regenerate', 'editMessage', 'abort', 'inputText', 'threads', 'messageQueue', 'approvals']) {
      expect(reader).not.toHaveProperty(name);
    }
    scope.stop();
  });

  it('streams a turn that starts after it was opened, without the consumer polling', async () => {
    const runs: unknown[][] = [[]];
    const d = deps([opened, withSecondTurn], runs);
    const stream = holdStream('Tidying', 'a2');
    const scope = effectScope();
    const reader = scope.run(() => useThreadReader({ ...d.options, threadId: 't1', followPollMs: 10 } as never))!;
    await vi.waitFor(() => expect(reader.messages.value.map((m) => m.id)).toEqual(['u1', 'a1']));
    // Asked a few times with nothing running, then a turn starts on the server.
    await vi.waitFor(() => expect(d.calls.filter((c) => c === 'chat.getActiveRuns').length).toBeGreaterThan(2));
    expect(stream.streams).toHaveLength(0);
    runs[0] = [{ runId: 'r1', userMessageId: 'u2', status: 'Running' }];

    await vi.waitFor(() => expect(stream.streams).toHaveLength(1));
    expect(stream.streams[0]!.url).toBe('/api/chat/runs/r1/stream?fromSequence=0');
    expect(stream.streams[0]!.method).toBe('GET');
    await vi.waitFor(() => expect(reader.isStreaming.value).toBe(true));
    const live = reader.messages.value[reader.messages.value.length - 1]!;
    expect(reader.messages.value.slice(0, 3).map((m) => m.id)).toEqual(['u1', 'a1', 'u2']);
    expect(live).toMatchObject({ role: 'assistant', content: 'Tidying', isStreaming: true });
    expect(reader.runId.value).toBe('r1');

    stream.finish();
    await vi.waitFor(() => expect(reader.isStreaming.value).toBe(false));
    expect(reader.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2', 'a2']);
    expect(reader.messages.value[3]).toMatchObject({ content: 'Tidying', status: 'done', isStreaming: false });

    // The server still lists the run for a moment: it is not taken for a new turn.
    const asked = d.calls.filter((c) => c === 'chat.getActiveRuns').length;
    await vi.waitFor(() => expect(d.calls.filter((c) => c === 'chat.getActiveRuns').length).toBeGreaterThan(asked + 1));
    expect(stream.streams).toHaveLength(1);
    expect(d.calls.filter((c) => !READER_CALLS.has(c))).toEqual([]);
    scope.stop();
  });

  it('attaches to the turn running when it opens', async () => {
    const d = deps([withSecondTurn], [[{ runId: 'r1', userMessageId: 'u2', status: 'Running' }]]);
    const stream = holdStream('Working', 'a2');
    const scope = effectScope();
    const reader = scope.run(() => useThreadReader({ ...d.options, threadId: 't1', followPollMs: 0 } as never))!;
    await vi.waitFor(() => expect(stream.streams).toHaveLength(1));
    await vi.waitFor(() => expect(reader.messages.value[reader.messages.value.length - 1]?.content).toBe('Working'));
    expect(reader.isStreaming.value).toBe(true);
    stream.finish();
    await vi.waitFor(() => expect(reader.isStreaming.value).toBe(false));
    scope.stop();
  });

  it('stops following when asked: the stream closes, nothing is cancelled, no new turn is looked for', async () => {
    const d = deps([withSecondTurn], [[{ runId: 'r1', userMessageId: 'u2', status: 'Running' }]]);
    const stream = holdStream('Working', 'a2');
    const scope = effectScope();
    const reader = scope.run(() => useThreadReader({ ...d.options, threadId: 't1', followPollMs: 10 } as never))!;
    await vi.waitFor(() => expect(reader.isStreaming.value).toBe(true));

    reader.stopFollowing();
    expect(reader.following.value).toBe(false);
    expect((stream.streams[0]!.signal as AbortSignal).aborted).toBe(true);
    expect(reader.isStreaming.value).toBe(false);
    const last = reader.messages.value[reader.messages.value.length - 1]!;
    expect(last).toMatchObject({ content: 'Working', isStreaming: false });
    expect(last.status).not.toBe('stopped');

    const asked = d.calls.length;
    await new Promise((r) => setTimeout(r, 60));
    expect(d.calls.length).toBe(asked);
    expect(d.calls).not.toContain('chat.cancelRun');

    // A refresh while not following reads the transcript and attaches to nothing.
    await reader.refresh();
    expect(d.calls.slice(asked)).toEqual(['thread.getMessages']);
    expect(stream.streams).toHaveLength(1);

    // Following again attaches to the running turn.
    await reader.follow();
    await vi.waitFor(() => expect(stream.streams).toHaveLength(2));
    expect(reader.following.value).toBe(true);
    scope.stop();
  });

  it('stops following when its scope ends', async () => {
    const d = deps([withSecondTurn], [[{ runId: 'r1', userMessageId: 'u2', status: 'Running' }]]);
    const stream = holdStream('Working', 'a2');
    const scope = effectScope();
    scope.run(() => useThreadReader({ ...d.options, threadId: 't1', followPollMs: 10 } as never));
    await vi.waitFor(() => expect(stream.streams).toHaveLength(1));
    scope.stop();
    expect((stream.streams[0]!.signal as AbortSignal).aborted).toBe(true);
    const asked = d.calls.length;
    await new Promise((r) => setTimeout(r, 60));
    expect(d.calls.length).toBe(asked);
    expect(d.calls).not.toContain('chat.cancelRun');
  });

  it('opens another conversation when the id changes, and shows none for no id', async () => {
    const d = deps([opened, withSecondTurn], [[]]);
    const id = ref<string | null>('t1');
    const scope = effectScope();
    const reader = scope.run(() => useThreadReader({ ...d.options, threadId: id, followPollMs: 0 } as never))!;
    await vi.waitFor(() => expect(reader.messages.value).toHaveLength(2));
    id.value = 't2';
    await vi.waitFor(() => expect(reader.threadId.value).toBe('t2'));
    expect(reader.messages.value).toHaveLength(3);
    id.value = null;
    await vi.waitFor(() => expect(reader.threadId.value).toBeUndefined());
    expect(reader.messages.value).toEqual([]);
    scope.stop();
  });

  it('pages back through older messages', async () => {
    const d = deps([], [[]]);
    const pages = [
      { messages: withSecondTurn.slice(2), hasMore: true },
      { messages: opened, hasMore: false },
    ];
    const threadApi = d.options.threadApi as Record<string, unknown>;
    const getMessages = vi.fn(async () => ({ succeeded: true, data: pages.shift() }));
    const reader = effectScope().run(() =>
      useThreadReader({ ...d.options, threadApi: new Proxy(threadApi, { get: (t, n) => (n === 'getMessages' ? getMessages : (t as never)[n]) }), threadId: 't1', followPollMs: 0 } as never))!;
    await vi.waitFor(() => expect(reader.hasOlderMessages.value).toBe(true));
    await reader.loadOlderMessages();
    expect(reader.messages.value.map((m) => m.id)).toEqual(['u1', 'a1', 'u2']);
    expect(reader.hasOlderMessages.value).toBe(false);
    expect(getMessages).toHaveBeenLastCalledWith('t1', { beforeOrder: 3, limit: 100 });
  });

  it('provides no conversation for a composer or TChatApp to pick up', async () => {
    const d = deps([opened], [[]]);
    let found: unknown = 'unset';
    const Child = defineComponent({
      setup() {
        found = inject(CHAT_THREADS_KEY, null);
        return () => null;
      },
    });
    const Parent = defineComponent({
      setup() {
        useThreadReader({ ...d.options, threadId: 't1', followPollMs: 0 } as never);
        return () => h(Child);
      },
    });
    const host = document.createElement('div');
    const app = createApp(Parent);
    app.mount(host);
    await nextTick();
    expect(found).toBeNull();
    app.unmount();
  });
});
