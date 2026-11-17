import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApprovalQueue, type ApprovalSource, type PendingInteraction } from '../../src/headless/approval-queue';

const NOW = Date.parse('2026-11-08T10:00:00Z');
const later = (minutes = 5) => new Date(NOW + minutes * 60_000).toISOString();
const at = (seconds: number) => new Date(NOW + seconds * 1000).toISOString();

function item(key: string, createdAt = at(0), expiresAt = later()): PendingInteraction {
  return { key, kind: 'permission', expiresAt, createdAt, data: { key } };
}

function source(id: string, list: () => readonly PendingInteraction[] | Promise<readonly PendingInteraction[]>) {
  return {
    id,
    load: vi.fn(async (_threadId: string) => list()),
    answer: vi.fn(async () => 'answered' as const),
  } satisfies ApprovalSource;
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('createApprovalQueue', () => {
  it('asks every source when a running conversation is followed, and lists them oldest first', async () => {
    const a = source('a', () => [item('a:2', at(2))]);
    const b = source('b', () => [item('b:1', at(1))]);
    const queue = createApprovalQueue({ sources: [a, b], now: () => Date.now() });

    queue.follow('t1', true);
    await vi.waitFor(() => expect(queue.items.value.map((i) => i.key)).toEqual(['b:1', 'a:2']));
    expect(a.load).toHaveBeenCalledWith('t1');
    expect(queue.items.value.map((i) => i.source)).toEqual(['b', 'a']);
  });

  it('asks again on its interval while the turn runs, once more when it ends, then stops asking', async () => {
    let list = [item('a:1')];
    const a = source('a', () => list);
    const queue = createApprovalQueue({ sources: [a], intervalMs: 1000 });
    queue.follow('t1', true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    list = [item('a:1'), item('a:2', at(1))];
    await vi.advanceTimersByTimeAsync(1000);
    expect(queue.items.value).toHaveLength(2);

    // a:1 still waits after the turn; a:2 went with it
    list = [item('a:1')];
    const before = a.load.mock.calls.length;
    queue.follow('t1', false);
    await vi.advanceTimersByTimeAsync(0);
    expect(a.load.mock.calls.length).toBe(before + 1);
    expect(queue.items.value.map((i) => i.key)).toEqual(['a:1']);

    await vi.advanceTimersByTimeAsync(5000);
    expect(a.load.mock.calls.length).toBe(before + 1);
    expect(queue.items.value.map((i) => i.key)).toEqual(['a:1']);
  });

  it('asks an idle conversation once on opening, shows what waits, and does not poll it', async () => {
    const a = source('a', () => [item('a:1')]);
    const b = source('b', () => [item('b:1', at(1))]);
    const queue = createApprovalQueue({ sources: [a, b], intervalMs: 1000 });
    queue.follow('t1', false);
    await vi.waitFor(() => expect(queue.items.value.map((i) => i.key)).toEqual(['a:1', 'b:1']));
    expect(a.load).toHaveBeenCalledWith('t1');
    expect(b.load).toHaveBeenCalledWith('t1');

    queue.follow('t1', false);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(a.load).toHaveBeenCalledTimes(1);
    expect(b.load).toHaveBeenCalledTimes(1);
    queue.dispose();
  });

  it('the ask when a turn ends waits for an ask already out, which may predate the change', async () => {
    let list = [item('a:1'), item('a:2', at(1))];
    let release: (() => void) | undefined;
    const a = source('a', () => {
      const snapshot = list;
      return new Promise<readonly PendingInteraction[]>((r) => (release = () => r(snapshot)));
    });
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);
    list = [item('a:1')];
    queue.follow('t1', false);
    release?.();
    await vi.waitFor(() => expect(a.load).toHaveBeenCalledTimes(2));
    release?.();
    await vi.waitFor(() => expect(queue.items.value.map((i) => i.key)).toEqual(['a:1']));
  });

  it('another conversation starts clean and is asked for itself, even with an ask out for the last one', async () => {
    let resolveT1: ((v: PendingInteraction[]) => void) | undefined;
    const a = source('a', () => [] as PendingInteraction[]);
    a.load.mockImplementation(async (threadId: string) =>
      threadId === 't1' ? new Promise<PendingInteraction[]>((r) => (resolveT1 = r)) : [item('a:t2')],
    );
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', false);
    queue.push('a', item('a:pushed'));
    expect(queue.items.value).toHaveLength(1);

    queue.follow('t2', false);
    expect(queue.items.value).toEqual([]);
    await vi.waitFor(() => expect(queue.items.value.map((i) => i.key)).toEqual(['a:t2']));
    resolveT1?.([item('a:t1')]);
    await vi.runAllTimersAsync();
    expect(queue.items.value.map((i) => i.key)).toEqual(['a:t2']);
    expect(a.load).toHaveBeenCalledWith('t2');
  });

  it('following nothing clears the cards and asks nobody', async () => {
    const a = source('a', () => [item('a:1')]);
    const queue = createApprovalQueue({ sources: [a], intervalMs: 1000 });
    queue.follow('t1', false);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));
    queue.follow(null, false);
    expect(queue.items.value).toEqual([]);
    await vi.advanceTimersByTimeAsync(5000);
    expect(a.load).toHaveBeenCalledTimes(1);
  });

  it('answers a card on an idle conversation as during a turn', async () => {
    const a = source('a', () => [item('a:1'), item('a:2', at(1))]);
    a.answer.mockImplementationOnce(async () => 'answered' as const);
    a.answer.mockImplementationOnce(async () => 'closed' as const);
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', false);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(2));

    await expect(queue.answer('a:1', { optionId: 'allow_once' })).resolves.toBe('answered');
    await expect(queue.answer('a:2', { optionId: 'x' })).resolves.toBe('closed');
    expect(queue.items.value).toEqual([]);
    // still listed by its source for a moment: stays out
    await queue.refresh();
    expect(queue.items.value).toEqual([]);
  });

  it('drops an answer that lands for a conversation no longer followed', async () => {
    let resolve: ((v: PendingInteraction[]) => void) | undefined;
    const a = source('a', () => [] as PendingInteraction[]);
    a.load.mockImplementation(async (threadId: string) =>
      threadId === 't1' ? new Promise<PendingInteraction[]>((r) => (resolve = r)) : [],
    );
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);
    queue.follow('t2', false);
    resolve?.([item('a:1')]);
    await vi.runAllTimersAsync();
    expect(queue.items.value).toEqual([]);
  });

  it('shows a pushed item at once and keeps it through an ask that was out before it appeared', async () => {
    let resolve: ((v: PendingInteraction[]) => void) | undefined;
    const a = source('a', () => new Promise<PendingInteraction[]>((r) => (resolve = r)));
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);

    queue.push('a', item('a:new'));
    expect(queue.items.value.map((i) => i.key)).toEqual(['a:new']);
    resolve?.([]);
    await vi.waitFor(() => expect(a.load).toHaveBeenCalled());
    await Promise.resolve();
    expect(queue.items.value.map((i) => i.key)).toEqual(['a:new']);
  });

  it('ignores a pushed item of an unknown source, an expired one, and one already answered', () => {
    const a = source('a', () => []);
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);
    queue.push('nobody', item('x'));
    queue.push('a', item('old', at(0), at(-1)));
    queue.remove('done');
    queue.push('a', item('done'));
    expect(queue.items.value).toEqual([]);
  });

  it('keeps a removed card out until its source stops listing it', async () => {
    let list = [item('a:1')];
    const a = source('a', () => list);
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    queue.remove('a:1');
    expect(queue.items.value).toEqual([]);
    await queue.refresh();
    expect(queue.items.value).toEqual([]);

    list = [];
    await queue.refresh();
    list = [item('a:1')];
    await queue.refresh();
    expect(queue.items.value).toHaveLength(1);
  });

  it('a source that fails keeps what it showed and reports it; the others go on', async () => {
    let failing = false;
    const a = source('a', () => {
      if (failing) throw new Error('down');
      return [item('a:1')];
    });
    const b = source('b', () => [item('b:1', at(1))]);
    const onError = vi.fn();
    const queue = createApprovalQueue({ sources: [a, b], intervalMs: 0, onError });
    queue.follow('t1', true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(2));

    failing = true;
    await queue.refresh();
    expect(queue.items.value.map((i) => i.key)).toEqual(['a:1', 'b:1']);
    expect(onError).toHaveBeenCalledWith(expect.any(Error), 'a');
  });

  it('answers through the item source: busy meanwhile, gone after, and a closed item goes too', async () => {
    const a = source('a', () => [item('a:1'), item('a:2', at(1))]);
    let finish: (() => void) | undefined;
    a.answer.mockImplementationOnce(() => new Promise<'answered'>((r) => (finish = () => r('answered'))));
    a.answer.mockImplementationOnce(async () => 'closed' as const);
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(2));

    const first = queue.answer('a:1', { optionId: 'allow_once' });
    expect(queue.answering.value.has('a:1')).toBe(true);
    finish?.();
    await expect(first).resolves.toBe('answered');
    expect(queue.answering.value.has('a:1')).toBe(false);
    expect(a.answer).toHaveBeenCalledWith(expect.objectContaining({ key: 'a:1' }), { optionId: 'allow_once' });

    await expect(queue.answer('a:2', { optionId: 'x' })).resolves.toBe('closed');
    expect(queue.items.value).toEqual([]);
    await expect(queue.answer('missing', { optionId: 'x' })).resolves.toBe('closed');
  });

  it('a refused answer keeps the card and rejects', async () => {
    const a = source('a', () => [item('a:1')]);
    a.answer.mockRejectedValueOnce(new Error('nope'));
    const queue = createApprovalQueue({ sources: [a], intervalMs: 0 });
    queue.follow('t1', true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    await expect(queue.answer('a:1', { optionId: 'x' })).rejects.toThrow('nope');
    expect(queue.items.value).toHaveLength(1);
    expect(queue.answering.value.size).toBe(0);
  });
});
