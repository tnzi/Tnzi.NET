/**
 * RAG API wrappers — Knowledge base queries, chat, and admin management
 *
 * User-facing: useRagApi (query + chat + stream)
 * Admin: useAdminKnowledgeBaseApi (CRUD + doc upload + search test)
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

export interface RagQueryParams {
  query: string;
  knowledgeBaseIds: string[];
  topK?: number;
  threshold?: number;
}

export interface RagChatParams {
  message: string;
  knowledgeBaseIds: string[];
  threadId?: string;
  topK?: number;
}

export interface KnowledgeBaseCreateParams {
  name: string;
  description?: string;
  embeddingModel?: string;
}

export interface KnowledgeBaseUpdateParams {
  name?: string;
  description?: string;
}

export interface SearchTestParams {
  query: string;
  topK?: number;
  threshold?: number;
}

/** Result returned from POST /admin/knowledge-bases/{id}/reindex */
export interface ReindexResultDto {
  knowledgeBaseId: string;
  chunkCount: number;
  documentCount: number;
  durationMs: number;
}

// ---------------------------------------------------------------------------
// User-facing RAG API
// Route: /rag
// ---------------------------------------------------------------------------

/** User-facing RAG query and chat API */
export function useRagApi(client: HttpClient) {
  const base = '/rag';

  return {
    /** Query knowledge base(s) — returns relevant chunks with scores */
    query: (data: RagQueryParams) =>
      client.post(`${base}/query`, data),

    /** Chat with RAG context — returns answer with citations */
    chat: (data: RagChatParams) =>
      client.post(`${base}/chat`, data),

    /** Get SSE stream URL for RAG chat */
    getStreamUrl: () =>
      `${base}/stream`,
  };
}

// ---------------------------------------------------------------------------
// Admin Knowledge Base API
// Route: /admin/knowledge-bases
// ---------------------------------------------------------------------------

/** Admin knowledge base management API */
export function useAdminKnowledgeBaseApi(client: HttpClient) {
  const base = '/admin/knowledge-bases';

  return {
    /**
     * Get paginated knowledge base list.
     *
     * Backend exposes the query as POST /admin/knowledge-bases/query (consistent
     * with the rest of the AI module's PagedQueryDto endpoints). GET /admin/
     * knowledge-bases is reserved for the POST create endpoint, so calling it
     * with `?page=…&pageSize=…` returns 405 Method Not Allowed.
     */
    getList: (params?: { pageIndex?: number; pageSize?: number; keyword?: string }) =>
      client.post(`${base}/query`, params ?? {}),

    /** Get knowledge base by ID */
    getById: (id: string) =>
      client.get(`${base}/${id}`),

    /** Create a new knowledge base */
    create: (data: KnowledgeBaseCreateParams) =>
      client.post(base, data),

    /** Update a knowledge base */
    update: (id: string, data: KnowledgeBaseUpdateParams) =>
      client.put(`${base}/${id}`, data),

    /** Delete a knowledge base */
    delete: (id: string) =>
      client.delete(`${base}/${id}`),

    /** Upload a document to a knowledge base */
    uploadDocument: (kbId: string, file: File) => {
      const formData = new FormData();
      formData.append('file', file);
      return client.post(`${base}/${kbId}/documents`, formData);
    },

    /** Get documents in a knowledge base */
    getDocuments: (kbId: string, params?: { page?: number; pageSize?: number }) =>
      client.get(`${base}/${kbId}/documents`, { params }),

    /** Delete a document from a knowledge base */
    deleteDocument: (kbId: string, docId: string) =>
      client.delete(`${base}/${kbId}/documents/${docId}`),

    /** Test search against a knowledge base */
    searchTest: (kbId: string, data: SearchTestParams) =>
      client.post(`${base}/${kbId}/search-test`, data),

    /** Get knowledge base stats (document count, chunk count, etc.) */
    getStats: (kbId: string) =>
      client.get(`${base}/${kbId}/stats`),

    /** Trigger full re-vectorization of all chunks in a knowledge base (admin) */
    reindex: (kbId: string) =>
      client.post<ReindexResultDto>(`${base}/${kbId}/reindex`, {}),
  };
}
