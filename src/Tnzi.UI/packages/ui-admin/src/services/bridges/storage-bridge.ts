/**
 * Storage bridge - full implementation (Phase 3 Task 3.17).
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
  useAdminFileFolderApi,
  useStorageApi,
  useStoragePreviewApi,
  type FileRecordDto,
  type FileQueryDto,
  type FileFolderDto,
  type CreateFileFolderDto,
  type UpdateFileFolderDto,
  type FileStorageStatisticsDto,
  type FileShareSummaryDto,
  type FileSharePreviewDto,
  type ActiveSharesQueryDto,
  type FileIntegrityResultDto,
  type BatchIntegrityResultDto,
  type FileReferenceDto,
  type UserStorageUsageDto,
  type FileUrlKind,
} from '@tnzi/core/services/storage'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'
import { getFileUrlResolver } from '../file-url-resolver'

type HttpClient = Parameters<typeof useAdminFileApi>[0]

/** ChunkUploader contract required by TChunkFileUpload */
export interface ChunkUploader {
  initUpload(fileMeta: { name: string; size: number; chunkCount: number }): Promise<{ uploadId: string }>
  uploadChunk(uploadId: string, chunkIndex: number, chunk: Blob): Promise<void>
  /**
   * Finish a chunked upload and return the stored record.
   *
   * Returns the record rather than a `{ url }`: the backend's `FileRecordDto`
   * has never carried a `url`, so the old shape resolved to `{ url: undefined }`
   * on every successful upload. Callers build the URL they need from `id`
   * (download / preview / presigned), which is also what every other file
   * surface in the admin does.
   */
  completeUpload(uploadId: string): Promise<FileRecordDto>
}

export interface StorageFilesContract extends BridgeCrudContract<FileRecordDto> {
  /**
   * Direct download URL, unsigned. Only works for PUBLIC files - a browser
   * request carries no Authorization header. Use `signedUrl` for anything else.
   */
  downloadUrl(id: string): string
  /**
   * Inline preview URL, unsigned. Only works for PUBLIC files (avatars, site
   * assets). Use `signedUrl` for anything else.
   */
  previewUrl(id: string): string
  /**
   * URL that renders a PRIVATE file in an `<img>` / `<a download>`: it carries
   * a short-lived signed token because those requests cannot send a bearer
   * token. Resolves to `null` when the caller may not read the file.
   *
   * Batched and cached across call sites, so a list of N files costs one round
   * trip rather than N.
   */
  signedUrl(id: string, kind?: FileUrlKind): Promise<string | null>
  /** Signed URLs for many files at once. Unreadable ids are absent from the map. */
  signedUrls(ids: string[], kind?: FileUrlKind): Promise<Map<string, string>>
  /** Single-shot upload for small files (e.g. avatars); pre-unwraps the envelope. */
  upload(file: File): Promise<FileRecordDto>
  /** Move a batch of files to a target folder (null = root / unfiled). */
  moveTo(fileIds: string[], folderId: string | null): Promise<void>
  /** ChunkUploader methods for TChunkFileUpload */
  initUpload: ChunkUploader['initUpload']
  uploadChunk: ChunkUploader['uploadChunk']
  completeUpload: ChunkUploader['completeUpload']
}

/**
 * Folder management surface - drives the StorageFile browser's left tree
 * and the create/move-folder dialogs.
 */
export interface StorageFoldersContract {
  /** Full folder tree from root (or rooted at `parentId`). */
  getTree(parentId?: string | null): Promise<FileFolderDto[]>
  getById(id: string): Promise<FileFolderDto>
  create(data: CreateFileFolderDto): Promise<FileFolderDto>
  update(id: string, data: UpdateFileFolderDto): Promise<FileFolderDto>
  delete(id: string): Promise<void>
  /** Move a folder under a new parent (null = make it a root). */
  move(id: string, newParentId: string | null): Promise<void>
}

