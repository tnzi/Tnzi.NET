/**
 * RAG API wrappers - Knowledge base queries, chat, and admin management
 *
 * User-facing: useRagApi (query + chat + stream)
 * Admin: useAdminKnowledgeBaseApi (CRUD + doc upload + search test)
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';

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

/**
 * Document ingestion status - mirrors the backend `DocumentStatus` enum. The
 * backend registers `JsonStringEnumConverter`, so responses carry the PascalCase
 * member NAME string. The numeric ordinals (0=Processing / 1=Completed /
 * 2=Failed) are kept in the union for backward compatibility with older payloads.
 */
export type DocumentStatus = 'Processing' | 'Completed' | 'Failed' | 0 | 1 | 2;

/** A knowledge base (vector store + ingestion config). */
export interface KnowledgeBaseDto {
  id: string;
  name: string;
  description?: string | null;
  embeddingProvider: string;
  embeddingModel?: string | null;
  chunkSize: number;
  chunkOverlap: number;
  documentCount: number;
  chunkCount: number;
  isEnabled: boolean;
  creationTime: string;
}

/** A document inside a knowledge base. */
export interface KnowledgeDocumentDto {
  id: string;
  knowledgeBaseId: string;
  fileName: string;
  contentType?: string | null;
  fileSize: number;
  chunkCount: number;
  status: DocumentStatus;
  errorMessage?: string | null;
  contentHash?: string | null;
  version: number;
  creationTime: string;
}

/** Result of uploading a document (ingestion is async; poll status by docId). */
export interface DocumentUploadResultDto {
  documentId: string;
  fileName: string;
  status: DocumentStatus;
  chunkCount: number;
  errorMessage?: string | null;
  /** True when the content hash matched an existing document (dedup hit). */
  isDuplicate: boolean;
}

/** A single ranked search hit from a knowledge base. */
export interface SearchResultDto {
  content: string;
  sourceName?: string | null;
  knowledgeBaseName?: string | null;
  /** Similarity score (0-1). */
  score: number;
  chunkIndex: number;
  metadata?: string | null;
}

export interface KnowledgeBaseCreateParams {
  name: string;
  description?: string;
  embeddingProvider?: string;
  embeddingModel?: string;
  chunkSize?: number;
  chunkOverlap?: number;
}

export interface KnowledgeBaseUpdateParams {
  name?: string;
  description?: string;
  isEnabled?: boolean;
}

export interface SearchTestParams {
  query: string;
  topK?: number;
  /** Metadata key/value filter matched against chunk metadata JSON. */
  metadataFilter?: Record<string, string>;
}

/** Query for the paged document list (POST /admin/knowledge-bases/{id}/documents/query). */
export interface DocumentQueryParams {
  pageIndex?: number;
  pageSize?: number;
  /** Filename keyword search. */
  keyword?: string;
  /** Filter by ingestion status (backend DocumentStatus enum value). */
  status?: number;
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
    /** Query knowledge base(s) - returns relevant chunks with scores */
    query: (data: RagQueryParams) =>
      client.post(`${base}/query`, data),

    /** Chat with RAG context - returns answer with citations */
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
      client.post<PagedList<KnowledgeBaseDto>>(`${base}/query`, params ?? {}),

    /** Get knowledge base by ID */
    getById: (id: string) =>
      client.get<KnowledgeBaseDto>(`${base}/${id}`),

    /** Create a new knowledge base */
    create: (data: KnowledgeBaseCreateParams) =>
      client.post<KnowledgeBaseDto>(base, data),

    /** Update a knowledge base */
    update: (id: string, data: KnowledgeBaseUpdateParams) =>
      client.put<KnowledgeBaseDto>(`${base}/${id}`, data),

    /** Delete a knowledge base */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Upload a document to a knowledge base */
    uploadDocument: (kbId: string, file: File) =>
      // Backend route is POST {id}/upload (multipart, IFormFile). Use
      // client.upload (multipart/form-data via XHR) - NOT client.post, which
      // would JSON.stringify the FormData into "{}" and drop the file.
      client.upload<DocumentUploadResultDto>(`${base}/${kbId}/upload`, file),

    /** Get the paged document list for a knowledge base (POST {id}/documents/query) */
    getDocuments: (kbId: string, query?: DocumentQueryParams) =>
      client.post<PagedList<KnowledgeDocumentDto>>(`${base}/${kbId}/documents/query`, query ?? {}),

    /** Poll a single document's ingestion status (GET {id}/documents/{docId}/status) */
    getDocumentStatus: (kbId: string, docId: string) =>
      client.get<KnowledgeDocumentDto>(`${base}/${kbId}/documents/${docId}/status`),

    /** Delete a document from a knowledge base */
    deleteDocument: (kbId: string, docId: string) =>
      client.delete<void>(`${base}/${kbId}/documents/${docId}`),

    /** Search within a knowledge base (POST {id}/search) */
    searchTest: (kbId: string, data: SearchTestParams) =>
      client.post<SearchResultDto[]>(`${base}/${kbId}/search`, data),

    /** Trigger full re-vectorization of all chunks in a knowledge base (admin) */
    reindex: (kbId: string) =>
      client.post<ReindexResultDto>(`${base}/${kbId}/reindex`, {}),
  };
}
