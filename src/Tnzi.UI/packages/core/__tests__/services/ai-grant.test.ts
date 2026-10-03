import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import { useAdminAgentGrantApi } from '../../src/services/ai/grant';
import { useAdminAgentGrantApi as fromBarrel } from '../../src/services/ai';

// Routes mirror Tnzi.AI DefaultAgentGrantAdminController (`admin/agents/grants`).

function createMockClient() {
  const calls: Array<{ method: string; url: string; data?: unknown; options?: unknown }> = [];
  const ok = () => Promise.resolve({ succeeded: true, data: null, code: 200 });
  const client = {
    get: vi.fn((url: string, options?: unknown) => {
      calls.push({ method: 'GET', url, options });
      return ok();
    }),
    post: vi.fn((url: string, data?: unknown, options?: unknown) => {
      calls.push({ method: 'POST', url, data, options });
      return ok();
    }),
    put: vi.fn(),
    delete: vi.fn((url: string, options?: unknown) => {
      calls.push({ method: 'DELETE', url, options });
      return ok();
    }),
    calls,
  };
  return client as unknown as HttpClient & { calls: typeof calls };
}

describe('useAdminAgentGrantApi', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  it('is exported from the ai barrel', () => {
    expect(fromBarrel).toBe(useAdminAgentGrantApi);
  });

  it('lists every grant of an agent', async () => {
    await useAdminAgentGrantApi(client).listForAgent('a-1');
    expect(client.calls[0]).toMatchObject({ method: 'GET', url: '/admin/agents/grants/agent/a-1' });
  });

  it('reverse lookups carry the resource key as the query parameter the backend binds', async () => {
    const api = useAdminAgentGrantApi(client);
    await api.agentsUsingTool('web');
    await api.agentsUsingSkill('write-blog-post');
    await api.agentsUsingKnowledge('kb-1');

    expect(client.calls[0]).toMatchObject({ url: '/admin/agents/grants/reverse/tool', options: { params: { key: 'web' } } });
    expect(client.calls[1]).toMatchObject({
      url: '/admin/agents/grants/reverse/skill',
      options: { params: { slug: 'write-blog-post' } },
    });
    expect(client.calls[2]).toMatchObject({
      url: '/admin/agents/grants/reverse/knowledge',
      options: { params: { knowledgeBaseId: 'kb-1' } },
    });
  });

  it('toggles a grant with the SetGrantEnabledDto body', async () => {
    await useAdminAgentGrantApi(client).setEnabled('skill', 'g-1', false);
    expect(client.calls[0]).toEqual({
      method: 'POST',
      url: '/admin/agents/grants/skill/g-1/enabled',
      data: { enabled: false },
      options: undefined,
    });
  });

  it('sets a priority with the SetGrantPriorityDto body', async () => {
    await useAdminAgentGrantApi(client).setPriority('knowledge', 'g-2', 7);
    expect(client.calls[0]).toMatchObject({
      method: 'POST',
      url: '/admin/agents/grants/knowledge/g-2/priority',
      data: { priority: 7 },
    });
  });

  it('deletes a grant by category and id', async () => {
    await useAdminAgentGrantApi(client).remove('tool', 'g-3');
    expect(client.calls[0]).toMatchObject({ method: 'DELETE', url: '/admin/agents/grants/tool/g-3' });
  });
});
