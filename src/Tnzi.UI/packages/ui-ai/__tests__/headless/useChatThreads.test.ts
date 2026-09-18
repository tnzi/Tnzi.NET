import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * `streamChat` is the transport; the hook's job is everything AROUND it, so it
 * is mocked and driven by hand. `streamRunner` lets a test decide what the
 * "server" does for that turn.
 */
let streamRunner: (opts: Record<string, unknown>) => Promise<{ text: string; reasoning?: string }>;
/** Same shape as the real class; the hook only reads `status` off an instance. */
class ChatStreamRequestError extends Error {
  constructor(readonly status: number) {
    super(`Stream request failed: ${status} {}`);
  }
}
vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: (opts: Record<string, unknown>) => streamRunner(opts),
  ChatStreamRequestError,
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');

function api(overrides: Record<string, unknown> = {}) {
  const threadApi = {
    getList: vi.fn(async () => ({
      succeeded: true,
      data: { items: [{ id: 't1', title: 'Existing', lastActivityTime: '2026-08-02T10:00:00Z' }] },
    })),
    getDetail: vi.fn(async () => ({
      succeeded: true,
      data: {
        messages: [
          { id: 'm1', role: 'user', content: 'hi', creationTime: '2026-08-02T10:00:00Z' },
        ],
      },
    })),
    delete: vi.fn(async () => ({ succeeded: true })),
    ...overrides,
  };
  return {
    http: {
      getAccessToken: () => 'tok',
      refreshAccessToken: vi.fn(async () => 'fresh'),
      reportUnauthorized: vi.fn(),
    },
    chatApi: { getChatStreamUrl: () => '/api/ai/chat/stream' },
    threadApi,
  } as never;
}

beforeEach(() => {
  streamRunner = async () => ({ text: 'done' });
});

