import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: async () => ({ text: 'done' }),
  ChatStreamRequestError: class extends Error {},
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');
const { ATTENTION_IDLE_POLL_MS } = await import('../../src/headless/useThreadAttention');

const NOW = '2026-11-01T10:00:00Z';

type Row = {
  id: string;
  title: string;
  lastActivityTime: string;
  lastUserMessageTime?: string;
  creationTime?: string;
  running?: boolean;
  awaitingOwner?: boolean;
  unread?: boolean;
};
type Summary = {
  awaitingOwnerCount: number;
  runningCount: number;
  unreadCount: number;
  awaitingOwnerThreadIds: string[];
  runningThreadIds: string[];
  unreadThreadIds: string[];
};

function server(rows: Row[], initial: Partial<Summary> = {}) {
  let summary: Summary = {
    awaitingOwnerCount: 0,
    runningCount: 0,
    unreadCount: 0,
    awaitingOwnerThreadIds: [],
    runningThreadIds: [],
    unreadThreadIds: [],
    ...initial,
  };
  return {
    set(next: Partial<Summary>) {
      summary = { ...summary, ...next };
    },
    getList: vi.fn(async () => ({ succeeded: true, data: { items: rows.map((r) => ({ ...r })), totalCount: rows.length } })),
    getMessages: vi.fn(async () => ({ succeeded: true, data: { messages: [] } })),
    getAttention: vi.fn(async (_query?: unknown) => ({ succeeded: true, code: 200, data: { ...summary } })),
    markRead: vi.fn(async (id: string) => {
      summary = {
        ...summary,
        unreadThreadIds: summary.unreadThreadIds.filter((t) => t !== id),
        unreadCount: summary.unreadThreadIds.includes(id) ? summary.unreadCount - 1 : summary.unreadCount,
      };
      return { succeeded: true };
    }),
  };
}

function deps(threadApi: ReturnType<typeof server>, extra: Record<string, unknown> = {}) {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(), reportUnauthorized: vi.fn() },
    chatApi: { getChatStreamUrl: () => '/s', getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] })), cancelRun: vi.fn() },
    threadApi,
    ...extra,
  } as never;
}

const flush = () => vi.advanceTimersByTimeAsync(0);

beforeEach(() => {
  localStorage.clear();
  vi.useFakeTimers();
});

afterEach(() => vi.useRealTimers());

describe('useChatThreads running and unread', () => {
  it('lays the summary on the list rows and counts over all conversations', async () => {
    const api = server(
      [
        { id: 'a', title: 'Alpha', lastActivityTime: NOW },
        { id: 'b', title: 'Beta', lastActivityTime: NOW },
        { id: 'c', title: 'Gamma', lastActivityTime: NOW },
      ],
      { runningCount: 1, runningThreadIds: ['a'], unreadCount: 3, unreadThreadIds: ['b', 'z', 'y'] },
    );
    const chat = useChatThreads(deps(api, { listAgentIds: () => 'chat-agent' }));
    await chat.loadThreads();
    await flush();

    expect(api.getAttention).toHaveBeenCalledWith({ agentIds: ['chat-agent'] });
    const byId = Object.fromEntries(chat.threads.value.map((t) => [t.id, t]));
    expect(byId.a).toMatchObject({ running: true });
    expect(byId.a!.unread).toBeUndefined();
    expect(byId.b).toMatchObject({ unread: true });
    expect(byId.c!.running).toBeUndefined();
    expect(byId.c!.unread).toBeUndefined();
    expect(chat.attention.runningCount.value).toBe(1);
    expect(chat.attention.unreadCount.value).toBe(3);
  });

  it('keeps the flags the list read said until the first summary arrives', async () => {
    const api = server([{ id: 'a', title: 'Alpha', lastActivityTime: NOW, running: true, unread: true }]);
    api.getAttention.mockImplementation(() => new Promise(() => {}));
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    expect(chat.threads.value[0]).toMatchObject({ running: true, unread: true });
  });

  it('marks a conversation read when it opens, and never shows the open one as unread', async () => {
    const api = server(
      [
        { id: 'a', title: 'Alpha', lastActivityTime: NOW },
        { id: 'b', title: 'Beta', lastActivityTime: NOW },
      ],
      { unreadCount: 2, unreadThreadIds: ['a', 'b'] },
    );
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await flush();

    await chat.selectThread('a');
    expect(api.markRead).toHaveBeenCalledWith('a');
    expect(chat.threads.value.find((t) => t.id === 'a')!.unread).toBeUndefined();
    expect(chat.threads.value.find((t) => t.id === 'b')!.unread).toBe(true);
    expect(chat.attention.unreadCount.value).toBe(1);

    // A reply lands in the open conversation while the page is in view: marked read again when the summary says so
    api.set({ unreadCount: 2, unreadThreadIds: ['a', 'b'] });
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS);
    expect(api.markRead).toHaveBeenCalledTimes(2);
    expect(chat.threads.value.find((t) => t.id === 'a')!.unread).toBeUndefined();
  });

  it('reads and marks nothing with attention off', async () => {
    const api = server([{ id: 'a', title: 'Alpha', lastActivityTime: NOW, unread: true }]);
    const chat = useChatThreads(deps(api, { attention: false }));
    await chat.loadThreads();
    await chat.selectThread('a');
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS * 2);

    expect(api.getAttention).not.toHaveBeenCalled();
    expect(api.markRead).not.toHaveBeenCalled();
    expect(chat.attention.unreadCount.value).toBe(0);
  });

  it('takes the poll intervals it is given', async () => {
    const api = server([], { runningCount: 1, runningThreadIds: ['a'] });
    useChatThreads(deps(api, { attention: { runningPollMs: 1_000, idlePollMs: 0 } }));
    await flush();
    await vi.advanceTimersByTimeAsync(1_000);
    expect(api.getAttention).toHaveBeenCalledTimes(2);
  });
});

