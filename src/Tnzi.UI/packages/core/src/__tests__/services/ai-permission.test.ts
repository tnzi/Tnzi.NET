import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { HttpClient } from '../../http/http';
import {
  usePermissionAdminApi,
  PermissionBehavior,
  ToolPermissionScope,
} from '../../services/ai/permission';

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

describe('usePermissionAdminApi', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  it('should create API with correct methods', () => {
    const api = usePermissionAdminApi(client);
    expect(api.getRules).toBeDefined();
    expect(api.evaluate).toBeDefined();
    expect(api.getPersistedRules).toBeDefined();
    expect(api.createPersistedRule).toBeDefined();
    expect(api.deletePersistedRule).toBeDefined();
  });

  it('should call getRules via GET /rules', async () => {
    const api = usePermissionAdminApi(client);
    await api.getRules();
    expect(client.get).toHaveBeenCalledWith('/admin/permissions/rules');
  });

  it('should call evaluate via POST /rules/evaluate with body', async () => {
    const api = usePermissionAdminApi(client);
    const body = { toolName: 'shell', toolGroup: 'bash', isDestructive: true };
    await api.evaluate(body);
    expect(client.post).toHaveBeenCalledWith('/admin/permissions/rules/evaluate', body);
  });

  it('should call getPersistedRules via GET /persisted-rules', async () => {
    const api = usePermissionAdminApi(client);
    await api.getPersistedRules();
    expect(client.get).toHaveBeenCalledWith('/admin/permissions/persisted-rules');
  });

  it('should call createPersistedRule via POST /persisted-rules with body', async () => {
    const api = usePermissionAdminApi(client);
    const body = {
      toolPattern: '*',
      behavior: PermissionBehavior.Deny,
      scope: ToolPermissionScope.User,
      priority: 10,
    };
    await api.createPersistedRule(body);
    expect(client.post).toHaveBeenCalledWith('/admin/permissions/persisted-rules', body);
  });

  it('should call deletePersistedRule via DELETE /persisted-rules/{id}', async () => {
    const api = usePermissionAdminApi(client);
    await api.deletePersistedRule('rule-1');
    expect(client.delete).toHaveBeenCalledWith('/admin/permissions/persisted-rules/rule-1');
  });
});

describe('Permission enums', () => {
  it('PermissionBehavior mirrors backend values', () => {
    expect(PermissionBehavior.Allow).toBe(0);
    expect(PermissionBehavior.Ask).toBe(1);
    expect(PermissionBehavior.Deny).toBe(2);
  });

  it('ToolPermissionScope mirrors backend values', () => {
    expect(ToolPermissionScope.System).toBe(0);
    expect(ToolPermissionScope.Project).toBe(1);
    expect(ToolPermissionScope.User).toBe(2);
    expect(ToolPermissionScope.Session).toBe(3);
  });
});
