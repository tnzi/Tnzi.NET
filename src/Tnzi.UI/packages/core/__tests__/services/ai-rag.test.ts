import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import { useRagApi, useAdminKnowledgeBaseApi } from '../../src/services/ai/rag';

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

  // The server drops unknown keys without a word, so every key the client sends
  // must be the backend RagQueryRequest / RagChatRequest property name. A
  // relevance threshold sent as `threshold` was ignored and every query ran at
  // the default 0.3; a chat sent as `message` was rejected with 400 because the
  // required `query` was missing.
  const queryRequestKeys = ['query', 'knowledgeBaseIds', 'topK', 'minRelevance', 'includeCitations'];
  const chatRequestKeys = [...queryRequestKeys, 'threadId', 'agentId'];

  it('should send query options under the backend property names', async () => {
    const api = useRagApi(client);
    await api.query({ query: 'test', knowledgeBaseIds: ['kb1', 'kb2'], topK: 5, minRelevance: 0.8, includeCitations: false });
    const body = client.calls[0].data as Record<string, unknown>;
    expect(body).toEqual({
      query: 'test',
      knowledgeBaseIds: ['kb1', 'kb2'],
      topK: 5,
      minRelevance: 0.8,
      includeCitations: false,
    });
    expect(Object.keys(body).every((key) => queryRequestKeys.includes(key))).toBe(true);
    expect(body).not.toHaveProperty('threshold');
  });

  it('should send the chat question as query, never as message', async () => {
    const api = useRagApi(client);
    await api.chat({ query: 'hello', knowledgeBaseIds: ['kb1'], threadId: 't1', agentId: 'a1', topK: 3 });
    const body = client.calls[0].data as Record<string, unknown>;
    expect(client.calls[0].url).toBe('/rag/chat');
    expect(body).toEqual({ query: 'hello', knowledgeBaseIds: ['kb1'], threadId: 't1', agentId: 'a1', topK: 3 });
    expect(Object.keys(body).every((key) => chatRequestKeys.includes(key))).toBe(true);
    expect(body).not.toHaveProperty('message');
  });

  it('should allow searching every user-queryable knowledge base', async () => {
    const api = useRagApi(client);
    await api.chat({ query: 'hello' });
    expect(client.post).toHaveBeenCalledWith('/rag/chat', { query: 'hello' });
  });

  it('should hand back the backend result shape', async () => {
    (client.post as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      succeeded: true,
      code: 200,
      data: { answer: 'A', citations: [{ text: 'c' }], threadId: 't9' },
    });
    const api = useRagApi(client);
    const result = await api.chat({ query: 'hello' });
    expect(result.data?.answer).toBe('A');
    expect(result.data?.threadId).toBe('t9');
    expect(result.data?.citations[0].text).toBe('c');
  });

  it('should build the stream URL through the client baseUrl', () => {
    const api = useRagApi(client);
    const url = api.getStreamUrl();
    expect(url).toBe('http://localhost:5000/api/rag/chat/stream');
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
    // Must use client.upload (multipart) - NOT client.post, which would
    // JSON.stringify the file away - and hit the backend POST {id}/upload route.
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