describe('useChatThreads list order', () => {
  /* In the server's order: the user's last message, newest first. */
  const rows = (): Row[] => [
    { id: 'a', title: 'Alpha', lastActivityTime: NOW, lastUserMessageTime: '2026-11-01T09:00:00Z', creationTime: '2026-10-01T00:00:00Z' },
    { id: 'b', title: 'Beta', lastActivityTime: NOW, lastUserMessageTime: '2026-10-31T09:00:00Z', creationTime: '2026-10-01T00:00:00Z' },
    { id: 'c', title: 'Gamma', lastActivityTime: NOW, lastUserMessageTime: '2026-10-30T09:00:00Z', creationTime: '2026-10-01T00:00:00Z' },
  ];
  const order = (chat: ReturnType<typeof useChatThreads>) => chat.threads.value.map((t) => t.id);

  it('puts the conversations awaiting the user first and the running ones next, and puts them back when they stop', async () => {
    const api = server(rows(), { awaitingOwnerCount: 1, awaitingOwnerThreadIds: ['c'], runningCount: 1, runningThreadIds: ['b'] });
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await flush();

    expect(order(chat)).toEqual(['c', 'b', 'a']);
    expect(chat.threads.value[0]).toMatchObject({ awaitingOwner: true });
    expect(chat.attention.awaitingOwnerCount.value).toBe(1);

    // Answered and finished, with a reply nobody has read yet: unread is a mark, not a tier
    api.set({ awaitingOwnerCount: 0, awaitingOwnerThreadIds: [], runningCount: 0, runningThreadIds: [], unreadCount: 2, unreadThreadIds: ['b', 'c'] });
    await chat.attention.refresh();
    expect(order(chat)).toEqual(['a', 'b', 'c']);
    expect(chat.threads.value.find((t) => t.id === 'c')).toMatchObject({ unread: true });
  });

  it('the user writing moves the row at once; opening, reading and the turn ending do not', async () => {
    const api = server(rows());
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await flush();

    await chat.selectThread('c');
    await flush();
    expect(order(chat)).toEqual(['a', 'b', 'c']);

    await chat.send('one more thing');
    await flush();
    expect(order(chat)[0]).toBe('c');
    expect(Date.parse(chat.threads.value[0]!.userMessageAt!)).toBeGreaterThan(Date.parse('2026-11-01T09:00:00Z'));
    expect(chat.isStreaming.value).toBe(false);
  });

  it('a new conversation\'s row starts at the top of the running tier, below one awaiting the user', async () => {
    const api = server(rows(), { awaitingOwnerCount: 1, awaitingOwnerThreadIds: ['c'] });
    api.getAttention.mockImplementation(async () => ({
      succeeded: true,
      code: 200,
      data: { awaitingOwnerCount: 1, awaitingOwnerThreadIds: ['c'], runningCount: 0, runningThreadIds: [], unreadCount: 0, unreadThreadIds: [] },
    }));
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await flush();

    const sending = chat.send('a new question');
    expect(order(chat)[0]).toBe('c');
    expect(chat.threads.value[1]).toMatchObject({ pending: true, running: true });
    await sending;
  });
});
