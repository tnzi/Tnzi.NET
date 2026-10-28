import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * A turn that runs into the user's usage limit, and the model the user picked.
 *
 * The server refuses an over-limit turn BEFORE writing anything (no thread, no
 * user message, no run), with `AI_USAGE_LIMIT_REACHED` and the window and reset
 * time in `errorDetails`. The hook must mirror that: nothing of the turn stays
 * on screen, the text goes back into the composer, and `usageLimit` says why.
 */
type Opts = Record<string, unknown>;
type Cb<T> = (value: T) => void;
let streamRunner: (opts: Opts) => Promise<{ text: string; reasoning?: string }>;
let bodies: Opts[] = [];

/** Same shape as the real class: an HTTP status and the body the server answered with. */
class ChatStreamRequestError extends Error {
  constructor(readonly status: number, readonly detail: string) {
    super(`Stream request failed: ${status} ${detail}`);
  }
}
vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: (opts: Opts) => {
    bodies.push((opts.body as Opts) ?? {});
    return streamRunner(opts);
  },
  ChatStreamRequestError,
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');

const DENIAL = { window: 'Weekly', resetsAt: '2026-10-23T02:00:00Z' };

function refuse(opts: Opts, envelope: Record<string, unknown>, status = 429): void {
  (opts.onError as Cb<Error>)(new ChatStreamRequestError(status, JSON.stringify(envelope)));
}

const limitEnvelope = {
  succeeded: false,
  code: 429,
  errorCode: 'AI_USAGE_LIMIT_REACHED',
  message: 'You have reached your weekly usage limit.',
  errorDetails: DENIAL,
};

function deps(overrides: Record<string, unknown> = {}) {
  const threadApi = {
    getList: vi.fn(async () => ({ succeeded: true, data: { items: [] } })),
    getMessages: vi.fn(async () => ({
      succeeded: true,
      data: {
        messages: [
          { id: 'm1', role: 'user', content: 'first', creationTime: '2026-10-21T10:00:00Z' },
          { id: 'm2', role: 'assistant', content: 'answer', creationTime: '2026-10-21T10:00:01Z' },
        ],
      },
    })),
    delete: vi.fn(async () => ({ succeeded: true })),
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
      getRunStreamUrl: (runId: string) => `/api/ai/chat/runs/${runId}/stream`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
    },
    threadApi,
    ...overrides,
  };
}

beforeEach(() => {
  bodies = [];
  streamRunner = async () => ({ text: 'done' });
});

