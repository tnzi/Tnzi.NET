import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { HttpClient } from '../../http/http';
import { useRagApi, useAdminKnowledgeBaseApi } from '../../services/ai/rag';

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
    upload: vi.fn((...args: unknown[]) => {
      calls.push({ method: 'UPLOAD', url: args[0] as string, data: args[1], options: args[2] });
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

describe('useRagApi', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  it('should create API with correct methods', () => {
    const api = useRagApi(client);
    expect(api.query).toBeDefined();
    expect(api.chat).toBeDefined();
    expect(api.getStreamUrl).toBeDefined();
  });

  it('should call query with correct params', async () => {
    const api = useRagApi(client);
    await api.query({ query: 'test', knowledgeBaseIds: ['kb1'] });
    expect(client.post).toHaveBeenCalledWith('/rag/query', {
      query: 'test',
      knowledgeBaseIds: ['kb1'],
    });
  });

  it('should call query with optional params', async () => {
    const api = useRagApi(client);
    await api.query({ query: 'test', knowledgeBaseIds: ['kb1', 'kb2'], topK: 5, threshold: 0.8 });
    expect(client.post).toHaveBeenCalledWith('/rag/query', {
      query: 'test',
      knowledgeBaseIds: ['kb1', 'kb2'],
      topK: 5,
      threshold: 0.8,
    });
  });

  it('should call chat with correct params', async () => {
    const api = useRagApi(client);
    await api.chat({ message: 'hello', knowledgeBaseIds: ['kb1'], threadId: 't1' });
    expect(client.post).toHaveBeenCalledWith('/rag/chat', {
      message: 'hello',
      knowledgeBaseIds: ['kb1'],
      threadId: 't1',
    });
  });

  it('should call chat with optional topK', async () => {
    const api = useRagApi(client);
    await api.chat({ message: 'hello', knowledgeBaseIds: ['kb1'], topK: 3 });
    expect(client.post).toHaveBeenCalledWith('/rag/chat', {
      message: 'hello',
      knowledgeBaseIds: ['kb1'],
      topK: 3,
    });
  });

  it('should build stream URL', () => {
    const api = useRagApi(client);
    const url = api.getStreamUrl();
    expect(url).toBe('/rag/stream');
  });
});

describe('useAdminKnowledgeBaseApi', () => {
  let client: ReturnType<typeof createMockClient>;

  beforeEach(() => {
    client = createMockClient();
  });

  it('should create API with correct methods', () => {
    const api = useAdminKnowledgeBaseApi(client);
    expect(api.getList).toBeDefined();
    expect(api.getById).toBeDefined();
    expect(api.create).toBeDefined();
    expect(api.update).toBeDefined();
    expect(api.delete).toBeDefined();
    expect(api.uploadDocument).toBeDefined();
    expect(api.getDocuments).toBeDefined();
    expect(api.deleteDocument).toBeDefined();
    expect(api.searchTest).toBeDefined();
  });

  it('should call getList via POST /query with params', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.getList({ pageIndex: 1, pageSize: 10 });
    expect(client.post).toHaveBeenCalledWith('/admin/knowledge-bases/query', {
      pageIndex: 1,
      pageSize: 10,
    });
  });

  it('should call getList via POST /query without params', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.getList();
    expect(client.post).toHaveBeenCalledWith('/admin/knowledge-bases/query', {});
  });

  it('should call getById', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.getById('kb1');
    expect(client.get).toHaveBeenCalledWith('/admin/knowledge-bases/kb1');
  });

  it('should call create', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.create({ name: 'Test KB', description: 'test' });
    expect(client.post).toHaveBeenCalledWith('/admin/knowledge-bases', {
      name: 'Test KB',
      description: 'test',
    });
  });

  it('should call create with embeddingModel', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.create({ name: 'Test KB', embeddingModel: 'text-embedding-3-small' });
    expect(client.post).toHaveBeenCalledWith('/admin/knowledge-bases', {
      name: 'Test KB',
      embeddingModel: 'text-embedding-3-small',
    });
  });

  it('should call update', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.update('kb1', { name: 'Updated KB' });
    expect(client.put).toHaveBeenCalledWith('/admin/knowledge-bases/kb1', {
      name: 'Updated KB',
    });
  });

  it('should call delete', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.delete('kb1');
    expect(client.delete).toHaveBeenCalledWith('/admin/knowledge-bases/kb1');
  });

  it('should call uploadDocument via client.upload to the /upload route', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    const file = new File(['test content'], 'test.txt');
    await api.uploadDocument('kb1', file);
    // Must use client.upload (multipart) — NOT client.post, which would
    // JSON.stringify the file away — and hit the backend POST {id}/upload route.
    expect(client.upload).toHaveBeenCalledWith('/admin/knowledge-bases/kb1/upload', file);
    expect(client.post).not.toHaveBeenCalled();
  });

  it('should call getDocuments via POST {id}/documents/query', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.getDocuments('kb1', { pageIndex: 1, pageSize: 20 });
    expect(client.post).toHaveBeenCalledWith('/admin/knowledge-bases/kb1/documents/query', {
      pageIndex: 1,
      pageSize: 20,
    });
  });

  it('should call deleteDocument', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.deleteDocument('kb1', 'doc1');
    expect(client.delete).toHaveBeenCalledWith('/admin/knowledge-bases/kb1/documents/doc1');
  });

  it('should call searchTest via POST {id}/search', async () => {
    const api = useAdminKnowledgeBaseApi(client);
    await api.searchTest('kb1', { query: 'test', topK: 5 });
    expect(client.post).toHaveBeenCalledWith('/admin/knowledge-bases/kb1/search', {
      query: 'test',
      topK: 5,
    });
  });
});
