import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import {
  useAdminSubAgentTypeApi,
  ToolApprovalMode,
} from '../../src/services/ai/sub-agent-type';

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

describe('useAdminSubAgentTypeApi', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  it('should create API with correct methods', () => {
    const api = useAdminSubAgentTypeApi(client);
    expect(api.getAll).toBeDefined();
    expect(api.create).toBeDefined();
    expect(api.update).toBeDefined();
    expect(api.delete).toBeDefined();
  });

  it('should call getAll via GET base', async () => {
    const api = useAdminSubAgentTypeApi(client);
    await api.getAll();
    expect(client.get).toHaveBeenCalledWith('/admin/ai/sub-agent-types');
  });

  it('should call create via POST base with body', async () => {
    const api = useAdminSubAgentTypeApi(client);
    const body = {
      name: 'researcher',
      description: 'Research sub-agent',
      maxTurns: 30,
      defaultApprovalMode: ToolApprovalMode.AlwaysRequire,
    };
    await api.create(body);
    expect(client.post).toHaveBeenCalledWith('/admin/ai/sub-agent-types', body);
  });

  it('should call update via PUT /{id} with body', async () => {
    const api = useAdminSubAgentTypeApi(client);
    const body = { name: 'researcher', description: 'Updated' };
    await api.update('type-1', body);
    expect(client.put).toHaveBeenCalledWith('/admin/ai/sub-agent-types/type-1', body);
  });

  it('should call delete via DELETE /{id}', async () => {
    const api = useAdminSubAgentTypeApi(client);
    await api.delete('type-1');
    expect(client.delete).toHaveBeenCalledWith('/admin/ai/sub-agent-types/type-1');
  });
});

describe('ToolApprovalMode enum', () => {
  it('mirrors backend values', () => {
    expect(ToolApprovalMode.NeverRequire).toBe(0);
    expect(ToolApprovalMode.AlwaysRequire).toBe(1);
    expect(ToolApprovalMode.Specific).toBe(2);
  });
});
