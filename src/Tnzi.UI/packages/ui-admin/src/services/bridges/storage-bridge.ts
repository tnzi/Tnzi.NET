/**
 * Storage bridge — full implementation (Phase 3 Task 3.17).
 *
 * Adapts the storage backend APIs to BridgeCrudContract + ChunkUploader shapes
 * used by TCrudPage-based storage management pages.
 *
 * Sub-contracts:
 *   - files    → useAdminFileApi (full CRUD + chunked upload + downloadUrl)
 *   - chunks   → stub (no standalone admin chunk query endpoint in backend)
 *   - versions → stub (versions are accessed per-file; no standalone paged admin endpoint)
 *
 * BACKEND GAPS:
 *   - chunks.fetch: DefaultStorageAdminController has no GET /admin/chunks paged endpoint.
 *     Chunk data is accessed via upload session context only. Stub rejects.
 *   - versions.fetch: File versions are accessed per-file (GET /files/{id}/versions),
 *     not via a standalone paged admin endpoint. Stub rejects.
 *   - versions.restore: Version restore is per-file (POST /files/{id}/versions/{n}/restore).
 *     Without a standalone version record ID → file mapping, this is a stub.
 *   - files.create / files.update: The admin API has no dedicated create/update form endpoints;
 *     file creation goes through chunked upload. These stubs reject.
 */
import {
  useAdminFileApi,
  useStorageApi,
  type FileRecordDto,
  type FileQueryDto,
} from '@tnzi/core/services/storage'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { mapQueryToListRequest } from '../_mappers'

type HttpClient = Parameters<typeof useAdminFileApi>[0]

/** ChunkUploader contract required by TChunkFileUpload */
export interface ChunkUploader {
  initUpload(fileMeta: { name: string; size: number; chunkCount: number }): Promise<{ uploadId: string }>
  uploadChunk(uploadId: string, chunkIndex: number, chunk: Blob): Promise<void>
  completeUpload(uploadId: string): Promise<{ url: string }>
}

export interface StorageFilesContract extends BridgeCrudContract<FileRecordDto> {
  /** Returns a direct download URL for a file. Works offline (no async needed). */
  downloadUrl(id: string): string
  /** ChunkUploader methods for TChunkFileUpload */
  initUpload: ChunkUploader['initUpload']
  uploadChunk: ChunkUploader['uploadChunk']
  completeUpload: ChunkUploader['completeUpload']
}

export interface StorageBridge {
  files: StorageFilesContract
  /**
   * Chunks — paged audit list wired to
   * DefaultStorageAuditAdminController (Plan E, 2026-04-14).
   * Supports optional filter by uploadSessionId passed through query.filters.
   */
  chunks: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<FileChunkAuditDto>>
    delete(ids: string[]): Promise<void>
  }
  /**
   * Versions — paged audit list wired to
   * DefaultStorageAuditAdminController (Plan E, 2026-04-14).
   * Supports optional filter by fileId and currentOnly via query.filters.
   */
  versions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<FileVersionAuditDto>>
    restore(versionId: string): Promise<void>
  }
}

/**
 * Inline mirror of Tnzi.Storage.Dtos.FileChunkAuditDto.
 * TODO(contracts-sync): regenerate @tnzi/core/services/storage and import from there.
 */
export interface FileChunkAuditDto {
  id: string
  uploadSessionId: string
  chunkIndex: number
  chunkSize: number
  md5Hash?: string | null
  creationTime: string
}

/**
 * Inline mirror of Tnzi.Storage.Dtos.FileVersionAuditDto.
 * TODO(contracts-sync): regenerate @tnzi/core/services/storage and import from there.
 */
export interface FileVersionAuditDto {
  id: string
  fileId: string
  version: number
  path: string
  size: number
  md5Hash?: string | null
  description?: string | null
  isCurrent: boolean
  creationTime: string
  creatorId?: string | null
}

export interface StorageBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock API objects directly. */
  fileApi?: ReturnType<typeof useAdminFileApi>
  storageApi?: ReturnType<typeof useStorageApi>
}

function unwrap<T>(res: T | { data: T; succeeded: boolean }): T {
  if (res && typeof res === 'object' && 'succeeded' in (res as object) && 'data' in (res as object)) {
    return (res as { data: T; succeeded: boolean }).data
  }
  return res as T
}