describe('useChatThreads', () => {
  it('loads the sidebar list through the adapter', async () => {
    const chat = useChatThreads(api());
    await chat.loadThreads();
    expect(chat.threads.value).toEqual([
      { id: 't1', title: 'Existing', updatedAt: '2026-08-02T10:00:00Z' },
    ]);
  });

  it('reports a failed list load rather than blanking the sidebar', async () => {
    const onError = vi.fn();
    const deps = api({ getList: vi.fn(async () => ({ succeeded: false, message: 'nope' })) });
    const chat = useChatThreads({ ...(deps as never), onError } as never);
    await chat.loadThreads();
    expect(onError).toHaveBeenCalledWith('nope');
  });

  it('opens a thread and maps its messages', async () => {
    const chat = useChatThreads(api());
    await chat.selectThread('t1');
    expect(chat.activeThreadId.value).toBe('t1');
    expect(chat.messages.value.map((m) => m.content)).toEqual(['hi']);
  });

  it('does not refetch the thread already open', async () => {
    const deps = api();
    const chat = useChatThreads(deps);
    await chat.selectThread('t1');
    await chat.selectThread('t1');
    expect((deps as unknown as { threadApi: { getDetail: { mock: { calls: unknown[] } } } }).threadApi.getDetail.mock.calls).toHaveLength(1);
  });

  it('newChat drops back to the empty state without touching the server', () => {
    const deps = api();
    const chat = useChatThreads(deps);
    chat.activeThreadId.value = 't1';
    chat.messages.value = [{ id: 'x', role: 'user', content: 'a', createdAt: '' }];
    chat.newChat();
    expect(chat.activeThreadId.value).toBeUndefined();
    expect(chat.messages.value).toEqual([]);
  });

  describe('send', () => {
    it('appends the user turn and a streaming placeholder, then finalises', async () => {
      streamRunner = async (opts) => {
        (opts.onDelta as (t: string) => void)('par');
        (opts.onDelta as (t: string) => void)('tial');
        return { text: 'partial' };
      };
      const chat = useChatThreads(api());
      await chat.send('question');

      expect(chat.messages.value.map((m) => [m.role, m.content])).toEqual([
        ['user', 'question'],
        ['assistant', 'partial'],
      ]);
      expect(chat.messages.value.every((m) => !m.isStreaming)).toBe(true);
      expect(chat.isStreaming.value).toBe(false);
      expect(chat.inputText.value).toBe('');
    });

    /** The user should see their conversation appear before the first token. */
    it('adds an optimistic sidebar row for a brand-new conversation', async () => {
      let rowsDuringStream = 0;
      streamRunner = async () => {
        rowsDuringStream = chat.threads.value.length;
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('a new conversation');
      expect(rowsDuringStream).toBe(1);
    });

    /** A sidebar row for a conversation that does not exist is worse than none. */
    it('rolls the optimistic row back when the stream fails', async () => {
      streamRunner = async (opts) => {
        (opts.onError as (e: Error) => void)(new Error('boom'));
        return { text: '' };
      };
      const chat = useChatThreads(api());
      await chat.send('doomed');
      expect(chat.threads.value).toEqual([]);
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.messages.value[1].error).toContain('boom');
    });

    it('keeps the row and adopts the real thread id when the backend commits one', async () => {
      streamRunner = async (opts) => {
        (opts.onDone as (e: Record<string, string>) => void)({ threadId: 'real-1' });
        return { text: 'ok' };
      };
      // The background refresh that `onDone` kicks off is the point: the server
      // has just created this thread and names it, so its list replaces the
      // local guess. Mock it the way the real backend answers.
      const deps = api({
        getList: vi.fn(async () => ({
          succeeded: true,
          data: {
            items: [{ id: 'real-1', title: 'Server title', lastActivityTime: '2026-08-02T11:00:00Z' }],
          },
        })),
      });
      const chat = useChatThreads(deps);
      await chat.send('first turn');
      await Promise.resolve(); // let the fire-and-forget refresh settle

      expect(chat.activeThreadId.value).toBe('real-1');
      expect(chat.threads.value.some((t) => t.id === 'real-1')).toBe(true);
      expect(chat.threads.value.some((t) => t.id.startsWith('pending_'))).toBe(false);
    });

    /**
     * Feedback and regenerate address rows by id, so the local placeholders must
     * be swapped for the persisted ones - and the post-stream finalisation has
     * to write to the NEW ids, not the ones the turn started with.
     */
    it('reconciles temp message ids and still finalises the right row', async () => {
      streamRunner = async (opts) => {
        (opts.onDone as (e: Record<string, string>) => void)({
          userMessageId: 'srv-user',
          assistantMessageId: 'srv-assistant',
        });
        return { text: 'final answer' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');

      expect(chat.messages.value.map((m) => m.id)).toEqual(['srv-user', 'srv-assistant']);
      expect(chat.messages.value[1].content).toBe('final answer');
      expect(chat.messages.value[1].isStreaming).toBe(false);
    });

    it('pins the configured agent onto the request', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: '' };
      };
      const chat = useChatThreads({ ...(api() as never), agentId: () => 'agent-7' } as never);
      await chat.send('q');
      expect(body?.agentId).toBe('agent-7');
    });

    it('omits agentId entirely when none is configured', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: '' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect('agentId' in (body ?? {})).toBe(false);
    });

    it('ignores an empty turn and a turn sent while streaming', async () => {
      const sent: string[] = [];
      streamRunner = async (opts) => {
        sent.push((opts.body as { message: string }).message);
        return { text: '' };
      };
      const chat = useChatThreads(api());
      await chat.send('   ');
      chat.isStreaming.value = true;
      await chat.send('while busy');
      expect(sent).toEqual([]);
    });
  });

  /** A cancelled turn must not leave a row blinking its caret forever. */
  it('abort clears the streaming flag on the message too', () => {
    const chat = useChatThreads(api());
    chat.isStreaming.value = true;
    chat.messages.value = [{ id: 'a', role: 'assistant', content: '', createdAt: '', isStreaming: true }];
    chat.abort();
    expect(chat.isStreaming.value).toBe(false);
    expect(chat.messages.value[0].isStreaming).toBe(false);
  });

  it('deleteThread removes the row and clears the view when it was open', async () => {
    const chat = useChatThreads(api());
    await chat.loadThreads();
    await chat.selectThread('t1');
    await chat.deleteThread('t1');
    expect(chat.threads.value).toEqual([]);
    expect(chat.activeThreadId.value).toBeUndefined();
  });

  it('keeps the row when the delete fails', async () => {
    const onError = vi.fn();
    const deps = api({ delete: vi.fn(async () => ({ succeeded: false, message: 'denied' })) });
    const chat = useChatThreads({ ...(deps as never), onError } as never);
    await chat.loadThreads();
    await chat.deleteThread('t1');
    expect(chat.threads.value).toHaveLength(1);
    expect(onError).toHaveBeenCalledWith('denied');
  });

  /**
   * The tail of an aborted turn must not touch shared state. Abort mid-answer,
   * send again immediately, and the first turn's `isStreaming = false` would
   * otherwise land after the second turn started - clearing the flag of a turn
   * that is still running, so the composer loses its stop button and the caret
   * stops on a message still being written.
   */
  it('a superseded turn does not clear the streaming state of the next one', async () => {
    let releaseFirst: (() => void) | undefined;
    let call = 0;
    streamRunner = async () => {
      call += 1;
      if (call === 1) {
        await new Promise<void>((r) => (releaseFirst = r));
        return { text: 'late' };
      }
      // Second turn stays in flight for the duration of the assertion.
      await new Promise(() => undefined);
      return { text: 'never' };
    };

    const chat = useChatThreads(api());
    const first = chat.send('one');
    chat.abort();
    void chat.send('two');
    expect(chat.isStreaming.value).toBe(true);

    releaseFirst?.();
    await first;

    expect(chat.isStreaming.value).toBe(true);
  });

  /**
   * Same reasoning for the terminal frame, but the damaging shape is the one
   * where `activeThreadId` is EMPTY: user aborts a brand-new conversation and
   * hits New chat, then the aborted turn's late `done` arrives carrying the
   * thread the backend created anyway - without a guard it drags the user into
   * a conversation they just walked away from, and slots a row for it.
   */
  it('a superseded turn does not adopt a thread id after New chat', async () => {
    let releaseFirst: (() => void) | undefined;
    streamRunner = async (opts) => {
      const done = opts.onDone as (e: Record<string, string>) => void;
      await new Promise<void>((r) => (releaseFirst = r));
      done({ threadId: 'late-thread' });
      return { text: '' };
    };

    const chat = useChatThreads(api());
    const first = chat.send('one');
    chat.abort();
    chat.newChat();

    releaseFirst?.();
    await first;

    expect(chat.activeThreadId.value).toBeUndefined();
    expect(chat.threads.value.some((t) => t.id === 'late-thread')).toBe(false);
  });

  /**
   * The stream is a raw fetch carrying a snapshot of the access token, so it
   * never goes through the client's 401 refresh path. A tab left open past the
   * token's lifetime used to fail every send with an inline 401 and never reach
   * the session-expired listener - the refresh token was still valid, so no
   * login redirect either. The hook now runs the same refresh-once-and-retry
   * the JSON path does, and reports the 401 when the retry still fails.
   */
  describe('expired access token', () => {
    type Http = { refreshAccessToken: ReturnType<typeof vi.fn>; reportUnauthorized: ReturnType<typeof vi.fn> };
    const httpOf = (deps: unknown) => (deps as { http: Http }).http;
    const unauthorized = (opts: Record<string, unknown>) =>
      (opts.onError as (e: Error) => void)(new ChatStreamRequestError(401));

    it('refreshes once and retries the stream with the new token', async () => {
      const authHeaders: (string | undefined)[] = [];
      streamRunner = async (opts) => {
        authHeaders.push((opts.headers as Record<string, string>).Authorization);
        if (authHeaders.length === 1) {
          unauthorized(opts);
          return { text: '' };
        }
        (opts.onDelta as (t: string) => void)('back');
        return { text: 'back' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.send('after a long idle');

      expect(authHeaders).toEqual(['Bearer tok', 'Bearer fresh']);
      expect(httpOf(deps).refreshAccessToken).toHaveBeenCalledTimes(1);
      expect(httpOf(deps).reportUnauthorized).not.toHaveBeenCalled();
      expect(chat.messages.value[1].content).toBe('back');
      expect(chat.messages.value[1].isStreaming).toBe(false);
      expect(chat.isStreaming.value).toBe(false);
      // The optimistic sidebar row survives: the turn did not fail.
      expect(chat.threads.value).toHaveLength(1);
    });

    it('reports unauthorized when the retry still 401s, and retries only once', async () => {
      let calls = 0;
      streamRunner = async (opts) => {
        calls += 1;
        unauthorized(opts);
        return { text: '' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.send('doomed');

      expect(calls).toBe(2);
      expect(httpOf(deps).refreshAccessToken).toHaveBeenCalledTimes(1);
      expect(httpOf(deps).reportUnauthorized).toHaveBeenCalledTimes(1);
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.messages.value[1].error).toContain('Session expired');
      expect(chat.messages.value[1].isStreaming).toBe(false);
      expect(chat.threads.value).toEqual([]);
    });

    it('does not retry when the refresh itself fails (the client already notified)', async () => {
      let calls = 0;
      streamRunner = async (opts) => {
        calls += 1;
        unauthorized(opts);
        return { text: '' };
      };
      const deps = api();
      httpOf(deps).refreshAccessToken.mockResolvedValue(null);
      const chat = useChatThreads(deps);
      await chat.send('doomed');

      expect(calls).toBe(1);
      expect(httpOf(deps).reportUnauthorized).not.toHaveBeenCalled();
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.threads.value).toEqual([]);
    });

    it('a non-401 failure is not treated as an expired token', async () => {
      streamRunner = async (opts) => {
        (opts.onError as (e: Error) => void)(new ChatStreamRequestError(500));
        return { text: '' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.send('q');

      expect(httpOf(deps).refreshAccessToken).not.toHaveBeenCalled();
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.messages.value[1].error).toContain('500');
    });

    it('a turn aborted during the refresh is not retried', async () => {
      let calls = 0;
      let releaseRefresh: ((token: string) => void) | undefined;
      streamRunner = async (opts) => {
        calls += 1;
        unauthorized(opts);
        return { text: '' };
      };
      const deps = api();
      httpOf(deps).refreshAccessToken.mockImplementation(
        () => new Promise<string>((r) => (releaseRefresh = r)),
      );
      const chat = useChatThreads(deps);
      const turn = chat.send('q');
      await vi.waitFor(() => expect(releaseRefresh).toBeDefined());
      chat.abort();
      releaseRefresh!('fresh');
      await turn;

      expect(calls).toBe(1);
      expect(chat.isStreaming.value).toBe(false);
    });
  });
  /**
   * `ChatMessage.status` is what the renderers key their dedicated UI off
   * (`TThreadMessage` / `TChatMessage`: error block with Retry, "Generation
   * stopped" mark). The hook used to write none of it - abort only cleared
   * `isStreaming`, a failure was appended to the body as italic markdown - so
   * those blocks never rendered on the default path.
   */
  describe('message status contract', () => {
    it('abort marks the streaming row stopped', () => {
      const chat = useChatThreads(api());
      chat.isStreaming.value = true;
      chat.messages.value = [
        { id: 'a', role: 'assistant', content: 'half', createdAt: '', isStreaming: true, status: 'streaming' },
      ];
      chat.abort();
      expect(chat.messages.value[0].status).toBe('stopped');
      expect(chat.messages.value[0].content).toBe('half');
    });

    it('a failed stream marks the row error with the message and keeps the buffered text', async () => {
      streamRunner = async (opts) => {
        (opts.onDelta as (t: string) => void)('partial ');
        (opts.onError as (e: Error) => void)(new Error('boom'));
        return { text: '' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      const row = chat.messages.value[1];
      expect(row.status).toBe('error');
      expect(row.error).toBe('boom');
      expect(row.content).toBe('partial ');
      expect(row.isStreaming).toBe(false);
    });

    it('a stream-level error event is reported through its errorMessage', async () => {
      streamRunner = async (opts) => {
        (opts.onError as (e: unknown) => void)({ isError: true, errorMessage: 'quota exceeded' });
        return { text: '' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.messages.value[1].error).toBe('quota exceeded');
    });

    it('a completed turn marks the row done, and the placeholder starts as streaming', async () => {
      let statusDuringStream: string | undefined;
      streamRunner = async () => {
        statusDuringStream = chat.messages.value[1].status;
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(statusDuringStream).toBe('streaming');
      expect(chat.messages.value[1].status).toBe('done');
      expect(chat.messages.value[1].error).toBeUndefined();
    });
  });
  /**
   * `selectThread` used to trust every id it was handed and every detail
   * response it got back. Two consequences: clicking the optimistic row of a
   * brand-new conversation (a `pending_` id, which exists only locally) aborted
   * the first turn and left `activeThreadId` poisoned so the next send posted
   * a non-GUID thread id; and clicking A then B before A's detail returned
   * rendered A's messages under B once A finally resolved.
   */
  describe('selectThread guards', () => {
    it('marks the optimistic row pending', async () => {
      let rowDuringStream: Record<string, unknown> | undefined;
      streamRunner = async () => {
        rowDuringStream = { ...chat.threads.value[0] };
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('first');
      expect(rowDuringStream?.pending).toBe(true);
    });

    it('selecting the optimistic pending row is a no-op and keeps the stream alive', async () => {
      let release: (() => void) | undefined;
      let aborted = false;
      streamRunner = async (opts) => {
        (opts.signal as AbortSignal).addEventListener('abort', () => (aborted = true));
        await new Promise<void>((r) => (release = r));
        return { text: 'done' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      const turn = chat.send('first');
      await vi.waitFor(() => expect(release).toBeDefined());

      const pendingId = chat.threads.value[0].id;
      expect(pendingId.startsWith('pending_')).toBe(true);
      await chat.selectThread(pendingId);

      expect(aborted).toBe(false);
      expect(chat.isStreaming.value).toBe(true);
      expect(chat.activeThreadId.value).toBeUndefined();
      expect((deps as unknown as { threadApi: { getDetail: ReturnType<typeof vi.fn> } }).threadApi.getDetail).not.toHaveBeenCalled();

      release?.();
      await turn;
      expect(chat.messages.value[1].content).toBe('done');
    });

    it('a stale detail response is discarded when another thread was selected meanwhile', async () => {
      const resolvers = new Map<string, (v: unknown) => void>();
      const deps = api({
        getDetail: vi.fn(
          (id: string) => new Promise((resolve) => resolvers.set(id, resolve)),
        ),
      });
      const chat = useChatThreads(deps);

      const a = chat.selectThread('A');
      const b = chat.selectThread('B');
      await vi.waitFor(() => expect(resolvers.size).toBe(2));

      resolvers.get('B')!({
        succeeded: true,
        data: { messages: [{ id: 'b1', role: 'user', content: 'from B', creationTime: '' }] },
      });
      await b;
      resolvers.get('A')!({
        succeeded: true,
        data: { messages: [{ id: 'a1', role: 'user', content: 'from A', creationTime: '' }] },
      });
      await a;

      expect(chat.activeThreadId.value).toBe('B');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['from B']);
    });

    it('a stale detail failure is not reported either', async () => {
      const onError = vi.fn();
      const resolvers = new Map<string, (v: unknown) => void>();
      const deps = api({
        getDetail: vi.fn(
          (id: string) => new Promise((resolve) => resolvers.set(id, resolve)),
        ),
      });
      const chat = useChatThreads({ ...(deps as never), onError } as never);

      const a = chat.selectThread('A');
      const b = chat.selectThread('B');
      await vi.waitFor(() => expect(resolvers.size).toBe(2));
      resolvers.get('B')!({ succeeded: true, data: { messages: [] } });
      await b;
      resolvers.get('A')!({ succeeded: false, message: 'gone' });
      await a;

      expect(onError).not.toHaveBeenCalled();
    });

    it('aborting the first turn removes the optimistic row and refreshes the list', async () => {
      let release: (() => void) | undefined;
      streamRunner = async () => {
        await new Promise<void>((r) => (release = r));
        return { text: '' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      const turn = chat.send('first');
      await vi.waitFor(() => expect(release).toBeDefined());
      expect(chat.threads.value.some((t) => t.id.startsWith('pending_'))).toBe(true);

      chat.abort();
      release?.();
      await turn;
      await Promise.resolve();

      expect(chat.threads.value.some((t) => t.id.startsWith('pending_'))).toBe(false);
      expect((deps as unknown as { threadApi: { getList: ReturnType<typeof vi.fn> } }).threadApi.getList).toHaveBeenCalledTimes(1);
      expect(chat.activeThreadId.value).toBeUndefined();
    });

    it('never adopts a pending id as the active thread, even when handed one directly', async () => {
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.selectThread('pending_123_4');
      expect(chat.activeThreadId.value).toBeUndefined();
      expect((deps as unknown as { threadApi: { getDetail: ReturnType<typeof vi.fn> } }).threadApi.getDetail).not.toHaveBeenCalled();
    });

    it('deleting a pending row is a no-op', async () => {
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.deleteThread('pending_123_4');
      expect((deps as unknown as { threadApi: { delete: ReturnType<typeof vi.fn> } }).threadApi.delete).not.toHaveBeenCalled();
    });
  });
  /**
   * The backend announces tool activity on the stream (`isToolCall` +
   * `toolCallNames` when a tool starts, a `toolCalls` detail list once it has
   * run), the answering agent on a handoff, and the token usage on the
   * terminal frame. None of it was wired, so the default shell never showed a
   * tool call - live or reopened - and never a token count.
   */
  describe('stream activity on the assistant row', () => {
    type Cb<T> = (v: T) => void;

    it('onToolCall marks the assistant row with pending tool entries', async () => {
      let seen: unknown;
      streamRunner = async (opts) => {
        (opts.onToolCall as Cb<string[]>)(['search', 'read_file']);
        seen = chat.messages.value[1].toolCalls;
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(seen).toEqual([{ name: 'search' }, { name: 'read_file' }]);
    });

    it('a toolCalls event fills in the pending entries with their outcome', async () => {
      streamRunner = async (opts) => {
        (opts.onToolCall as Cb<string[]>)(['search']);
        (opts.onEvent as Cb<Record<string, unknown>>)({
          toolCalls: [{ name: 'search', durationMs: 12.3, isSuccess: false, error: 'timeout' }],
        });
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].toolCalls).toEqual([
        { name: 'search', durationMs: 12.3, isSuccess: false, error: 'timeout' },
      ]);
    });

    it('a second call of the same tool is a second entry, not an overwrite', async () => {
      streamRunner = async (opts) => {
        const onToolCall = opts.onToolCall as Cb<string[]>;
        const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
        onToolCall(['search']);
        onEvent({ toolCalls: [{ name: 'search', durationMs: 1, isSuccess: true }] });
        onToolCall(['search']);
        onEvent({ toolCalls: [{ name: 'search', durationMs: 2, isSuccess: true }] });
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].toolCalls?.map((c) => c.durationMs)).toEqual([1, 2]);
    });

    it('onAgentSwitch stamps the answering agent on the row', async () => {
      streamRunner = async (opts) => {
        (opts.onAgentSwitch as Cb<string>)('Researcher');
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].agentName).toBe('Researcher');
    });

    it('onDone carries the usage onto the row', async () => {
      const usage = { inputTokens: 10, outputTokens: 5, totalTokens: 15, cachedInputTokens: 0, cacheCreationTokens: 0 };
      streamRunner = async (opts) => {
        (opts.onDone as Cb<Record<string, unknown>>)({ usage });
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].usage).toEqual(usage);
    });

    it('activity for a superseded turn is ignored', async () => {
      let release: (() => void) | undefined;
      let fire: (() => void) | undefined;
      streamRunner = async (opts) => {
        fire = () => {
          (opts.onToolCall as Cb<string[]>)(['late']);
          (opts.onAgentSwitch as Cb<string>)('Late');
        };
        await new Promise<void>((r) => (release = r));
        return { text: '' };
      };
      const chat = useChatThreads(api());
      const turn = chat.send('one');
      await vi.waitFor(() => expect(release).toBeDefined());
      chat.abort();
      fire?.();
      release?.();
      await turn;
      expect(chat.messages.value[1].toolCalls).toBeUndefined();
      expect(chat.messages.value[1].agentName).toBeUndefined();
    });
  });
  /**
   * The composer emits `(text, files)`; `send` took `(content)` and the files
   * evaporated between the two - the chip vanished on Send, the text went up,
   * and the assistant answered "I don't see an image". Images travel inline as
   * base64 content parts; anything else needs a Storage id and therefore the
   * consumer's `uploadFile`, and is refused loudly without one.
   */
  describe('send with attachments', () => {
    const png = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'shot.png', { type: 'image/png' });
    const pdf = () => new File([new Uint8Array([1, 2, 3])], 'doc.pdf', { type: 'application/pdf' });

    it('a send with files reaches streamChat with text + image content parts', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: 'a cat' };
      };
      const chat = useChatThreads(api());
      await chat.send('what is this?', [png()]);

      expect(body?.message).toBe('what is this?');
      expect(body?.content).toEqual([
        { type: 'text', text: 'what is this?' },
        { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png' },
      ]);
      expect(chat.messages.value[0].attachments).toEqual([
        { type: 'image', fileName: 'shot.png', mediaType: 'image/png', base64Data: 'iVBORw==' },
      ]);
      expect(chat.messages.value[1].content).toBe('a cat');
    });

    /**
     * The backend only adds (and persists) the user turn when `message` is
     * non-blank, so a files-only send must carry a message the user did not
     * type: the attachment names. A blank `message` here is a turn the model
     * never sees, under a bubble that says the image was sent.
     */
    it('a files-only send with empty text is sent with the attachment names as its message', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('', [png()]);
      expect(body).toBeDefined();
      expect(body?.message).toBe('[Attached: shot.png]');
      expect(body?.content).toEqual([
        { type: 'text', text: '[Attached: shot.png]' },
        { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png' },
      ]);
      expect(chat.messages.value).toHaveLength(2);
      // The user row shows what was typed (nothing) plus the attachment chip.
      expect(chat.messages.value[0].content).toBe('');
      expect(chat.messages.value[0].attachments).toHaveLength(1);
    });

    it('a text-only send carries no content parts (the plain message path is unchanged)', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('plain', []);
      expect('content' in (body ?? {})).toBe(false);
    });

    it('refuses a non-image file through onError and sends nothing, keeping the draft', async () => {
      const onError = vi.fn();
      const sent: unknown[] = [];
      streamRunner = async (opts) => {
        sent.push(opts.body);
        return { text: '' };
      };
      const chat = useChatThreads({ ...(api() as never), onError } as never);
      chat.inputText.value = 'summarise this';
      await chat.send('summarise this', [pdf()]);

      expect(sent).toEqual([]);
      expect(onError).toHaveBeenCalledTimes(1);
      expect(onError.mock.calls[0][0]).toContain('doc.pdf');
      expect(chat.messages.value).toEqual([]);
      expect(chat.threads.value).toEqual([]);
      expect(chat.isStreaming.value).toBe(false);
      expect(chat.inputText.value).toBe('summarise this');
    });

    it('refuses an oversized attachment before the optimistic row is added', async () => {
      const onError = vi.fn();
      const chat = useChatThreads({ ...(api() as never), onError, maxAttachmentBytes: 2 } as never);
      await chat.send('x', [png()]);
      expect(onError).toHaveBeenCalledTimes(1);
      expect(chat.messages.value).toEqual([]);
      expect(chat.threads.value).toEqual([]);
    });

    it('a stop during the upload cancels the turn before anything is sent', async () => {
      let releaseUpload: ((v: { id: string }) => void) | undefined;
      const uploadFile = vi.fn(() => new Promise<{ id: string }>((r) => (releaseUpload = r)));
      const sent: unknown[] = [];
      streamRunner = async (opts) => {
        sent.push(opts.body);
        return { text: '' };
      };
      const chat = useChatThreads({ ...(api() as never), uploadFile } as never);
      const turn = chat.send('summarise', [pdf()]);
      await vi.waitFor(() => expect(releaseUpload).toBeDefined());
      expect(chat.isStreaming.value).toBe(true);

      chat.abort();
      releaseUpload!({ id: 'late' });
      await turn;

      expect(sent).toEqual([]);
      expect(chat.messages.value).toEqual([]);
      expect(chat.isStreaming.value).toBe(false);
    });

    it('references a non-image file through uploadFile', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: 'ok' };
      };
      const uploadFile = vi.fn(async (f: File) => ({ id: 'file-9', fileName: f.name }));
      const chat = useChatThreads({ ...(api() as never), uploadFile } as never);
      await chat.send('summarise', [pdf()]);
      expect(body?.content).toEqual([
        { type: 'text', text: 'summarise' },
        { type: 'file', fileId: 'file-9', fileName: 'doc.pdf' },
      ]);
      expect(chat.messages.value[0].attachments).toEqual([{ type: 'file', fileId: 'file-9', fileName: 'doc.pdf' }]);
    });
  });
});
