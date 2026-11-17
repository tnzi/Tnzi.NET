import { describe, it, expect, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import { useThreadApi } from '../../src/services/ai/api';

/** The thread endpoints behind the running / unread indicators: the attention summary and marking a thread read. */
function mockClient() {
  const calls: Array<{ url: string; data?: unknown }> = [];
  const client = {
    post: vi.fn((url: string, data?: unknown) => {
      calls.push({ url, data });
      return Promise.resolve({ succeeded: true, data: null, code: 200 });
    }),
    calls,
  };
  return client as unknown as HttpClient & { calls: typeof calls };
}

describe('useThreadApi attention', () => {
  it('posts the agent filter to the attention endpoint', async () => {
    const client = mockClient();
    await useThreadApi(client).getAttention({ agentIds: ['a-1'] });
    expect(client.calls).toEqual([{ url: '/threads/attention', data: { agentIds: ['a-1'] } }]);
  });

  it('asks for every thread without a filter', async () => {
    const client = mockClient();
    await useThreadApi(client).getAttention();
    expect(client.calls).toEqual([{ url: '/threads/attention', data: {} }]);
  });

  it('marks a thread read', async () => {
    const client = mockClient();
    await useThreadApi(client).markRead('t-1');
    expect(client.calls).toEqual([{ url: '/threads/t-1/read', data: undefined }]);
  });
});
