import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { HttpClient } from '../../http/http';
import { useAdminTaskApi, AgentTaskStatus } from '../../services/ai/task';

// ---------------------------------------------------------------------------
// Mock HttpClient
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

describe('useAdminTaskApi', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  it('should create API with correct methods', () => {
    const api = useAdminTaskApi(client);
    expect(api.getByRunId).toBeDefined();
    expect(api.getByStatus).toBeDefined();
  });

  it('should call getByRunId via GET with runId query param', async () => {
    const api = useAdminTaskApi(client);
    await api.getByRunId('run-1');
    expect(client.get).toHaveBeenCalledWith('/admin/ai/tasks', { params: { runId: 'run-1' } });
  });

  it('should call getByStatus via GET /by-status with status query param', async () => {
    const api = useAdminTaskApi(client);
    await api.getByStatus(AgentTaskStatus.InProgress);
    expect(client.get).toHaveBeenCalledWith('/admin/ai/tasks/by-status', {
      params: { status: AgentTaskStatus.InProgress },
    });
  });
});

describe('AgentTaskStatus enum', () => {
  it('mirrors backend values', () => {
    expect(AgentTaskStatus.Pending).toBe(0);
    expect(AgentTaskStatus.InProgress).toBe(1);
    expect(AgentTaskStatus.Completed).toBe(2);
    expect(AgentTaskStatus.Skipped).toBe(3);
  });
});
