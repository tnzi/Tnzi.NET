import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope } from 'vue';
import {
  useThreadAttention,
  ATTENTION_IDLE_POLL_MS,
  ATTENTION_RUNNING_POLL_MS,
} from '../../src/headless/useThreadAttention';

type Summary = {
  awaitingOwnerCount?: number;
  awaitingOwnerThreadIds?: string[];
  runningCount: number;
  unreadCount: number;
  runningThreadIds: string[];
  unreadThreadIds: string[];
};

/** A server whose summary a test sets; `markRead` resolves when the test says so. */
function server(initial: Partial<Summary> = {}) {
  let summary: Summary = { runningCount: 0, unreadCount: 0, runningThreadIds: [], unreadThreadIds: [], ...initial };
  let releaseMark: (() => void) | null = null;
  return {
    set(next: Partial<Summary>) {
      summary = { ...summary, ...next };
    },
    getAttention: vi.fn(async (_query?: unknown) => ({ succeeded: true, code: 200, data: { ...summary } })),
    markRead: vi.fn(
      (_id: string) =>
        new Promise<{ succeeded: boolean }>((resolve) => {
          releaseMark = () => resolve({ succeeded: true });
        }),
    ),
    releaseMark: () => releaseMark?.(),
  };
}

const flush = () => vi.advanceTimersByTimeAsync(0);

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('useThreadAttention', () => {
  it('reads the summary at once, with the agent filter', async () => {
    const api = server({ runningCount: 1, runningThreadIds: ['a'], unreadCount: 2, unreadThreadIds: ['b', 'c'] });
    const attention = useThreadAttention({ threadApi: api as never, agentIds: () => 'agent-1' });
    await flush();

    expect(api.getAttention).toHaveBeenCalledWith({ agentIds: ['agent-1'] });
    expect(attention.loaded.value).toBe(true);
    expect(attention.runningCount.value).toBe(1);
    expect(attention.unreadCount.value).toBe(2);
    expect([...attention.runningIds.value]).toEqual(['a']);
    expect([...attention.unreadIds.value]).toEqual(['b', 'c']);
    expect(attention.active.value).toBe(true);
    attention.dispose();
  });

  it('sends no filter for no agents', async () => {
    const api = server();
    const attention = useThreadAttention({ threadApi: api as never, agentIds: () => [] });
    await flush();
    expect(api.getAttention).toHaveBeenCalledWith({ agentIds: null });
    attention.dispose();
  });

  it('polls fast while something runs and slowly otherwise', async () => {
    const api = server({ runningCount: 1, runningThreadIds: ['a'] });
    const attention = useThreadAttention({ threadApi: api as never });
    await flush();
    expect(api.getAttention).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(ATTENTION_RUNNING_POLL_MS);
    expect(api.getAttention).toHaveBeenCalledTimes(2);

    api.set({ runningCount: 0, runningThreadIds: [] });
    await vi.advanceTimersByTimeAsync(ATTENTION_RUNNING_POLL_MS);
    expect(api.getAttention).toHaveBeenCalledTimes(3);
    expect(attention.runningCount.value).toBe(0);

    await vi.advanceTimersByTimeAsync(ATTENTION_RUNNING_POLL_MS);
    expect(api.getAttention).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS);
    expect(api.getAttention).toHaveBeenCalledTimes(4);
    attention.dispose();
  });

  it('reads the conversations awaiting the user, counts them as active and polls fast while there are any', async () => {
    const api = server({ awaitingOwnerCount: 2, awaitingOwnerThreadIds: ['w', 'x'] });
    const attention = useThreadAttention({ threadApi: api as never });
    await flush();

    expect(attention.awaitingOwnerCount.value).toBe(2);
    expect([...attention.awaitingOwnerIds.value]).toEqual(['w', 'x']);
    expect(attention.active.value).toBe(true);
    await vi.advanceTimersByTimeAsync(ATTENTION_RUNNING_POLL_MS);
    expect(api.getAttention).toHaveBeenCalledTimes(2);

    // A backend that does not report it reads as none
    api.set({ awaitingOwnerCount: undefined, awaitingOwnerThreadIds: undefined });
    await attention.refresh();
    expect(attention.awaitingOwnerCount.value).toBe(0);
    expect(attention.active.value).toBe(false);
    attention.dispose();
  });

  it('folds refreshes asked for during a read into one more read', async () => {
    const api = server();
    const attention = useThreadAttention({ threadApi: api as never, idlePollMs: 0 });
    void attention.refresh();
    void attention.refresh();
    await attention.refresh();
    await flush();
    expect(api.getAttention).toHaveBeenCalledTimes(2);
    attention.dispose();
  });

  it('takes a conversation off the unread list at once, and a read from before the mark does not bring it back', async () => {
    const api = server({ unreadCount: 2, unreadThreadIds: ['a', 'b'] });
    const attention = useThreadAttention({ threadApi: api as never, idlePollMs: 0 });
    await flush();

    const marking = attention.markRead('a');
    expect([...attention.unreadIds.value]).toEqual(['b']);
    expect(attention.unreadCount.value).toBe(1);
    expect(api.markRead).toHaveBeenCalledWith('a');

    // The server has not taken the mark yet: its summary still lists the conversation
    await attention.refresh();
    expect([...attention.unreadIds.value]).toEqual(['b']);
    expect(attention.unreadCount.value).toBe(1);

    api.releaseMark();
    await marking;
    api.set({ unreadCount: 1, unreadThreadIds: ['b'] });
    await attention.refresh();
    expect(attention.unreadCount.value).toBe(1);

    // A later reply makes it unread again: once the mark is behind every read, the summary is taken as it is
    api.set({ unreadCount: 2, unreadThreadIds: ['a', 'b'] });
    await attention.refresh();
    expect([...attention.unreadIds.value].sort()).toEqual(['a', 'b']);
    attention.dispose();
  });

  it('stops asking a backend without the endpoint', async () => {
    const api = server();
    api.getAttention.mockResolvedValue({ succeeded: false, code: 404, data: undefined as never });
    const attention = useThreadAttention({ threadApi: api as never });
    await flush();
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS * 3);
    expect(api.getAttention).toHaveBeenCalledTimes(1);
    expect(attention.loaded.value).toBe(false);
    attention.dispose();
  });

  it('keeps the last answer when a read fails, and tries again on the next tick', async () => {
    const api = server({ unreadCount: 1, unreadThreadIds: ['a'] });
    const attention = useThreadAttention({ threadApi: api as never });
    await flush();
    api.getAttention.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS);
    expect(attention.unreadCount.value).toBe(1);
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS);
    expect(api.getAttention).toHaveBeenCalledTimes(3);
    attention.dispose();
  });

  it('does not poll a hidden page, and reads again when it is shown', async () => {
    const api = server();
    const attention = useThreadAttention({ threadApi: api as never });
    await flush();

    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS * 3);
    expect(api.getAttention).toHaveBeenCalledTimes(1);

    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
    await flush();
    expect(api.getAttention).toHaveBeenCalledTimes(2);
    attention.dispose();
  });

  it('waits for start() when not immediate, and stops with its scope', async () => {
    const api = server();
    const scope = effectScope();
    const attention = scope.run(() => useThreadAttention({ threadApi: api as never, immediate: false }))!;
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS);
    expect(api.getAttention).not.toHaveBeenCalled();

    attention.start();
    await flush();
    expect(api.getAttention).toHaveBeenCalledTimes(1);

    scope.stop();
    await vi.advanceTimersByTimeAsync(ATTENTION_IDLE_POLL_MS * 3);
    expect(api.getAttention).toHaveBeenCalledTimes(1);
  });
});
