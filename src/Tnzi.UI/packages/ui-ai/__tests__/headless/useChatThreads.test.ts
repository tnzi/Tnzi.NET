import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * `streamChat` is the transport; the hook's job is everything AROUND it, so it
 * is mocked and driven by hand. `streamRunner` lets a test decide what the
 * "server" does for that turn.
 */
let streamRunner: (opts: Record<string, unknown>) => Promise<{ text: string; reasoning?: string }>;
vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: (opts: Record<string, unknown>) => streamRunner(opts),
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
    http: { getAccessToken: () => 'tok' },
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
      expect(chat.messages.value[1].content).toContain('boom');
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
});