export interface StorageBridge {
  files: StorageFilesContract
  /** Hierarchical folder management - see {@link StorageFoldersContract}. */
  folders: StorageFoldersContract
  /**
   * Chunks - paged audit list wired to
   * DefaultStorageAuditAdminController (Plan E, 2026-04-14).
   * Supports optional filter by uploadSessionId passed through query.filters.
   */
  chunks: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<FileChunkAuditDto>>
    delete(ids: string[]): Promise<void>
  }
  /**
   * Versions - paged audit list wired to
   * DefaultStorageAuditAdminController (Plan E, 2026-04-14).
   * Supports optional filter by fileId and currentOnly via query.filters.
   */
  versions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<FileVersionAuditDto>>
    /**
     * Restore an older version as the current one. Reuses the user-facing
     * endpoint `POST /files/{fileId}/versions/{version}/restore` because the
     * admin audit controller is intentionally read-only.
     */
    restore(fileId: string, version: number): Promise<void>
  }
  /** Aggregate storage statistics - GET /admin/files/statistics. */
  statistics: {
    get(): Promise<FileStorageStatisticsDto>
  }
  /**
   * Share-link management - paged active-share listing + revocation.
   * Wired to /admin/files/shares/* and /admin/files/{id}/shares.
   */
  shares: StorageSharesContract
  /**
   * The RECIPIENT side of a share link - anonymous, no admin shell, no session.
   * Distinct from `shares` above, which is the owner listing/revoking links.
   *
   * Lives on the bridge (rather than the page calling `useStorageApi` directly)
   * so `pages/share/SharePage.vue` keeps the page → bridge → core layering every
   * other page follows, and so its tests can `vi.mock` this module.
   */
  publicShare: StoragePublicShareContract
  /** File-integrity verification - single + batch. */
  integrity: {
    verifyOne(fileId: string): Promise<FileIntegrityResultDto>
    batchVerify(maxFiles?: number): Promise<BatchIntegrityResultDto>
  }
  /** File tags - set per file + query files by a single tag. */
  tags: {
    set(fileId: string, tags: string[]): Promise<FileRecordDto>
    byTag(tag: string, query: CrudPageQuery): Promise<CrudPageResult<FileRecordDto>>
  }
  /** File metadata (key/value map). */
  metadata: {
    get(fileId: string): Promise<Record<string, string>>
    set(fileId: string, metadata: Record<string, string>): Promise<FileRecordDto>
  }
  /**
   * Public-read visibility. Public files are readable by anyone including
   * unauthenticated callers, which is what an `<img src>` needs - right for
   * avatars and site assets, wrong for contracts, cheques and HR documents.
   */
  visibility: {
    set(fileId: string, isPublic: boolean): Promise<FileRecordDto>
    /**
     * Backfill from the backend's `[FileField(Public = true)]` declarations;
     * resolves to the number of files changed. One-shot repair for avatars
     * stored before those declarations existed. Never makes a file private.
     */
    syncFromDeclarations(): Promise<number>
  }
  /** File-reference queries - by file or by owning entity. */
  references: {
    byFile(fileId: string): Promise<FileReferenceDto[]>
    byEntity(entityType: string, entityId: string): Promise<FileReferenceDto[]>
  }
  /** Per-user storage usage - single user + top-N leaderboard. */
  userUsage: {
    forUser(userId: string): Promise<UserStorageUsageDto>
    topUsers(topN?: number): Promise<UserStorageUsageDto[]>
  }
  /** Temporary-file maintenance - list + cleanup trigger. */
  cleanup: {
    temporaryFiles(olderThanHours?: number): Promise<FileRecordDto[]>
    trigger(olderThanHours?: number): Promise<number>
  }
  /** File preview - capability check + URL resolution (user-facing controller). */
  preview: {
    canPreview(fileId: string): Promise<boolean>
    url(fileId: string): Promise<string>
  }
}

/**
 * Active-share listing + revocation surface. Read + revoke only - shares are
 * created from the file detail / user-facing API, never via this admin list.
 */
export interface StorageSharesContract {
  fetch(query: CrudPageQuery): Promise<CrudPageResult<FileShareSummaryDto>>
  byFile(fileId: string): Promise<FileShareSummaryDto[]>
  batchRevoke(shareIds: string[]): Promise<number>
}

