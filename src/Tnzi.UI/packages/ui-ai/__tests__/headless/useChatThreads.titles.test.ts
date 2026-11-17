import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope } from 'vue';

vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: async () => ({ text: 'done' }),
  ChatStreamRequestError: class extends Error {},
}));

const { useChatThreads, TITLE_FOLLOW_DELAYS_MS } = await import('../../src/headless/useChatThreads');

type Row = { id: string; title: string; titlePending?: boolean; lastActivityTime: string };

/** The server names a new conversation in the background: `titlePending` until `name` is called. */
function server(rows: Row[]) {
  let data = rows.map((r) => ({ ...r }));
  return {
    name(id: string, title: string) {
      data = data.map((r) => (r.id === id ? { ...r, title, titlePending: false } : r));
    },
    giveUp(id: string) {
      data = data.map((r) => (r.id === id ? { ...r, titlePending: false } : r));
    },
    getList: vi.fn(async () => ({ succeeded: true, data: { items: data.map((r) => ({ ...r })), totalCount: data.length } })),
    getDetail: vi.fn(async (id: string) => {
      const row = data.find((r) => r.id === id);
      return row ? { succeeded: true, data: { ...row, messageCount: 0, creationTime: row.lastActivityTime, messages: [] } } : { succeeded: false };
    }),
    getMessages: vi.fn(async () => ({ succeeded: true, data: { messages: [] } })),
  };
}

function deps(threadApi: ReturnType<typeof server>) {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(), reportUnauthorized: vi.fn() },
    chatApi: { getChatStreamUrl: () => '/s', getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] })), cancelRun: vi.fn() },
    threadApi,
  } as never;
}

const NOW = '2026-11-01T10:00:00Z';

beforeEach(() => {
  localStorage.clear();
  vi.useFakeTimers();
});

afterEach(() => vi.useRealTimers());

describe('useChatThreads AI titles', () => {
  it('reads a conversation again until its AI title arrives, then shows it without a reload', async () => {
    const api = server([{ id: 'n', title: 'Plan a trip to', titlePending: true, lastActivityTime: NOW }]);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    expect(chat.threads.value[0]).toMatchObject({ title: 'Plan a trip to', titlePending: true });

    await vi.advanceTimersByTimeAsync(TITLE_FOLLOW_DELAYS_MS[0]!);
    expect(api.getDetail).toHaveBeenCalledTimes(1);
    expect(chat.threads.value[0]!.title).toBe('Plan a trip to');

    api.name('n', 'Kyoto Spring Trip');
    await vi.advanceTimersByTimeAsync(TITLE_FOLLOW_DELAYS_MS[1]!);
    expect(chat.threads.value[0]).toMatchObject({ title: 'Kyoto Spring Trip' });
    expect(chat.threads.value[0]!.titlePending).toBeFalsy();

    await vi.advanceTimersByTimeAsync(60_000);
    expect(api.getDetail).toHaveBeenCalledTimes(2);
  });

  it('does not read anything when no title is coming', async () => {
    const api = server([{ id: 'a', title: 'Alpha', lastActivityTime: NOW }]);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();

    await vi.advanceTimersByTimeAsync(60_000);
    expect(api.getDetail).not.toHaveBeenCalled();
  });

  it('gives up after the last wait and keeps the fallback', async () => {
    const api = server([{ id: 'n', title: 'Plan a trip to', titlePending: true, lastActivityTime: NOW }]);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();

    await vi.advanceTimersByTimeAsync(120_000);
    expect(api.getDetail).toHaveBeenCalledTimes(TITLE_FOLLOW_DELAYS_MS.length);
    expect(chat.threads.value[0]!.title).toBe('Plan a trip to');
  });

  it('a list reload while waiting does not start a second follow-up', async () => {
    const api = server([{ id: 'n', title: 'Plan a trip to', titlePending: true, lastActivityTime: NOW }]);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await chat.loadThreads();

    await vi.advanceTimersByTimeAsync(TITLE_FOLLOW_DELAYS_MS[0]!);
    expect(api.getDetail).toHaveBeenCalledTimes(1);
  });

  it('stops reading when the scope is disposed', async () => {
    const api = server([{ id: 'n', title: 'Plan a trip to', titlePending: true, lastActivityTime: NOW }]);
    const scope = effectScope();
    const chat = scope.run(() => useChatThreads(deps(api)))!;
    await chat.loadThreads();

    scope.stop();
    await vi.advanceTimersByTimeAsync(120_000);
    expect(api.getDetail).not.toHaveBeenCalled();
  });
});
