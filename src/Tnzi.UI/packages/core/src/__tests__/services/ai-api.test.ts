import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { HttpClient } from '../../http/http';
import {
  useAdminPersonaApi,
  useAdminSkillCategoryApi,
  useAdminMcpToolAnalyticsApi,
  useArtifactApi,
  useUserProfileApi,
} from '../../services/ai/api';

// ---------------------------------------------------------------------------
// Mock HttpClient — intercept all HTTP methods
// ---------------------------------------------------------------------------

function createMockClient() {
  const calls: Array<{ method: string; url: string; data?: unknown; options?: unknown }> = [];

  const mockResult = <T>(data: T) => Promise.resolve({ succeeded: true, data, code: 200 });

  const client = {
    get: vi.fn((...args: unknown[]) => {
      calls.push({ method: 'GET', url: args[0] as string, options: args[1] });
      return mockResult(null);
    }),
    post: vi.fn((...args: unknown[]) => {
      calls.push({ method: 'POST', url: args[0] as string, data: args[1], options: args[2] });
      return mockResult(null);
    }),
    put: vi.fn((...args: unknown[]) => {
      calls.push({ method: 'PUT', url: args[0] as string, data: args[1], options: args[2] });
      return mockResult(null);
    }),
    patch: vi.fn((...args: unknown[]) => {
      calls.push({ method: 'PATCH', url: args[0] as string, data: args[1], options: args[2] });
      return mockResult(null);
    }),
    delete: vi.fn((...args: unknown[]) => {
      calls.push({ method: 'DELETE', url: args[0] as string, options: args[1] });
      return mockResult(null);
    }),
    resolveUrl: vi.fn((path: string) => `http://localhost:5000/api${path}`),
    calls,
  };

  return client as unknown as HttpClient & { calls: typeof calls };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AI API Wrappers', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  // ==========================================
  // useAdminPersonaApi
  // ==========================================

  describe('useAdminPersonaApi', () => {
    it('should call POST /admin/ai/personas/query for getList', async () => {
      const api = useAdminPersonaApi(client);
      await api.getList({ pageIndex: 1, pageSize: 10 });
      expect(client.post).toHaveBeenCalledWith(
        '/admin/ai/personas/query',
        { pageIndex: 1, pageSize: 10 },
      );
    });

    it('should call GET /admin/ai/personas/{id} for getById', async () => {
      const api = useAdminPersonaApi(client);
      await api.getById('abc-123');
      expect(client.get).toHaveBeenCalledWith('/admin/ai/personas/abc-123');
    });

    it('should call GET /admin/ai/personas/by-slug/{slug} for getBySlug', async () => {
      const api = useAdminPersonaApi(client);
      await api.getBySlug('friendly-helper');
      expect(client.get).toHaveBeenCalledWith('/admin/ai/personas/by-slug/friendly-helper');
    });

    it('should call POST /admin/ai/personas for create', async () => {
      const api = useAdminPersonaApi(client);
      const input = { name: 'Test', slug: 'test', content: 'Hello' };
      await api.create(input);
      expect(client.post).toHaveBeenCalledWith('/admin/ai/personas', input);
    });

    it('should call PUT /admin/ai/personas/{id} for update', async () => {
      const api = useAdminPersonaApi(client);
      const input = { name: 'Updated' };
      await api.update('abc-123', input);
      expect(client.put).toHaveBeenCalledWith('/admin/ai/personas/abc-123', input);
    });

    it('should call DELETE /admin/ai/personas/{id} for delete', async () => {
      const api = useAdminPersonaApi(client);
      await api.delete('abc-123');
      expect(client.delete).toHaveBeenCalledWith('/admin/ai/personas/abc-123');
    });
  });

  // ==========================================
  // useAdminSkillCategoryApi
  // ==========================================

  describe('useAdminSkillCategoryApi', () => {
    it('should call GET /admin/ai/skill-categories/tree for getTree', async () => {
      const api = useAdminSkillCategoryApi(client);
      await api.getTree();
      expect(client.get).toHaveBeenCalledWith('/admin/ai/skill-categories/tree');
    });

    it('should call POST /admin/ai/skill-categories for create', async () => {
      const api = useAdminSkillCategoryApi(client);
      const input = { name: 'Code Tools' };
      await api.create(input);
      expect(client.post).toHaveBeenCalledWith('/admin/ai/skill-categories', input);
    });

    it('should call PUT /admin/ai/skill-categories/{id} for update', async () => {
      const api = useAdminSkillCategoryApi(client);
      const input = { name: 'Updated Name' };
      await api.update('cat-1', input);
      expect(client.put).toHaveBeenCalledWith('/admin/ai/skill-categories/cat-1', input);
    });

    it('should call DELETE /admin/ai/skill-categories/{id} for delete', async () => {
      const api = useAdminSkillCategoryApi(client);
      await api.delete('cat-1');
      expect(client.delete).toHaveBeenCalledWith('/admin/ai/skill-categories/cat-1');
    });

    it('should call GET /admin/ai/skill-categories/{id}/skills for getSkillsByCategory', async () => {
      const api = useAdminSkillCategoryApi(client);
      await api.getSkillsByCategory('cat-1');
      expect(client.get).toHaveBeenCalledWith('/admin/ai/skill-categories/cat-1/skills');
    });
  });

  // ==========================================
  // useAdminMcpToolAnalyticsApi
  // ==========================================

  describe('useAdminMcpToolAnalyticsApi', () => {
    it('should call GET stats/{toolName} for getToolStats', async () => {
      const api = useAdminMcpToolAnalyticsApi(client);
      await api.getToolStats('my-tool');
      expect(client.get).toHaveBeenCalledWith(
        '/admin/ai/mcp/tool-analytics/stats/my-tool',
        { params: { from: undefined, to: undefined } },
      );
    });

    it('should pass from/to params to getToolStats', async () => {
      const api = useAdminMcpToolAnalyticsApi(client);
      await api.getToolStats('my-tool', '2026-01-01', '2026-01-31');
      expect(client.get).toHaveBeenCalledWith(
        '/admin/ai/mcp/tool-analytics/stats/my-tool',
        { params: { from: '2026-01-01', to: '2026-01-31' } },
      );
    });

    it('should call GET most-used for getMostUsedTools', async () => {
      const api = useAdminMcpToolAnalyticsApi(client);
      await api.getMostUsedTools(5);
      expect(client.get).toHaveBeenCalledWith(
        '/admin/ai/mcp/tool-analytics/most-used',
        { params: { top: 5, from: undefined, to: undefined } },
      );
    });

    it('should use default top=10 for getMostUsedTools', async () => {
      const api = useAdminMcpToolAnalyticsApi(client);
      await api.getMostUsedTools();
      expect(client.get).toHaveBeenCalledWith(
        '/admin/ai/mcp/tool-analytics/most-used',
        { params: { top: 10, from: undefined, to: undefined } },
      );
    });

    it('should call GET errors/{toolName} for getToolErrors', async () => {
      const api = useAdminMcpToolAnalyticsApi(client);
      await api.getToolErrors('my-tool', 5);
      expect(client.get).toHaveBeenCalledWith(
        '/admin/ai/mcp/tool-analytics/errors/my-tool',
        { params: { top: 5 } },
      );
    });

    it('should call DELETE cleanup for cleanup', async () => {
      const api = useAdminMcpToolAnalyticsApi(client);
      await api.cleanup(30);
      expect(client.delete).toHaveBeenCalledWith(
        '/admin/ai/mcp/tool-analytics/cleanup',
        { params: { retentionDays: 30 } },
      );
    });
  });

  // ==========================================
  // useArtifactApi
  // ==========================================

  describe('useArtifactApi', () => {
    it('should call GET /artifacts without params for getList', async () => {
      const api = useArtifactApi(client);
      await api.getList();
      expect(client.get).toHaveBeenCalledWith('/artifacts', { params: undefined });
    });

    it('should call GET /artifacts with params for getList', async () => {
      const api = useArtifactApi(client);
      await api.getList({ threadId: 'thread-1' });
      expect(client.get).toHaveBeenCalledWith(
        '/artifacts',
        { params: { threadId: 'thread-1' } },
      );
    });

    it('should call GET /artifacts with threadId for getByThread', async () => {
      const api = useArtifactApi(client);
      await api.getByThread('thread-1');
      expect(client.get).toHaveBeenCalledWith(
        '/artifacts',
        { params: { threadId: 'thread-1' } },
      );
    });

    it('should call GET /artifacts/by-run with runId for getByRun', async () => {
      const api = useArtifactApi(client);
      await api.getByRun('run-1');
      expect(client.get).toHaveBeenCalledWith(
        '/artifacts/by-run',
        { params: { runId: 'run-1' } },
      );
    });

    it('should call GET /artifacts/{id} for getById', async () => {
      const api = useArtifactApi(client);
      await api.getById('artifact-1');
      expect(client.get).toHaveBeenCalledWith('/artifacts/artifact-1');
    });
  });

  // ==========================================
  // useUserProfileApi
  // ==========================================

  describe('useUserProfileApi', () => {
    it('should call GET /user-profile for get', async () => {
      const api = useUserProfileApi(client);
      await api.get();
      expect(client.get).toHaveBeenCalledWith('/user-profile');
    });

    it('should call PUT /user-profile for update', async () => {
      const api = useUserProfileApi(client);
      const input = { displayName: 'Alice', role: 'developer' };
      await api.update(input);
      expect(client.put).toHaveBeenCalledWith('/user-profile', input);
    });
  });
});
