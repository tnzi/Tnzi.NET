import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope, ref } from 'vue';

/**
 * `useChatThreads` around runs that outlive the request (agents bound to an external CLI
 * runtime), edit and regenerate through the server's rewind, per-turn metadata, history in
 * pages, the welcome message, and stored attachments.
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
const { stashWelcomeMessage } = await import('../../src/headless/welcome-message');

type Cb<T> = (v: T) => void;

function deps(overrides: { threadApi?: Record<string, unknown>; chatApi?: Record<string, unknown> } = {}) {
  const threadApi = {
    getList: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0 } })),
    getMessages: vi.fn(async () => ({
      succeeded: true,
      data: {
        messages: [
          { id: 'u1', role: 'user', content: 'first', creationTime: '2026-10-19T10:00:00Z', order: 1 },
          { id: 'a1', role: 'assistant', content: 'one', creationTime: '2026-10-19T10:00:01Z', order: 2 },
          { id: 'u2', role: 'user', content: 'second', creationTime: '2026-10-19T10:01:00Z', order: 3 },
          { id: 'a2', role: 'assistant', content: 'two', creationTime: '2026-10-19T10:01:01Z', order: 4 },
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

beforeEach(() => {
  streamRunner = async () => ({ text: 'done' });
  sessionStorage.clear();
});

describe('runs that outlive the request', () => {
  it('learns the run from the first event, and Stop cancels it on the server', async () => {
    const d = deps();
    let release: (() => void) | undefined;
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u' });
      await new Promise<void>((r) => (release = r));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));
    expect(chat.messages.value[0].id).toBe('srv-u');

    chat.abort();
    release?.();
    await turn;

    expect(d.chatApi.cancelRun).toHaveBeenCalledWith('r-1');
    expect(chat.messages.value[1].status).toBe('stopped');
    expect(chat.activeRunId.value).toBeUndefined();
  });

  it('a turn without a run is only closed, nothing is cancelled', async () => {
    const d = deps();
    let release: (() => void) | undefined;
    streamRunner = async () => {
      await new Promise<void>((r) => (release = r));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('hi');
    await vi.waitFor(() => expect(release).toBeDefined());
    chat.abort();
    release?.();
    await turn;
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
  });

  it('leaving the page closes the stream but does not cancel the run', async () => {
    const d = deps();
    let release: (() => void) | undefined;
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-1' });
      await new Promise<void>((r) => (release = r));
      return { text: '' };
    };
    const scope = effectScope();
    const chat = scope.run(() => useChatThreads(d as never))!;
    const turn = chat.send('long task');
    await vi.waitFor(() => expect(chat.activeRunId.value).toBe('r-1'));
    scope.stop();
    release?.();
    await turn;
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
  });

  it('opening a thread with a run still going reattaches to it from the start', async () => {
    const d = deps({
      chatApi: { getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [{ runId: 'r-9', userMessageId: 'u2', status: 'Running' }] })) },
    });
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      (opts.onDelta as Cb<string>)('resumed answer');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'a3', runId: 'r-9' });
      return { text: 'resumed answer' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    await vi.waitFor(() => expect(calls).toHaveLength(1));
    await vi.waitFor(() => expect(chat.isStreaming.value).toBe(false));

    expect(calls[0].method).toBe('GET');
    expect(calls[0].url).toBe('/api/chat/runs/r-9/stream?fromSequence=0');
    expect(calls[0].body).toBeUndefined();
    const last = chat.messages.value[chat.messages.value.length - 1];
    expect(last).toMatchObject({ id: 'a3', role: 'assistant', content: 'resumed answer', status: 'done' });
  });
});

describe('usage limit', () => {
  it('ends the reply as its own outcome, with the reset time', async () => {
    streamRunner = async (opts) => {
      (opts.onError as Cb<Record<string, unknown>>)({
        isError: true, errorCode: 'AI_CLI_USAGE_LIMIT', errorMessage: "You've hit your limit", usageLimitResetsAt: '2026-10-19T15:00:00Z',
      });
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('hi');
    expect(chat.messages.value[1]).toMatchObject({ status: 'usage_limit', usageLimitResetsAt: '2026-10-19T15:00:00Z' });
  });

  it('any other failure stays an error', async () => {
    streamRunner = async (opts) => {
      (opts.onError as Cb<Record<string, unknown>>)({ isError: true, errorCode: 'AI_CHAT_FAILED', errorMessage: 'boom' });
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('hi');
    expect(chat.messages.value[1]).toMatchObject({ status: 'error', error: 'boom' });
  });

  it('a refused request shows the server message, not the raw body', async () => {
    streamRunner = async (opts) => {
      (opts.onError as Cb<Error>)(new ChatStreamRequestError(409, JSON.stringify({ message: 'Stop it first.' })));
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('hi');
    expect(chat.messages.value[1].error).toBe('Stop it first.');
  });
});

describe('edit and regenerate', () => {
  it('regenerate asks the server to send the last question again, and drops the answer after it', async () => {
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      return { text: 'two, again' };
    };
    const d = deps();
    const chat = useChatThreads({ ...(d as never), agentId: () => 'agent-1' } as never);
    await chat.selectThread('t1');

    await chat.regenerate({ metadata: { targets: ['pc-1'] } });

    expect(calls[0].body).toEqual({ threadId: 't1', regenerate: true, agentId: 'agent-1', metadata: { targets: ['pc-1'] } });
    expect(chat.messages.value.map((m) => m.content)).toEqual(['first', 'one', 'second', 'two, again']);
  });

  it('edit replaces the message and everything after it, and sends the new text', async () => {
    const calls: StreamOpts[] = [];
    streamRunner = async (opts) => {
      calls.push(opts);
      (opts.onDone as Cb<Record<string, unknown>>)({ userMessageId: 'u1b', assistantMessageId: 'a1b' });
      return { text: 'one, edited' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.selectThread('t1');

    await chat.editMessage('u1', 'first, edited');

    expect(calls[0].body).toEqual({ threadId: 't1', editMessageId: 'u1', message: 'first, edited' });
    expect(chat.messages.value.map((m) => [m.id, m.content])).toEqual([['u1b', 'first, edited'], ['a1b', 'one, edited']]);
  });

  it('a refused edit shows the thread as the server still has it', async () => {
    const d = deps();
    streamRunner = async (opts) => {
      (opts.onError as Cb<Error>)(new ChatStreamRequestError(409, JSON.stringify({ message: 'A reply is in progress.' })));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    await chat.editMessage('u1', 'changed');
    await vi.waitFor(() => expect(d.threadApi.getMessages).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(chat.messages.value.map((m) => m.content)).toEqual(['first', 'one', 'second', 'two']));
  });

  it('a message never stored cannot be edited', async () => {
    const onError = vi.fn();
    const chat = useChatThreads({ ...(deps() as never), onError } as never);
    await chat.selectThread('t1');
    chat.updateMessage('u1', { id: 'm_local_1' });
    await chat.editMessage('m_local_1', 'x');
    expect(onError).toHaveBeenCalled();
  });
});

describe('per-turn metadata', () => {
  it('goes with the turn, and stays on the user row', async () => {
    let body: Record<string, unknown> | undefined;
    streamRunner = async (opts) => {
      body = opts.body as Record<string, unknown>;
      return { text: 'ok' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('restart them', [], { metadata: { targets: ['pc-1', 'pc-2'] } });
    expect(body?.metadata).toEqual({ targets: ['pc-1', 'pc-2'] });
    expect(chat.messages.value[0].metadata).toEqual({ targets: ['pc-1', 'pc-2'] });
  });
});

describe('history in pages', () => {
  it('opens a thread on its newest page and pages back before the oldest message', async () => {
    const getMessages = vi.fn()
      .mockResolvedValueOnce({
        succeeded: true,
        data: { messages: [{ id: 'u5', role: 'user', content: 'five', creationTime: 'x', order: 5 }], hasMore: true },
      })
      .mockResolvedValueOnce({
        succeeded: true,
        data: { messages: [{ id: 'u4', role: 'user', content: 'four', creationTime: 'x', order: 4 }], hasMore: false },
      });
    const chat = useChatThreads({ ...(deps({ threadApi: { getMessages } }) as never), messageLimit: 1 } as never);
    await chat.selectThread('t1');
    expect(chat.hasOlderMessages.value).toBe(true);

    await chat.loadOlderMessages();

    expect(getMessages).toHaveBeenLastCalledWith('t1', { beforeOrder: 5, limit: 1 });
    expect(chat.messages.value.map((m) => m.content)).toEqual(['four', 'five']);
    expect(chat.hasOlderMessages.value).toBe(false);
  });

  it('pages and searches the sidebar list', async () => {
    const getList = vi.fn()
      .mockResolvedValueOnce({ succeeded: true, data: { items: [{ id: 't1', title: 'A', lastActivityTime: 'x' }], totalCount: 2 } })
      .mockResolvedValueOnce({ succeeded: true, data: { items: [{ id: 't2', title: 'B', lastActivityTime: 'x' }], totalCount: 2 } })
      .mockResolvedValueOnce({ succeeded: true, data: { items: [{ id: 't2', title: 'B', lastActivityTime: 'x' }], totalCount: 1 } });
    const chat = useChatThreads({ ...(deps({ threadApi: { getList } }) as never), pageSize: 1 } as never);

    await chat.loadThreads();
    expect(chat.hasMoreThreads.value).toBe(true);
    await chat.loadMoreThreads();
    expect(chat.threads.value.map((t) => t.id)).toEqual(['t1', 't2']);
    expect(chat.hasMoreThreads.value).toBe(false);

    await chat.searchThreads('B');
    expect(getList).toHaveBeenLastCalledWith(expect.objectContaining({ keyword: 'B' }));
    expect(chat.threads.value.map((t) => t.id)).toEqual(['t2']);
  });
});

describe('the welcome message', () => {
  it('a message kept before sign-in is sent once when the hook starts', async () => {
    stashWelcomeMessage('hello from the welcome screen');
    const bodies: unknown[] = [];
    streamRunner = async (opts) => {
      bodies.push(opts.body);
      return { text: 'hi' };
    };
    const chat = useChatThreads(deps() as never);
    await vi.waitFor(() => expect(bodies).toHaveLength(1));
    expect((bodies[0] as { message: string }).message).toBe('hello from the welcome screen');
    expect(chat.messages.value[0].content).toBe('hello from the welcome screen');

    useChatThreads(deps() as never);
    await Promise.resolve();
    expect(bodies).toHaveLength(1);
  });

  it('can be turned off', async () => {
    stashWelcomeMessage('kept');
    const runner = vi.fn(async () => ({ text: '' }));
    streamRunner = runner;
    useChatThreads({ ...(deps() as never), sendWelcomeMessage: false } as never);
    await Promise.resolve();
    expect(runner).not.toHaveBeenCalled();
  });

  /**
   * A product that resolves its agent after start-up used to have the welcome message go
   * out at once, to no agent (a bare model call), and had to turn it off and send it by
   * hand. It now waits for the agent.
   */
  it('waits for an agent resolved after start-up, then goes to that agent', async () => {
    stashWelcomeMessage('hello');
    const bodies: Array<Record<string, unknown>> = [];
    streamRunner = async (opts) => {
      bodies.push(opts.body as Record<string, unknown>);
      return { text: 'hi' };
    };
    const agent = ref<string | undefined>(undefined);
    useChatThreads({ ...(deps() as never), agentId: () => agent.value } as never);
    await new Promise((r) => setTimeout(r, 0));
    expect(bodies).toHaveLength(0);

    agent.value = 'agent-7';
    await vi.waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ message: 'hello', agentId: 'agent-7' });

    agent.value = 'agent-8';
    await new Promise((r) => setTimeout(r, 0));
    expect(bodies).toHaveLength(1);
  });

  it('goes at start-up when the agent is already known', async () => {
    stashWelcomeMessage('hello');
    const bodies: Array<Record<string, unknown>> = [];
    streamRunner = async (opts) => {
      bodies.push(opts.body as Record<string, unknown>);
      return { text: 'hi' };
    };
    useChatThreads({ ...(deps() as never), agentId: () => 'agent-1' } as never);
    await vi.waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ message: 'hello', agentId: 'agent-1' });
  });

  it('does not go out on top of a turn already running; it follows when that turn ends', async () => {
    stashWelcomeMessage('hello');
    const bodies: Array<Record<string, unknown>> = [];
    let release: (() => void) | undefined;
    streamRunner = async (opts) => {
      bodies.push(opts.body as Record<string, unknown>);
      if (bodies.length === 1) await new Promise<void>((r) => (release = r));
      return { text: 'ok' };
    };
    const agent = ref<string | undefined>(undefined);
    const chat = useChatThreads({ ...(deps() as never), agentId: () => agent.value } as never);
    const first = chat.send('typed before the agent arrived');
    await vi.waitFor(() => expect(release).toBeDefined());

    agent.value = 'agent-2';
    await new Promise((r) => setTimeout(r, 0));
    expect(bodies).toHaveLength(1);

    release?.();
    await first;
    await vi.waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[1]).toMatchObject({ message: 'hello', agentId: 'agent-2' });
  });
});

