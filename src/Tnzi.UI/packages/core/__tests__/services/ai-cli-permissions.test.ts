import { describe, it, expect, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import { useChatApi } from '../../src/services/ai/api';
import { useAdminCliRunApi, useCliRunApi, CliPermissionOptionKind } from '../../src/services/ai/cli';

/**
 * Answering an external agent's permission request: the chat endpoint, the user endpoint for your own
 * runs and the admin endpoint that answers on someone's behalf all take the run id, the request id and
 * one of the offered options.
 */
function mockClient() {
  const calls: Array<{ url: string; body: unknown }> = [];
  const client = {
    post: vi.fn((url: string, body?: unknown) => {
      calls.push({ url, body });
      return Promise.resolve({ succeeded: true, code: 200 });
    }),
    calls,
  };
  return client as unknown as HttpClient & { calls: typeof calls };
}

const answer = { optionId: CliPermissionOptionKind.RejectOnce, message: 'Keep the build directory.' };

describe('permission request answers', () => {
  it('chat: posts the answer to the run', async () => {
    const client = mockClient();
    await useChatApi(client).answerRunPermission('r-1', 'p-1', answer);
    expect(client.calls).toEqual([{ url: '/chat/runs/r-1/permissions/p-1', body: answer }]);
  });

  it('user run endpoint', async () => {
    const client = mockClient();
    await useCliRunApi(client).answerPermission('r-1', 'p-1', answer);
    expect(client.calls).toEqual([{ url: '/ai/cli-runs/r-1/permissions/p-1', body: answer }]);
  });

  it('admin run endpoint', async () => {
    const client = mockClient();
    await useAdminCliRunApi(client).answerPermission('r-1', 'p-1', answer);
    expect(client.calls).toEqual([{ url: '/admin/ai/cli-runs/r-1/permissions/p-1', body: answer }]);
  });

  it('encodes the ids it puts into the path', async () => {
    const client = mockClient();
    await useChatApi(client).answerRunPermission('r/1', 'p?1', answer);
    expect(client.calls[0].url).toBe('/chat/runs/r%2F1/permissions/p%3F1');
  });
});