export function createStorageBridge(deps: StorageBridgeDeps = {}): StorageBridge {
  const fileApi = deps.fileApi ?? (deps.client ? useAdminFileApi(deps.client) : null)
  const storageApi = deps.storageApi ?? (deps.client ? useStorageApi(deps.client) : null)

  if (!fileApi || !storageApi) {
    const noOp = () => Promise.reject(new Error('createStorageBridge: no deps provided'))
    const stub = () => Promise.reject(new Error('Not implemented'))
    return {
      files: {
        fetch: noOp as never,
        create: noOp as never,
        update: noOp as never,
        delete: noOp as never,
        downloadUrl: () => '',
        initUpload: noOp as never,
        uploadChunk: noOp as never,
        completeUpload: noOp as never,
      },
      chunks: { fetch: stub as never, delete: noOp as never },
      versions: { fetch: stub as never, restore: stub as never },
    }
  }

  // ---- files ----

  const files: StorageFilesContract = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FileRecordDto>> => {
      const params = mapQueryToListRequest(query) as unknown as FileQueryDto
      const result = unwrap<{ items: FileRecordDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await fileApi.query(params),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },

    // Files are not created via a form — creation happens through chunked upload.
    create: async (_data): Promise<FileRecordDto> => {
      throw new Error('files.create: use chunked upload (initUpload/uploadChunk/completeUpload) instead of create')
    },

    // Files cannot be updated via the admin API — metadata changes go through specific endpoints.
    update: async (_id, _data): Promise<FileRecordDto> => {
      throw new Error('files.update: no generic update endpoint in backend; use rename/tags endpoints directly')
    },

    delete: async (ids: string[]): Promise<void> => {
      await fileApi.batchDelete(ids)
    },

    downloadUrl: (id: string): string => {
      // Use a relative path that matches the backend route /api/files/{id}/download
      return `/api/files/${id}/download`
    },

    initUpload: async (fileMeta: { name: string; size: number; chunkCount: number }): Promise<{ uploadId: string }> => {
      const session = unwrap(
        await storageApi.initiateChunkedUpload({
          fileName: fileMeta.name,
          totalSize: fileMeta.size,
          chunkSize: Math.ceil(fileMeta.size / Math.max(fileMeta.chunkCount, 1)),
        }),
      )
      return { uploadId: (session as { id: string }).id }
    },

    uploadChunk: async (uploadId: string, chunkIndex: number, chunk: Blob): Promise<void> => {
      await storageApi.uploadChunk(uploadId, chunkIndex, chunk as File)
    },

    completeUpload: async (uploadId: string): Promise<{ url: string }> => {
      const record = unwrap(await storageApi.completeChunkedUpload(uploadId, { isTemporary: false }))
      return { url: (record as FileRecordDto).url }
    },
  }

  // ---- chunks: wired to /admin/storage/audit/chunks (Plan E, 2026-04-14) ----
  // TODO(contracts-sync): move to useAdminStorageAuditApi once @tnzi/core is regenerated.
  const chunks: StorageBridge['chunks'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FileChunkAuditDto>> => {
      if (!deps.client) {
        throw new Error('chunks.fetch: HttpClient (deps.client) is required')
      }
      const params = new URLSearchParams({
        pageIndex: String(query.pageIndex),
        pageSize: String(query.pageSize),
      })
      const uploadSessionId = query.filters.uploadSessionId
      if (typeof uploadSessionId === 'string' && uploadSessionId.length > 0) {
        params.set('uploadSessionId', uploadSessionId)
      }
      const res = await deps.client.get<{
        items: FileChunkAuditDto[]
        totalCount: number
        pageIndex: number
        pageSize: number
      }>(`/admin/storage/audit/chunks?${params.toString()}`)
      const paged = unwrap(res)
      return {
        items: paged.items ?? [],
        totalCount: paged.totalCount ?? 0,
        pageIndex: paged.pageIndex ?? query.pageIndex,
        pageSize: paged.pageSize ?? query.pageSize,
      }
    },
    delete: async (ids: string[]): Promise<void> => {
      // Orphaned chunk cleanup reuses file batch delete (same repo semantics).
      await fileApi.batchDelete(ids)
    },
  }

  // ---- versions: wired to /admin/storage/audit/versions (Plan E, 2026-04-14) ----
  const versions: StorageBridge['versions'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FileVersionAuditDto>> => {
      if (!deps.client) {
        throw new Error('versions.fetch: HttpClient (deps.client) is required')
      }
      const params = new URLSearchParams({
        pageIndex: String(query.pageIndex),
        pageSize: String(query.pageSize),
      })
      const fileId = query.filters.fileId
      if (typeof fileId === 'string' && fileId.length > 0) {
        params.set('fileId', fileId)
      }
      const currentOnly = query.filters.currentOnly
      if (typeof currentOnly === 'boolean') {
        params.set('currentOnly', String(currentOnly))
      }
      const res = await deps.client.get<{
        items: FileVersionAuditDto[]
        totalCount: number
        pageIndex: number
        pageSize: number
      }>(`/admin/storage/audit/versions?${params.toString()}`)
      const paged = unwrap(res)
      return {
        items: paged.items ?? [],
        totalCount: paged.totalCount ?? 0,
        pageIndex: paged.pageIndex ?? query.pageIndex,
        pageSize: paged.pageSize ?? query.pageSize,
      }
    },
    restore: async (_versionId: string): Promise<void> => {
      // Backend audit endpoint is read-only. Per-file restore still goes
      // through storageApi.restoreVersion with fileId + version number.
      throw new Error('versions.restore: admin audit endpoint is read-only; use storageApi.restoreVersion(fileId, version) directly')
    },
  }

  return { files, chunks, versions }
}
