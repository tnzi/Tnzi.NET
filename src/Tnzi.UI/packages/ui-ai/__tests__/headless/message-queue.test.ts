import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { QueuedMessageDto } from '@tnzi/core/services/ai';
import {
  createMessageQueue,
  isChangeable,
  queuedLine,
  queuedText,
  waitsToGo,
  wentOut,
  QUEUE_POLL_MS,
  QUEUE_AFTER_TURN_MS,
} from '../../src/headless/message-queue';

/**
 * A conversation's queued messages on the server: read when a conversation opens and when its turn
 * ends, read again while one waits to go, and changed one by one. A message that went out without
 * this page taking it away is a turn the page did not start, so `onDelivered` fires.
 */
function message(id: string, patch: Partial<QueuedMessageDto> = {}): QueuedMessageDto {
  return { id, threadId: 't-1', text: id, state: 'Queued', sendNow: false, queuedTime: '2026-11-09T00:00:00Z', ...patch };
}

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data });
const refused = (errorCode: string, code = 409) => ({ succeeded: false, success: false, code, errorCode, message: errorCode });

function fakeApi(initial: QueuedMessageDto[] = []) {
  let server = [...initial];
  return {
    set: (next: QueuedMessageDto[]) => (server = [...next]),
    getQueue: vi.fn(async () => ok([...server])),
    queueMessage: vi.fn(async (_t: string, input: { text: string }) => {
      const queued = message(`q-${server.length + 1}`, { text: input.text, afterRunId: 'r-1' });
      server = [...server, queued];
      return ok(queued);
    }),
    updateQueuedMessage: vi.fn(async (_t: string, id: string, text: string) => {
      server = server.map((m) => (m.id === id ? { ...m, text } : m));
      return ok(server.find((m) => m.id === id)!);
    }),
    removeQueuedMessage: vi.fn(async (_t: string, id: string) => {
      server = server.filter((m) => m.id !== id);
      return ok(undefined);
    }),
    sendQueuedMessageNow: vi.fn(async (_t: string, id: string) => {
      server = server.map((m) => (m.id === id ? { ...m, sendNow: true, state: 'Queued' as const } : m));
      return ok(server.find((m) => m.id === id)!);
    }),
  };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('queue helpers', () => {
  it('wentOut: what was waiting, is gone, and was not removed here', () => {
    const before = [message('a'), message('b', { state: 'Held' }), message('c'), message('d', { state: 'Delivering' })];
    expect(wentOut(before, [message('c')], new Set(['a']))).toEqual(['d']);
  });

  it('waitsToGo: anything that is not held', () => {
    expect(waitsToGo([message('a', { state: 'Held' })])).toBe(false);
    expect(waitsToGo([message('a', { state: 'Held' }), message('b')])).toBe(true);
  });

  it('queuedLine says where a message stands', () => {
    expect(queuedLine(message('a'))).toBe('waiting');
    expect(queuedLine(message('a', { sendNow: true }))).toBe('next');
    expect(queuedLine(message('a', { state: 'Delivering' }))).toBe('sending');
    expect(queuedLine(message('a', { state: 'Held', failure: 'The turn was stopped, so this was not sent.', failureCode: 'AI_QUEUED_TURN_STOPPED' }))).toBe('stopped');
    // An older server held it for the same cause without a reason.
    expect(queuedLine(message('a', { state: 'Held' }))).toBe('stopped');
    expect(queuedLine(message('a', { state: 'Held', failureCode: 'AI_MODEL_NOT_ALLOWED' }))).toBe('failed');
    expect(queuedLine(message('a', { state: 'Held', failure: 'nope' }))).toBe('failed');
    expect(queuedLine(message('a', { state: 'HandedOver' }))).toBe('handed');
  });

  it('only a waiting or held message can still be changed; one with the agent goes on by itself', () => {
    expect(isChangeable(message('a'))).toBe(true);
    expect(isChangeable(message('a', { state: 'Held' }))).toBe(true);
    for (const state of ['Delivering', 'HandedOver', 'Delivered'] as const) {
      expect(isChangeable(message('a', { state }))).toBe(false);
    }
    expect(waitsToGo([message('a', { state: 'Delivering' })])).toBe(true);
    expect(waitsToGo([message('a', { state: 'HandedOver' })])).toBe(true);
  });

  it('a message the agent took in waits for nothing, and its row clearing at the turn end is not a turn that went out', () => {
    expect(waitsToGo([message('a', { state: 'Delivered' })])).toBe(false);
    expect(wentOut([message('a', { state: 'Delivered' })], [], new Set())).toEqual([]);
    // One handed over and gone without being seen taken in may have gone out as a turn of its own.
    expect(wentOut([message('a', { state: 'HandedOver' })], [], new Set())).toEqual(['a']);
  });

  it('queuedText trims, and has nothing for blank words', () => {
    expect(queuedText('  hi \n')).toBe('hi');
    expect(queuedText('   ')).toBeNull();
  });
});

describe('createMessageQueue', () => {
  it('reads a conversation when it opens, and only a turn with a server run can be queued behind', async () => {
    const api = fakeApi([message('a', { state: 'Held' })]);
    const queue = createMessageQueue({ api: api as never });

    queue.follow('t-1', false, false);
    await vi.waitFor(() => expect(queue.items.value.map((m) => m.id)).toEqual(['a']));
    expect(api.getQueue).toHaveBeenCalledWith('t-1');
    expect(queue.available.value).toBe(false);

    queue.follow('t-1', true, false);
    expect(queue.available.value).toBe(false);
    queue.follow('t-1', true, true);
    expect(queue.available.value).toBe(true);
    expect(queue.running.value).toBe(true);
  });

  it('queues, edits, removes, and lists the one to send now first', async () => {
    const api = fakeApi();
    const queue = createMessageQueue({ api: api as never });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalled());

    await queue.add({ text: 'one' });
    await queue.add({ text: 'two' });
    expect(api.queueMessage).toHaveBeenCalledWith('t-1', { text: 'one' });
    expect(queue.items.value.map((m) => m.text)).toEqual(['one', 'two']);

    expect(await queue.edit('q-1', '  better  ')).toBe(true);
    expect(api.updateQueuedMessage).toHaveBeenCalledWith('t-1', 'q-1', 'better');
    expect(await queue.edit('q-1', '   ')).toBe(false);

    expect(await queue.sendNow('q-2')).toBe(true);
    expect(queue.items.value.map((m) => m.id)).toEqual(['q-2', 'q-1']);

    expect(await queue.remove('q-1')).toBe(true);
    expect(queue.items.value.map((m) => m.id)).toEqual(['q-2']);
  });

  it('when the turn ends and the messages went out, says the conversation has a turn it did not start', async () => {
    const api = fakeApi([message('a', { afterRunId: 'r-1' }), message('b', { afterRunId: 'r-1' })]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(2));

    // The turn ends here; the server has not got to them yet.
    queue.follow('t-1', false, false);
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalledTimes(2));
    expect(onDelivered).not.toHaveBeenCalled();

    // Read again while they wait: now they went out.
    api.set([]);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS);
    expect(queue.items.value).toEqual([]);
    expect(onDelivered).toHaveBeenCalledTimes(1);

    // Nothing waits any more: no more reading.
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 3);
    expect(api.getQueue).toHaveBeenCalledTimes(3);
  });

  it('a message the agent takes in mid-turn leaves the list, and its row clearing at the turn end follows nothing', async () => {
    const api = fakeApi([message('a', { state: 'HandedOver', handedToRunId: 'r-1' })]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    // Taken in: a user message of the conversation now; the server keeps the row until the turn ends.
    api.set([message('a', { state: 'Delivered', handedToRunId: 'r-1', threadMessageId: 'u-1' })]);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS);
    expect(queue.items.value).toEqual([]);
    expect(onDelivered).not.toHaveBeenCalled();
    // Nothing waits to go: no reading while the turn runs on.
    const reads = api.getQueue.mock.calls.length;
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 3);
    expect(api.getQueue).toHaveBeenCalledTimes(reads);

    // The turn ends; the server clears the row: no turn went out, nothing to follow.
    api.set([]);
    queue.follow('t-1', false, false);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 3);
    expect(api.getQueue).toHaveBeenCalledTimes(reads + 1);
    expect(queue.items.value).toEqual([]);
    expect(onDelivered).not.toHaveBeenCalled();
  });

  it('beside a message the agent took in, one waiting for the turn end still goes out and is followed', async () => {
    const api = fakeApi([
      message('a', { state: 'Delivered', handedToRunId: 'r-1', threadMessageId: 'u-1' }),
      message('b', { afterRunId: 'r-1' }),
    ]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value.map((m) => m.id)).toEqual(['b']));

    queue.follow('t-1', false, false);
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalledTimes(2));
    expect(onDelivered).not.toHaveBeenCalled();

    // The server cleared the taken-in row and sent the waiting one as the next turn.
    api.set([]);
    await vi.advanceTimersByTimeAsync(QUEUE_AFTER_TURN_MS);
    expect(queue.items.value).toEqual([]);
    expect(onDelivered).toHaveBeenCalledTimes(1);
  });

  it('a message seen gone while this page still shows the last turn is followed once that turn ends here', async () => {
    const api = fakeApi([message('a', { afterRunId: 'r-1' })]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    // The server ended the run and sent the message as the next turn before this page's stream ended.
    api.set([]);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS);
    expect(queue.items.value).toEqual([]);
    expect(onDelivered).not.toHaveBeenCalled();

    // The stream ends: nothing is gone any more (the list is empty), but the turn is owed.
    queue.follow('t-1', false, false);
    expect(onDelivered).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 2);
    expect(onDelivered).toHaveBeenCalledTimes(1);
  });

  it('a page opened while the turn runs follows the turn the server started the same way', async () => {
    const api = fakeApi([message('a', { afterRunId: 'r-1' }), message('b', { state: 'Held' })]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    // Opened (and reattached to the run) mid-turn.
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(2));

    api.set([message('b', { state: 'Held' })]);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS);
    expect(onDelivered).not.toHaveBeenCalled();

    queue.follow('t-1', false, false);
    expect(onDelivered).toHaveBeenCalledTimes(1);
  });

  it('a debt for a conversation the page left is dropped', async () => {
    const api = fakeApi([message('a', { afterRunId: 'r-1' })]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));
    api.set([]);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS);

    queue.follow('t-2', false, false);
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalledWith('t-2'));
    expect(onDelivered).not.toHaveBeenCalled();
  });

  it('right after a turn is stopped, the message it held shows why within moments, as after a reload', async () => {
    const api = fakeApi([message('a', { afterRunId: 'r-1' })]);
    const queue = createMessageQueue({ api: api as never });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    // Stopped here; the server has not held it yet when the page reads at the turn's end.
    queue.follow('t-1', false, false);
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalledTimes(2));
    expect(queuedLine(queue.items.value[0])).toBe('waiting');

    api.set([message('a', { state: 'Held', failure: 'The turn was stopped, so this was not sent.', failureCode: 'AI_QUEUED_TURN_STOPPED' })]);
    await vi.advanceTimersByTimeAsync(QUEUE_AFTER_TURN_MS);
    expect(queuedLine(queue.items.value[0])).toBe('stopped');

    // Held only: no more reading.
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 3);
    expect(api.getQueue).toHaveBeenCalledTimes(3);
  });

  it('does not read again for held messages only, and a removed one is not a delivery', async () => {
    const api = fakeApi([message('a', { state: 'Held' }), message('b')]);
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', false, false);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(2));

    await queue.remove('b');
    api.set([message('a', { state: 'Held' })]);
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 2);
    expect(onDelivered).not.toHaveBeenCalled();
    expect(api.getQueue).toHaveBeenCalledTimes(1);
  });

  it('a message queued when nothing ran went at once: it is not listed and the conversation is followed', async () => {
    const api = fakeApi();
    api.queueMessage.mockResolvedValueOnce(ok(message('now', { state: 'Delivering', afterRunId: null })));
    const onDelivered = vi.fn();
    const queue = createMessageQueue({ api: api as never, onDelivered });
    queue.follow('t-1', false, false);

    const queued = await queue.add({ text: 'hello' });

    expect(queued?.state).toBe('Delivering');
    expect(queue.items.value).toEqual([]);
    expect(onDelivered).toHaveBeenCalledTimes(1);
  });

  it('reports a refusal with its operation, and reads the queue again after a refused change', async () => {
    const api = fakeApi([message('a')]);
    const onError = vi.fn();
    const queue = createMessageQueue({ api: api as never, onError });
    queue.follow('t-1', true, true);
    await vi.waitFor(() => expect(queue.items.value).toHaveLength(1));

    api.queueMessage.mockResolvedValueOnce(refused('AI_QUEUE_FULL') as never);
    expect(await queue.add({ text: 'x' })).toBeNull();
    expect(onError).toHaveBeenLastCalledWith('queue', expect.objectContaining({ errorCode: 'AI_QUEUE_FULL' }));

    api.removeQueuedMessage.mockResolvedValueOnce(refused('AI_QUEUED_MESSAGE_DELIVERING') as never);
    expect(await queue.remove('a')).toBe(false);
    expect(onError).toHaveBeenLastCalledWith('remove-queued', expect.anything());
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalledTimes(2));

    api.sendQueuedMessageNow.mockRejectedValueOnce(new Error('offline'));
    expect(await queue.sendNow('a')).toBe(false);
    expect(onError).toHaveBeenLastCalledWith('send-queued-now', expect.any(Error));
    expect(queue.busy.value.size).toBe(0);
  });

  it('switching conversations empties the list and drops a slow read of the old one', async () => {
    const api = fakeApi([message('old')]);
    let release: (() => void) | undefined;
    api.getQueue.mockImplementationOnce(async () => {
      await new Promise<void>((r) => (release = r));
      return ok([message('old')]);
    });
    const queue = createMessageQueue({ api: api as never });
    queue.follow('t-1', false, false);
    api.set([message('new', { threadId: 't-2' })]);
    queue.follow('t-2', false, false);
    await vi.waitFor(() => expect(queue.items.value.map((m) => m.id)).toEqual(['new']));
    release?.();
    await vi.advanceTimersByTimeAsync(0);
    expect(queue.items.value.map((m) => m.id)).toEqual(['new']);

    queue.follow(null, false, false);
    expect(queue.items.value).toEqual([]);
    expect(await queue.add({ text: 'x' })).toBeNull();
  });

  it('stops reading when disposed', async () => {
    const api = fakeApi([message('a')]);
    const queue = createMessageQueue({ api: api as never });
    queue.follow('t-1', false, false);
    await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalledTimes(1));
    queue.dispose();
    await vi.advanceTimersByTimeAsync(QUEUE_POLL_MS * 3);
    expect(api.getQueue).toHaveBeenCalledTimes(1);
  });
});
