import { describe, it, expect, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import { useChatApi } from '../../src/services/ai/api';
import { AiChatErrorCodes } from '../../src/services/ai/metadata';

/** The calls a thread's message queue makes: list, queue, edit, remove, send now. */
function mockClient() {
  const calls: Array<{ method: string; url: string; body?: unknown }> = [];
  const ok = <T>(data: T) => Promise.resolve({ succeeded: true, data, code: 200 });
  const record = (method: string) => vi.fn((url: string, body?: unknown) => {
    calls.push(body === undefined ? { method, url } : { method, url, body });
    return ok(null);
  });
  const client = { get: record('GET'), post: record('POST'), put: record('PUT'), delete: record('DELETE'), calls };
  return client as unknown as HttpClient & { calls: typeof calls };
}

describe('useChatApi queue endpoints', () => {
  it('lists a thread\'s queue', async () => {
    const client = mockClient();
    await useChatApi(client).getQueue('t 1');
    expect(client.calls).toEqual([{ method: 'GET', url: '/chat/threads/t%201/queue' }]);
  });

  it('queues what the composer would have sent', async () => {
    const client = mockClient();
    await useChatApi(client).queueMessage('t-1', { text: 'and run the tests', model: 'm-1', metadata: { device: 'd-7' } });
    expect(client.calls).toEqual([
      { method: 'POST', url: '/chat/threads/t-1/queue', body: { text: 'and run the tests', model: 'm-1', metadata: { device: 'd-7' } } },
    ]);
  });

  it('edits, removes and sends one now by its id', async () => {
    const client = mockClient();
    const api = useChatApi(client);
    await api.updateQueuedMessage('t-1', 'q/1', 'better words');
    await api.removeQueuedMessage('t-1', 'q-2');
    await api.sendQueuedMessageNow('t-1', 'q-3');
    expect(client.calls).toEqual([
      { method: 'PUT', url: '/chat/threads/t-1/queue/q%2F1', body: { text: 'better words' } },
      { method: 'DELETE', url: '/chat/threads/t-1/queue/q-2' },
      { method: 'POST', url: '/chat/threads/t-1/queue/q-3/send-now' },
    ]);
  });

  it('names the queue\'s error codes as the backend does', () => {
    expect(AiChatErrorCodes.QueueFull).toBe('AI_QUEUE_FULL');
    expect(AiChatErrorCodes.QueueNotAllowed).toBe('AI_QUEUE_NOT_ALLOWED');
    expect(AiChatErrorCodes.QueuedMessageDelivering).toBe('AI_QUEUED_MESSAGE_DELIVERING');
    expect(AiChatErrorCodes.QueuedMessageNotFound).toBe('AI_QUEUED_MESSAGE_NOT_FOUND');
    expect(AiChatErrorCodes.QueuedMessageInterrupted).toBe('AI_QUEUED_MESSAGE_INTERRUPTED');
    expect(AiChatErrorCodes.QueuedTurnStopped).toBe('AI_QUEUED_TURN_STOPPED');
  });
});
