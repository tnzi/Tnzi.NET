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
    getMessages: vi.fn(async () => ({
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
    chatApi: {
      getChatStreamUrl: () => '/api/ai/chat/stream',
      getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] })),
      getRunStreamUrl: (runId: string, from = 0) => `/api/ai/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
    },
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
    expect(onError).toHaveBeenCalledWith('nope', expect.objectContaining({ operation: 'load-threads' }));
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
    expect((deps as unknown as { threadApi: { getMessages: { mock: { calls: unknown[] } } } }).threadApi.getMessages.mock.calls).toHaveLength(1);
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
        (opts.onError as (e: Error) => void)(new ChatStreamRequestError(500));
        return { text: '' };
      };
      const chat = useChatThreads(api());
      await chat.send('doomed');
      expect(chat.threads.value).toEqual([]);
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.messages.value[1].error).toContain('500');
    });

    /**
     * A connection lost before any event named a thread: nothing to ask the server about, so
     * the turn fails, but the server may have kept a thread, so the list is read again (as
     * after Stop) instead of being left as it was.
     */
    it('a connection lost before the first event fails the turn and re-reads the list', async () => {
      streamRunner = async (opts) => {
        (opts.onError as (e: Error) => void)(new TypeError('Load failed'));
        return { text: '' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.send('doomed');
      expect(chat.threads.value.some((t) => t.pending)).toBe(false);
      expect((deps as { threadApi: { getList: ReturnType<typeof vi.fn> } }).threadApi.getList).toHaveBeenCalled();
      expect(chat.messages.value[1].status).toBe('error');
      expect(chat.messages.value[1].error).toContain('Load failed');
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
    expect(onError).toHaveBeenCalledWith('denied', expect.objectContaining({ operation: 'delete-thread' }));
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
      expect((deps as unknown as { threadApi: { getMessages: ReturnType<typeof vi.fn> } }).threadApi.getMessages).not.toHaveBeenCalled();

      release?.();
      await turn;
      expect(chat.messages.value[1].content).toBe('done');
    });

    it('a stale detail response is discarded when another thread was selected meanwhile', async () => {
      const resolvers = new Map<string, (v: unknown) => void>();
      const deps = api({
        getMessages: vi.fn(
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
        getMessages: vi.fn(
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

    /**
     * `selectThread` moved `activeThreadId` before the load and never put it
     * back: a failed load left A's transcript on screen under B's id, the next
     * message was persisted into B, and clicking B again did nothing because
     * it already counted as open.
     */
    it('a failed load leaves the open conversation as it was, and the row can be retried', async () => {
      const onError = vi.fn();
      const deps = api();
      const chat = useChatThreads({ ...(deps as never), onError } as never);
      await chat.selectThread('t1');
      const getMessages = (deps as unknown as { threadApi: { getMessages: ReturnType<typeof vi.fn> } }).threadApi.getMessages;
      getMessages.mockResolvedValueOnce({ succeeded: false, message: 'Network error' });

      await chat.selectThread('B');

      expect(onError).toHaveBeenCalledWith('Network error', expect.objectContaining({ operation: 'load-thread' }));
      expect(chat.activeThreadId.value).toBe('t1');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['hi']);

      // Not "already open": the retry reaches the server and switches.
      getMessages.mockResolvedValueOnce({
        succeeded: true,
        data: { messages: [{ id: 'b1', role: 'user', content: 'from B', creationTime: '' }] },
      });
      await chat.selectThread('B');
      expect(chat.activeThreadId.value).toBe('B');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['from B']);
    });

    it('the open conversation and its id switch together, when the transcript arrives', async () => {
      let resolveB: ((v: unknown) => void) | undefined;
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.selectThread('t1');
      (deps as unknown as { threadApi: { getMessages: ReturnType<typeof vi.fn> } }).threadApi.getMessages.mockImplementationOnce(
        () => new Promise((resolve) => (resolveB = resolve)),
      );

      const switching = chat.selectThread('B');
      await vi.waitFor(() => expect(resolveB).toBeDefined());
      expect(chat.activeThreadId.value).toBe('t1');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['hi']);

      resolveB!({ succeeded: true, data: { messages: [{ id: 'b1', role: 'user', content: 'from B', creationTime: '' }] } });
      await switching;
      expect(chat.activeThreadId.value).toBe('B');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['from B']);
    });

    // The switch waits for the transcript, so the clicked row is not "open" yet:
    // deleting it in that window must cancel the switch rather than land the
    // user in a conversation that no longer exists.
    it('deleting the thread still loading cancels the switch to it', async () => {
      let resolveB: ((v: unknown) => void) | undefined;
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.selectThread('t1');
      (deps as unknown as { threadApi: { getMessages: ReturnType<typeof vi.fn> } }).threadApi.getMessages.mockImplementationOnce(
        () => new Promise((resolve) => (resolveB = resolve)),
      );

      const switching = chat.selectThread('B');
      await vi.waitFor(() => expect(resolveB).toBeDefined());
      await chat.deleteThread('B');
      resolveB!({ succeeded: true, data: { messages: [{ id: 'b1', role: 'user', content: 'from B', creationTime: '' }] } });
      await switching;

      expect(chat.activeThreadId.value).toBe('t1');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['hi']);
    });

    it('a send while another thread loads stays in the open conversation and cancels the switch', async () => {
      let resolveB: ((v: unknown) => void) | undefined;
      let body: { threadId?: string | null } | undefined;
      streamRunner = async (opts) => {
        body = opts.body as { threadId?: string | null };
        return { text: 'answer' };
      };
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.selectThread('t1');
      (deps as unknown as { threadApi: { getMessages: ReturnType<typeof vi.fn> } }).threadApi.getMessages.mockImplementationOnce(
        () => new Promise((resolve) => (resolveB = resolve)),
      );

      const switching = chat.selectThread('B');
      await vi.waitFor(() => expect(resolveB).toBeDefined());
      await chat.send('continuing');
      resolveB!({ succeeded: true, data: { messages: [{ id: 'b1', role: 'user', content: 'from B', creationTime: '' }] } });
      await switching;

      expect(body?.threadId).toBe('t1');
      expect(chat.activeThreadId.value).toBe('t1');
      // The turn is still on screen, under the conversation it went to.
      expect(chat.messages.value.map((m) => m.content)).toEqual(['hi', 'continuing', 'answer']);
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
      expect((deps as unknown as { threadApi: { getMessages: ReturnType<typeof vi.fn> } }).threadApi.getMessages).not.toHaveBeenCalled();
    });

    it('deleting a pending row is a no-op', async () => {
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.deleteThread('pending_123_4');
      expect((deps as unknown as { threadApi: { delete: ReturnType<typeof vi.fn> } }).threadApi.delete).not.toHaveBeenCalled();
    });
  });
  /**
   * `activeThreadId` only moves when the transcript arrives, so before
   * `openingThreadId` a click on a slow thread changed nothing on screen for the
   * whole load, and two paths silently threw a click away: picking the
   * conversation already on screen while another loaded (the other one still
   * landed), and the same while a new conversation's first answer streamed.
   */
  describe('opening a thread', () => {
    type Deps = { threadApi: { getMessages: ReturnType<typeof vi.fn> } };
    const transcript = (content: string) => ({
      succeeded: true,
      data: { messages: [{ id: `${content}-1`, role: 'user', content, creationTime: '' }] },
    });

    /** A thread API whose loads wait until the test answers them. */
    function deferredLoads() {
      const pending = new Map<string, (v: unknown) => void>();
      const deps = api({
        getMessages: vi.fn((id: string) => new Promise((resolve) => pending.set(id, resolve))),
      });
      return { deps, pending };
    }

    it('names the thread being opened at once and clears it with the switch', async () => {
      const { deps, pending } = deferredLoads();
      const chat = useChatThreads(deps);

      const opening = chat.selectThread('B');
      expect(chat.openingThreadId.value).toBe('B');
      expect(chat.activeThreadId.value).toBeUndefined();

      await vi.waitFor(() => expect(pending.has('B')).toBe(true));
      pending.get('B')!(transcript('from B'));
      await opening;

      expect(chat.openingThreadId.value).toBeUndefined();
      expect(chat.activeThreadId.value).toBe('B');
    });

    it('clears it when the load fails, and when the load throws', async () => {
      const onError = vi.fn();
      const deps = api({
        getMessages: vi
          .fn()
          .mockResolvedValueOnce({ succeeded: false, message: 'Network error' })
          .mockRejectedValueOnce(new Error('offline')),
      });
      const chat = useChatThreads({ ...(deps as never), onError } as never);

      await chat.selectThread('B');
      expect(chat.openingThreadId.value).toBeUndefined();

      await chat.selectThread('C');
      expect(chat.openingThreadId.value).toBeUndefined();
      expect(chat.activeThreadId.value).toBeUndefined();
      expect(onError).toHaveBeenCalledTimes(2);
      expect(onError).toHaveBeenLastCalledWith('offline', expect.objectContaining({ operation: 'load-thread' }));
    });

    it('picking the open conversation while another loads keeps it, instead of being overtaken', async () => {
      const deps = api();
      const chat = useChatThreads(deps);
      await chat.selectThread('t1');
      let resolveB: ((v: unknown) => void) | undefined;
      (deps as unknown as Deps).threadApi.getMessages.mockImplementationOnce(
        () => new Promise((resolve) => (resolveB = resolve)),
      );

      const switching = chat.selectThread('B');
      await vi.waitFor(() => expect(resolveB).toBeDefined());
      await chat.selectThread('t1');
      expect(chat.openingThreadId.value).toBeUndefined();

      resolveB!(transcript('from B'));
      await switching;
      expect(chat.activeThreadId.value).toBe('t1');
      expect(chat.messages.value.map((m) => m.content)).toEqual(['hi']);
    });

    it('picking the optimistic row while another loads keeps the new conversation', async () => {
      let release: (() => void) | undefined;
      streamRunner = async () => {
        await new Promise<void>((r) => (release = r));
        return { text: 'done' };
      };
      const { deps, pending } = deferredLoads();
      const chat = useChatThreads(deps);
      const turn = chat.send('first');
      await vi.waitFor(() => expect(release).toBeDefined());
      const pendingRow = chat.threads.value[0]!.id;

      const switching = chat.selectThread('B');
      await vi.waitFor(() => expect(pending.has('B')).toBe(true));
      await chat.selectThread(pendingRow);
      expect(chat.openingThreadId.value).toBeUndefined();

      pending.get('B')!(transcript('from B'));
      await switching;
      expect(chat.isStreaming.value).toBe(true);
      expect(chat.messages.value.map((m) => m.content)).toEqual(['first', '']);

      release?.();
      await turn;
    });

    it('a second click on the thread being opened waits for the same load', async () => {
      const { deps, pending } = deferredLoads();
      const chat = useChatThreads(deps);

      const first = chat.selectThread('B');
      const second = chat.selectThread('B');
      expect(second).toBe(first);
      await vi.waitFor(() => expect(pending.has('B')).toBe(true));
      expect((deps as unknown as Deps).threadApi.getMessages).toHaveBeenCalledTimes(1);

      pending.get('B')!(transcript('from B'));
      await second;
      expect(chat.activeThreadId.value).toBe('B');
    });

    it('a newer click takes over the highlight from the one still loading', async () => {
      const { deps } = deferredLoads();
      const chat = useChatThreads(deps);
      void chat.selectThread('B');
      void chat.selectThread('C');
      expect(chat.openingThreadId.value).toBe('C');
    });

    it('New chat, a send and deleting the loading thread all end the opening', async () => {
      const { deps } = deferredLoads();
      const chat = useChatThreads(deps);

      void chat.selectThread('B');
      chat.newChat();
      expect(chat.openingThreadId.value).toBeUndefined();

      void chat.selectThread('B');
      await chat.send('carry on here');
      expect(chat.openingThreadId.value).toBeUndefined();

      void chat.selectThread('C');
      await chat.deleteThread('C');
      expect(chat.openingThreadId.value).toBeUndefined();
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

    /**
     * External CLI agents send a call's start and its result as two events
     * with the same id, and the start event carries the detail as well as the
     * name. One card per call, settled by id.
     */
    it('a call with an id is one entry from start to result, even when the same tool runs twice', async () => {
      streamRunner = async (opts) => {
        const onToolCall = opts.onToolCall as Cb<string[]>;
        const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
        const start = (id: string) => {
          const event = { isToolCall: true, toolCallNames: ['shot'], toolCalls: [{ id, name: 'shot', status: 'running', isSuccess: true }] };
          onEvent(event);
          onToolCall(['shot']);
        };
        start('a');
        start('b');
        onEvent({ toolCalls: [{ id: 'b', name: 'shot', status: 'completed', isSuccess: true, durationMs: 2 }] });
        onEvent({ toolCalls: [{ id: 'a', name: 'shot', status: 'failed', isSuccess: false, durationMs: 1 }] });
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].toolCalls?.map((c) => [c.id, c.status])).toEqual([
        ['a', 'failed'],
        ['b', 'completed'],
      ]);
    });

    it('a name-only start still adds its entry after an event that carried no start detail', async () => {
      streamRunner = async (opts) => {
        const onToolCall = opts.onToolCall as Cb<string[]>;
        const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
        onEvent({ toolCalls: [{ id: 'a', name: 'shot', status: 'completed', isSuccess: true }] });
        onEvent({ isToolCall: true, toolCallNames: ['search'] });
        onToolCall(['search']);
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('q');
      expect(chat.messages.value[1].toolCalls?.map((c) => c.name)).toEqual(['shot', 'search']);
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
    const pdf = () => new File([new Uint8Array([0x25, 0x50, 0x44, 0x46])], 'doc.pdf', { type: 'application/pdf' });
    const docx = () => new File([new Uint8Array([1, 2, 3])], 'notes.docx', { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });

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
        { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png', fileName: 'shot.png' },
      ]);
      expect(chat.messages.value[0].attachments).toEqual([
        { type: 'image', fileName: 'shot.png', mediaType: 'image/png', base64Data: 'iVBORw==', size: 4 },
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
        { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png', fileName: 'shot.png' },
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

    it('sends a PDF inline as a document part', async () => {
      let body: Record<string, unknown> | undefined;
      streamRunner = async (opts) => {
        body = opts.body as Record<string, unknown>;
        return { text: 'ok' };
      };
      const chat = useChatThreads(api());
      await chat.send('summarise', [pdf()]);
      expect(body?.content).toEqual([
        { type: 'text', text: 'summarise' },
        { type: 'document', base64Data: 'JVBERg==', mediaType: 'application/pdf', fileName: 'doc.pdf' },
      ]);
      expect(chat.messages.value[0].attachments?.[0]).toMatchObject({ type: 'file', fileName: 'doc.pdf' });
    });

    it('refuses a file that is neither an image nor a PDF through onError and sends nothing, keeping the draft', async () => {
      const onError = vi.fn();
      const sent: unknown[] = [];
      streamRunner = async (opts) => {
        sent.push(opts.body);
        return { text: '' };
      };
      const chat = useChatThreads({ ...(api() as never), onError } as never);
      chat.inputText.value = 'summarise this';
      await chat.send('summarise this', [docx()]);

      expect(sent).toEqual([]);
      expect(onError).toHaveBeenCalledTimes(1);
      expect(onError.mock.calls[0][0]).toContain('notes.docx');
      expect(chat.messages.value).toEqual([]);
      expect(chat.threads.value).toEqual([]);
      expect(chat.isStreaming.value).toBe(false);
      expect(chat.inputText.value).toBe('summarise this');
    });

    /**
     * A composer bound with `v-model` (TChatApp's) clears the draft the moment
     * it emits `send`, before the attachments are read. "The draft intact on
     * refusal" therefore has to mean the hook puts it back; otherwise a long
     * prompt is lost with the refused file.
     */
    it('puts the draft back when a composer cleared it before the refusal', async () => {
      const chat = useChatThreads({ ...(api() as never), onError: vi.fn() } as never);
      chat.inputText.value = '';
      await chat.send('a long prompt about the attached report', [docx()]);
      expect(chat.messages.value).toEqual([]);
      expect(chat.inputText.value).toBe('a long prompt about the attached report');
    });

    it('does not overwrite what the user typed while a refused upload was in flight', async () => {
      let rejectUpload: ((err: Error) => void) | undefined;
      const uploadFile = vi.fn(() => new Promise<{ id: string }>((_, reject) => (rejectUpload = reject)));
      const chat = useChatThreads({ ...(api() as never), uploadFile, onError: vi.fn() } as never);
      const turn = chat.send('first draft', [docx()]);
      await vi.waitFor(() => expect(rejectUpload).toBeDefined());
      chat.inputText.value = 'something new';
      rejectUpload!(new Error('upload failed'));
      await turn;
      expect(chat.inputText.value).toBe('something new');
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
      const turn = chat.send('summarise', [docx()]);
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
      await chat.send('summarise', [docx()]);
      expect(body?.content).toEqual([
        { type: 'text', text: 'summarise' },
        { type: 'file', fileId: 'file-9', fileName: 'notes.docx' },
      ]);
      expect(chat.messages.value[0].attachments).toEqual([{ type: 'file', fileId: 'file-9', fileName: 'notes.docx' }]);
    });
  });
});