describe('error codes reach the product', () => {
  it('onError carries the server code, details and status of a refused call', async () => {
    const onError = vi.fn();
    const d = deps({
      threadApi: {
        delete: vi.fn(async () => ({
          succeeded: false, code: 409, message: 'A reply is still running', errorCode: 'AI_CHAT_RUN_ACTIVE', errorDetails: { runId: 'r-1' },
        })),
      },
    });
    const chat = useChatThreads({ ...(d as never), onError } as never);
    await chat.deleteThread('t1');
    expect(onError).toHaveBeenCalledWith('A reply is still running', {
      operation: 'delete-thread', message: 'A reply is still running', code: 'AI_CHAT_RUN_ACTIVE', details: { runId: 'r-1' }, status: 409,
    });
  });

  it('a refused turn writes the code and details on the reply row', async () => {
    const envelope = JSON.stringify({
      succeeded: false, code: 400, message: 'Turn metadata is too large', errorCode: 'AI_INVALID_TURN_METADATA', errorDetails: { maxBytes: 4096 },
    });
    streamRunner = async (opts) => {
      (opts.onError as Cb<unknown>)(new ChatStreamRequestError(400, envelope));
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('hi');
    expect(chat.messages.value[1]).toMatchObject({
      status: 'error', error: 'Turn metadata is too large', errorCode: 'AI_INVALID_TURN_METADATA', errorDetails: { maxBytes: 4096 },
    });
  });

  it('describeError words both kinds in the product copy, and a null keeps the original', async () => {
    const copy: Record<string, string> = {
      AI_CLI_USAGE_LIMIT: 'Your plan limit was reached.',
      AI_CHAT_RUN_ACTIVE: 'Wait for the reply to finish.',
    };
    const describeError = vi.fn((e: { code: string | null }) => (e.code ? copy[e.code] : null));
    const onError = vi.fn();
    streamRunner = async (opts) => {
      (opts.onError as Cb<Record<string, unknown>>)({ isError: true, errorCode: 'AI_CLI_USAGE_LIMIT', errorMessage: 'Limit hit' });
      return { text: '' };
    };
    const d = deps({
      threadApi: {
        delete: vi.fn(async () => ({ succeeded: false, code: 409, message: 'still running', errorCode: 'AI_CHAT_RUN_ACTIVE' })),
        getList: vi.fn(async () => ({ succeeded: false, code: 500, message: 'list failed' })),
      },
    });
    const chat = useChatThreads({ ...(d as never), onError, describeError } as never);

    await chat.send('hi');
    expect(chat.messages.value[1]).toMatchObject({
      status: 'usage_limit', error: 'Your plan limit was reached.', errorCode: 'AI_CLI_USAGE_LIMIT',
    });

    await chat.deleteThread('t1');
    expect(onError).toHaveBeenLastCalledWith('Wait for the reply to finish.', expect.objectContaining({ code: 'AI_CHAT_RUN_ACTIVE' }));

    await chat.loadThreads();
    expect(onError).toHaveBeenLastCalledWith('list failed', expect.objectContaining({ operation: 'load-threads', code: null, status: 500 }));
  });

  it('an attachment refused by the hook reports why and for which file', async () => {
    const onError = vi.fn();
    const chat = useChatThreads({ ...(deps() as never), onError } as never);
    await chat.send('see this', [new File(['x'], 'tool.exe', { type: 'application/x-msdownload' })]);
    expect(onError).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({
      operation: 'attachment', code: null, details: { reason: 'unsupported-type', fileName: 'tool.exe' },
    }));
  });
});

describe('stored attachments', () => {
  const created: string[] = [];
  const revoked: string[] = [];
  beforeEach(() => {
    created.length = 0;
    revoked.length = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => {
      const url = `blob:${created.length + 1}`;
      created.push(url);
      return url;
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url: string) => {
      revoked.push(url);
    });
  });
  afterEach(() => vi.restoreAllMocks());

  it('are read once per thread through the client, and released when the thread changes', async () => {
    const d = deps();
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');

    const first = await chat.resolveAttachmentUrl('att-1');
    const again = await chat.resolveAttachmentUrl('att-1');

    expect(first).toBe('blob:1');
    expect(again).toBe('blob:1');
    expect(d.threadApi.getAttachment).toHaveBeenCalledTimes(1);
    expect(d.threadApi.getAttachment).toHaveBeenCalledWith('t1', 'att-1');

    chat.newChat();
    await vi.waitFor(() => expect(revoked).toEqual(['blob:1']));
    expect(await chat.resolveAttachmentUrl('att-1')).toBeNull();
  });
});