describe('a turn refused at the usage limit', () => {
  it('leaves nothing on screen, puts the text back and says which window and when', async () => {
    streamRunner = async (opts) => {
      refuse(opts, limitEnvelope);
      return { text: '' };
    };
    const onUsageLimit = vi.fn();
    const d = deps();
    const chat = useChatThreads({ ...(d as never), onUsageLimit });
    chat.inputText.value = 'please summarise';

    await chat.send('please summarise');

    expect(chat.messages.value).toEqual([]);
    expect(chat.threads.value).toEqual([]);
    expect(chat.inputText.value).toBe('please summarise');
    expect(chat.isStreaming.value).toBe(false);
    expect(chat.usageLimit.value).toEqual(DENIAL);
    expect(onUsageLimit).toHaveBeenCalledWith(DENIAL);
    // Nothing was created, so there is nothing to re-read.
    expect(d.threadApi.getList).not.toHaveBeenCalled();
  });

  it('does not overwrite text typed while the request was in flight', async () => {
    const chat = useChatThreads(deps() as never);
    streamRunner = async (opts) => {
      chat.inputText.value = 'something newer';
      refuse(opts, limitEnvelope);
      return { text: '' };
    };
    await chat.send('older');
    expect(chat.inputText.value).toBe('something newer');
  });

  it('in an open conversation only this turn goes; the earlier messages stay', async () => {
    const chat = useChatThreads(deps() as never);
    await chat.selectThread('t1');
    streamRunner = async (opts) => {
      refuse(opts, limitEnvelope);
      return { text: '' };
    };
    await chat.send('one more');
    expect(chat.messages.value.map((m) => m.content)).toEqual(['first', 'answer']);
  });

  it('a refused regenerate shows the thread as the server kept it', async () => {
    const d = deps();
    const chat = useChatThreads(d as never);
    await chat.selectThread('t1');
    streamRunner = async (opts) => {
      refuse(opts, limitEnvelope);
      return { text: '' };
    };
    await chat.regenerate();
    await vi.waitFor(() => expect(d.threadApi.getMessages).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(chat.messages.value.map((m) => m.content)).toEqual(['first', 'answer']));
    expect(chat.usageLimit.value).toEqual(DENIAL);
  });

  it('the next turn clears the notice', async () => {
    streamRunner = async (opts) => {
      refuse(opts, limitEnvelope);
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('a');
    expect(chat.usageLimit.value).not.toBeNull();

    streamRunner = async () => ({ text: 'ok' });
    await chat.send('b');
    expect(chat.usageLimit.value).toBeNull();
  });

  it('a limit with no reset (a zero limit) still carries the window', async () => {
    streamRunner = async (opts) => {
      refuse(opts, { ...limitEnvelope, errorDetails: { window: 'Session', resetsAt: null } });
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('a');
    expect(chat.usageLimit.value).toEqual({ window: 'Session', resetsAt: null });
  });

  it('another refusal (a rate limit) stays a failed reply', async () => {
    streamRunner = async (opts) => {
      refuse(opts, { succeeded: false, code: 429, errorCode: 'RATE_LIMITED', message: 'Slow down' });
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('a');
    expect(chat.messages.value[1]).toMatchObject({ status: 'error', errorCode: 'RATE_LIMITED' });
    expect(chat.usageLimit.value).toBeNull();
  });
});

describe('the user limit reached once the turn was under way', () => {
  it('ends the reply as a usage-limit row naming the window, and sets the notice', async () => {
    streamRunner = async (opts) => {
      (opts.onError as Cb<Record<string, unknown>>)({
        isError: true,
        errorCode: 'AI_USAGE_LIMIT_REACHED',
        errorMessage: 'You have reached your session usage limit.',
        usageLimitWindow: 'Session',
        usageLimitResetsAt: '2026-10-21T15:00:00Z',
      });
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('hi');

    expect(chat.messages.value[1]).toMatchObject({
      status: 'usage_limit',
      errorCode: 'AI_USAGE_LIMIT_REACHED',
      usageLimitWindow: 'Session',
      usageLimitResetsAt: '2026-10-21T15:00:00Z',
    });
    // The message was sent and stays in the conversation.
    expect(chat.messages.value[0]).toMatchObject({ role: 'user', content: 'hi' });
    expect(chat.usageLimit.value).toEqual({ window: 'Session', resetsAt: '2026-10-21T15:00:00Z' });
  });

  it("the external CLI account's limit keeps its own code and sets no user notice", async () => {
    streamRunner = async (opts) => {
      (opts.onError as Cb<Record<string, unknown>>)({
        isError: true, errorCode: 'AI_CLI_USAGE_LIMIT', usageLimitResetsAt: '2026-10-21T15:00:00Z',
      });
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    await chat.send('hi');
    expect(chat.messages.value[1]).toMatchObject({ status: 'usage_limit', errorCode: 'AI_CLI_USAGE_LIMIT' });
    expect(chat.usageLimit.value).toBeNull();
  });
});

describe('the picked model', () => {
  it('goes with every kind of turn', async () => {
    const chat = useChatThreads({ ...(deps() as never), model: () => 'claude-opus-5-5' });
    await chat.selectThread('t1');
    await chat.send('a');
    await chat.regenerate();
    const userId = chat.messages.value.find((m) => m.role === 'user' && !m.id.startsWith('m_'))?.id ?? 'm1';
    await chat.editMessage(userId, 'edited');

    expect(bodies).toHaveLength(3);
    expect(bodies.every((b) => b.model === 'claude-opus-5-5')).toBe(true);
  });

  it('no pick sends no model, so the agent decides', async () => {
    const chat = useChatThreads({ ...(deps() as never), model: () => null });
    await chat.send('a');
    expect(bodies[0]).not.toHaveProperty('model');
  });
});