/**
 * Anonymous share-link surface used by the recipient page (`/share/:token`).
 *
 * All three are unauthenticated by design: the recipient is a client, auditor
 * or vendor with no account.
 */
export interface StoragePublicShareContract {
  /**
   * What the link points at, WITHOUT consuming an access.
   *
   * Resolves to `null` for every unusable link - revoked, expired, exhausted or
   * never existed. The page shows one message for all of them: distinguishing
   * them would turn this into a probe, and the recipient can do nothing
   * different about any of them anyway.
   */
  preview(token: string): Promise<FileSharePreviewDto | null>
  /**
   * Check the link password without consuming an access. `false` for a wrong
   * password; never throws for the ordinary wrong-password case.
   */
  verifyPassword(token: string, password?: string): Promise<boolean>
  /**
   * Anonymous download URL - goes straight into `window.location` / `<a href>`.
   * Reading it as a Blob instead would break large files, resumable transfers
   * and the browser's own download manager.
   */
  downloadUrl(token: string, password?: string): string
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
  folderApi?: ReturnType<typeof useAdminFileFolderApi>
  previewApi?: ReturnType<typeof useStoragePreviewApi>
}

export function createStorageBridge(deps: StorageBridgeDeps = {}): StorageBridge {
  const fileApi = deps.fileApi ?? (deps.client ? useAdminFileApi(deps.client) : null)
  const storageApi = deps.storageApi ?? (deps.client ? useStorageApi(deps.client) : null)
  // folderApi is optional - when neither a client nor an explicit api was
  // supplied the folders sub-contract degrades to lazy-rejecting stubs (same
  // pattern as identity-bridge). This keeps the existing 2-api test fixtures
  // green while still providing real implementations in production.
  const folderApi =
    deps.folderApi ?? (deps.client ? useAdminFileFolderApi(deps.client) : null)
  const previewApi =
    deps.previewApi ?? (deps.client ? useStoragePreviewApi(deps.client) : null)

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
        previewUrl: () => '',
        signedUrl: async () => null,
        signedUrls: async () => new Map<string, string>(),
        upload: noOp as never,
        moveTo: noOp as never,
        initUpload: noOp as never,
        uploadChunk: noOp as never,
        completeUpload: noOp as never,
      },
      folders: {
        getTree: noOp as never,
        getById: noOp as never,
        create: noOp as never,
        update: noOp as never,
        delete: noOp as never,
        move: noOp as never,
      },
      chunks: { fetch: stub as never, delete: noOp as never },
      versions: { fetch: stub as never, restore: stub as never },
      statistics: { get: noOp as never },
      shares: { fetch: stub as never, byFile: noOp as never, batchRevoke: noOp as never },
      publicShare: {
        preview: async () => null,
        verifyPassword: async () => false,
        downloadUrl: () => '',
      },
      integrity: { verifyOne: noOp as never, batchVerify: noOp as never },
      tags: { set: noOp as never, byTag: stub as never },
      metadata: { get: noOp as never, set: noOp as never },
      visibility: { set: noOp as never, syncFromDeclarations: noOp as never },
      references: { byFile: noOp as never, byEntity: noOp as never },
      userUsage: { forUser: noOp as never, topUsers: noOp as never },
      cleanup: { temporaryFiles: noOp as never, trigger: noOp as never },
      preview: { canPreview: noOp as never, url: noOp as never },
    }
  }

  /** Unsigned URL for the requested read endpoint. */
  const plainUrl = (id: string, kind: FileUrlKind = 'preview'): string =>
    kind === 'download'
      ? storageApi.getDownloadUrl(id)
      : kind === 'thumbnail'
        ? storageApi.getThumbnailUrl(id)
        : storageApi.getPreviewUrl(id)

  const missing = <T>(label: string): Promise<T> =>
    Promise.reject(
      new Error(`storage-bridge: ${label} requires an HttpClient or explicit folderApi mock`),
    )

  // ---- files ----

  const files: StorageFilesContract = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FileRecordDto>> => {
      const params = mapQueryToListRequest(query) as unknown as FileQueryDto
      const result = unwrap<{ items: FileRecordDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await fileApi.query(params),
      )
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },

    // Files are not created via a form - creation happens through chunked upload.
    create: async (_data): Promise<FileRecordDto> => {
      throw new Error('files.create: use chunked upload (initUpload/uploadChunk/completeUpload) instead of create')
    },

    // Files cannot be updated via the admin API - metadata changes go through specific endpoints.
    update: async (_id, _data): Promise<FileRecordDto> => {
      throw new Error('files.update: no generic update endpoint in backend; use rename/tags endpoints directly')
    },

    delete: async (ids: string[]): Promise<void> => {
      ensureOk(await fileApi.batchDelete(ids))
    },

    // Deployment-prefix-aware download URL (resolveUrl), symmetric with
    // previewUrl - no hardcoded `/api` that breaks under a sub-app mount.
    downloadUrl: (id: string): string => storageApi.getDownloadUrl(id),

    // Synchronous anonymous preview URL (no request) - correct for PUBLIC files
    // only (avatars, site assets). Private files need `signedUrl`.
    previewUrl: (id: string): string => storageApi.getPreviewUrl(id),

    // Signed URLs for private files. The resolver is shared per client (see
    // services/fileUrlResolver) so the token cache survives this bridge, which
    // is recreated on every page mount.
    //
    // Without a client there is nothing to mint against - that only happens on
    // the api-injection path used by unit tests, where the plain URL is what
    // those fixtures already assert on.
    signedUrl: (id: string, kind?: FileUrlKind): Promise<string | null> =>
      deps.client
        ? getFileUrlResolver(deps.client).resolve(id, kind)
        : Promise.resolve(plainUrl(id, kind)),

    signedUrls: (ids: string[], kind?: FileUrlKind): Promise<Map<string, string>> =>
      deps.client
        ? getFileUrlResolver(deps.client).resolveMany(ids, kind)
        : Promise.resolve(new Map(ids.filter(Boolean).map((id) => [id, plainUrl(id, kind)]))),

    // Single-shot upload (avatars etc.); pre-unwraps the ApiResult envelope so
    // callers get the stored FileRecordDto directly (the endpoint returns the record;
    // there was never a separate upload-result DTO on the backend).
    upload: async (file: File): Promise<FileRecordDto> =>
      unwrap<FileRecordDto>(await storageApi.upload(file)),

    moveTo: async (fileIds: string[], folderId: string | null): Promise<void> => {
      if (!folderApi) {
        throw new Error('files.moveTo: folderApi not configured')
      }
      if (!fileIds.length) return
      ensureOk(await folderApi.moveFiles({ fileIds, folderId }))
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
      ensureOk(await storageApi.uploadChunk(uploadId, chunkIndex, chunk as File))
    },

    completeUpload: async (uploadId: string): Promise<FileRecordDto> => {
      return unwrap(await storageApi.completeChunkedUpload(uploadId, { isTemporary: false }))
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
      return pagedResult({
        items: paged.items ?? [],
        totalCount: paged.totalCount ?? 0,
        pageIndex: paged.pageIndex ?? query.pageIndex,
        pageSize: paged.pageSize ?? query.pageSize,
      })
    },
    delete: async (_ids: string[]): Promise<void> => {
      // Chunks are managed by the upload lifecycle (initiate/upload/complete/abort)
      // - there's no direct admin "delete a single chunk row" endpoint. The previous
      // implementation forwarded to `fileApi.batchDelete`, which targets FileRecord
      // (the wrong table), so chunk delete was a silent no-op against the wrong rows.
      throw new Error(
        'chunks.delete: not supported by the admin audit endpoint - abort the parent upload session via the user-facing /storage API to clean up chunks',
      )
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
      return pagedResult({
        items: paged.items ?? [],
        totalCount: paged.totalCount ?? 0,
        pageIndex: paged.pageIndex ?? query.pageIndex,
        pageSize: paged.pageSize ?? query.pageSize,
      })
    },
    restore: async (fileId: string, version: number): Promise<void> => {
      if (!deps.client) {
        throw new Error('versions.restore: HttpClient (deps.client) is required')
      }
      // Admin audit endpoint is read-only - reuse the user-facing restore endpoint
      // (DefaultStorageController.RestoreVersion). The audit controller could
      // proxy it, but keeping a single source of truth for restore semantics
      // matters more than ducking through `/admin`.
      ensureOk(await deps.client.post(`/files/${encodeURIComponent(fileId)}/versions/${version}/restore`))
    },
  }

  const folders: StorageFoldersContract = folderApi
    ? {
        getTree: async (parentId?: string | null): Promise<FileFolderDto[]> => {
          const items = unwrap<FileFolderDto[]>(await folderApi.getTree(parentId))
          return Array.isArray(items) ? items : []
        },
        getById: async (id: string) =>
          unwrap<FileFolderDto>(await folderApi.getById(id)),
        create: async (data: CreateFileFolderDto) =>
          unwrap<FileFolderDto>(await folderApi.create(data)),
        update: async (id: string, data: UpdateFileFolderDto) =>
          unwrap<FileFolderDto>(await folderApi.update(id, data)),
        delete: async (id: string) => {
          ensureOk(await folderApi.delete(id))
        },
        move: async (id: string, newParentId: string | null) => {
          ensureOk(await folderApi.move(id, newParentId))
        },
      }
    : {
        getTree: () => missing('folders.getTree'),
        getById: () => missing('folders.getById'),
        create: () => missing('folders.create'),
        update: () => missing('folders.update'),
        delete: () => missing('folders.delete'),
        move: () => missing('folders.move'),
      }

  // ---- statistics ----
  const statistics: StorageBridge['statistics'] = {
    get: async (): Promise<FileStorageStatisticsDto> =>
      unwrap<FileStorageStatisticsDto>(await fileApi.getStatistics()),
  }

  // ---- shares ----
  const shares: StorageSharesContract = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FileShareSummaryDto>> => {
      const f = query.filters
      const request: ActiveSharesQueryDto = {
        pageIndex: query.pageIndex,
        pageSize: query.pageSize,
        skip: (query.pageIndex - 1) * query.pageSize,
        take: query.pageSize,
        fileId: typeof f.fileId === 'string' && f.fileId ? f.fileId : undefined,
        creatorId: typeof f.creatorId === 'string' && f.creatorId ? f.creatorId : undefined,
        includeExpired: typeof f.includeExpired === 'boolean' ? f.includeExpired : undefined,
        includeDisabled: typeof f.includeDisabled === 'boolean' ? f.includeDisabled : undefined,
      }
      const result = unwrap<{
        items: FileShareSummaryDto[]
        totalCount: number
        pageIndex: number
        pageSize: number
      }>(await fileApi.queryActiveShares(request))
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    byFile: async (fileId: string): Promise<FileShareSummaryDto[]> => {
      const items = unwrap<FileShareSummaryDto[]>(await fileApi.getSharesByFile(fileId))
      return Array.isArray(items) ? items : []
    },
    batchRevoke: async (shareIds: string[]): Promise<number> => {
      if (!shareIds.length) return 0
      return unwrap<number>(await fileApi.batchRevokeShares(shareIds))
    },
  }

  // ---- publicShare (recipient side of a share link, anonymous) ----
  const publicShare: StoragePublicShareContract = {
    preview: async (token: string): Promise<FileSharePreviewDto | null> => {
      try {
        const res = await storageApi.getSharePreview(token)
        return res.succeeded ? (res.data ?? null) : null
      } catch {
        // A network failure and "this link is gone" are the same event to the
        // recipient: either way there is nothing they can do here.
        return null
      }
    },
    verifyPassword: async (token: string, password?: string): Promise<boolean> => {
      const res = await storageApi.verifyShareAccess(token, password)
      return res.succeeded && res.data === true
    },
    downloadUrl: (token: string, password?: string): string =>
      storageApi.getShareDownloadUrl(token, password || undefined),
  }

  // ---- integrity ----
  const integrity: StorageBridge['integrity'] = {
    verifyOne: async (fileId: string): Promise<FileIntegrityResultDto> =>
      unwrap<FileIntegrityResultDto>(await fileApi.verifyFileIntegrity(fileId)),
    batchVerify: async (maxFiles = 100): Promise<BatchIntegrityResultDto> =>
      unwrap<BatchIntegrityResultDto>(await fileApi.batchVerifyIntegrity(maxFiles)),
  }

  // ---- tags ----
  const tags: StorageBridge['tags'] = {
    set: async (fileId: string, tagList: string[]): Promise<FileRecordDto> =>
      unwrap<FileRecordDto>(await fileApi.setFileTags(fileId, { tags: tagList })),
    byTag: async (tag: string, query: CrudPageQuery): Promise<CrudPageResult<FileRecordDto>> => {
      const result = unwrap<{
        items: FileRecordDto[]
        totalCount: number
        pageIndex: number
        pageSize: number
      }>(await fileApi.getFilesByTag(tag, query.pageIndex, query.pageSize))
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
  }

  // ---- metadata ----
  const metadata: StorageBridge['metadata'] = {
    get: async (fileId: string): Promise<Record<string, string>> => {
      const map = unwrap<Record<string, string>>(await fileApi.getMetadata(fileId))
      return map ?? {}
    },
    set: async (fileId: string, meta: Record<string, string>): Promise<FileRecordDto> =>
      unwrap<FileRecordDto>(await fileApi.setMetadata(fileId, meta)),
  }

  // ---- visibility (public read) ----
  const visibility: StorageBridge['visibility'] = {
    set: async (fileId: string, isPublic: boolean): Promise<FileRecordDto> =>
      unwrap<FileRecordDto>(await fileApi.setFileVisibility(fileId, { isPublic })),
    syncFromDeclarations: async (): Promise<number> =>
      unwrap<number>(await fileApi.syncPublicFlags()),
  }

  // ---- references ----
  const references: StorageBridge['references'] = {
    byFile: async (fileId: string): Promise<FileReferenceDto[]> => {
      const items = unwrap<FileReferenceDto[]>(await fileApi.getReferences(fileId))
      return Array.isArray(items) ? items : []
    },
    byEntity: async (entityType: string, entityId: string): Promise<FileReferenceDto[]> => {
      const items = unwrap<FileReferenceDto[]>(
        await fileApi.getReferencesByEntity(entityType, entityId),
      )
      return Array.isArray(items) ? items : []
    },
  }

  // ---- userUsage ----
  const userUsage: StorageBridge['userUsage'] = {
    forUser: async (userId: string): Promise<UserStorageUsageDto> =>
      unwrap<UserStorageUsageDto>(await fileApi.getUserStorageUsage(userId)),
    topUsers: async (topN = 20): Promise<UserStorageUsageDto[]> => {
      const items = unwrap<UserStorageUsageDto[]>(await fileApi.getTopUsersByStorage(topN))
      return Array.isArray(items) ? items : []
    },
  }

  // ---- cleanup (temporary files) ----
  const cleanup: StorageBridge['cleanup'] = {
    temporaryFiles: async (olderThanHours?: number): Promise<FileRecordDto[]> => {
      const items = unwrap<FileRecordDto[]>(await fileApi.getTemporaryFiles(olderThanHours))
      return Array.isArray(items) ? items : []
    },
    trigger: async (olderThanHours?: number): Promise<number> =>
      unwrap<number>(await fileApi.cleanupTemporary(olderThanHours)),
  }

  // ---- preview (user-facing controller) ----
  const preview: StorageBridge['preview'] = previewApi
    ? {
        canPreview: async (fileId: string): Promise<boolean> => {
          const can = unwrap<boolean>(await previewApi.canPreview(fileId))
          return can === true
        },
        url: async (fileId: string): Promise<string> =>
          unwrap<string>(await previewApi.getPreviewUrl(fileId)),
      }
    : {
        canPreview: () => missing('preview.canPreview'),
        url: () => missing('preview.url'),
      }

  return {
    files,
    folders,
    chunks,
    versions,
    statistics,
    shares,
    publicShare,
    integrity,
    tags,
    metadata,
    visibility,
    references,
    userUsage,
    cleanup,
    preview,
  }
}
