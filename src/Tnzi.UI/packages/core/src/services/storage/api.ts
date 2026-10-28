/**
 * Storage Module API - File storage, versioning, sharing, and admin operations
 * Aligned with backend DefaultStorageController, DefaultStoragePreviewController,
 * and DefaultStorageAdminController
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type { ReorderRequest } from '../../types/api';
import type {
  FileChunkAuditDto,
  FileChunkAuditQueryDto,
  FileVersionAuditDto,
  FileVersionAuditQueryDto,
} from './types';
import type {
  FileRecordDto,
  FileQueryDto,
  FileStorageStatisticsDto,
  ThumbnailBackfillRequest,
  ThumbnailBackfillResult,
  FileUploadSessionDto,
  FileChunkDto,
  FileUploadProgressDto,
  InitiateChunkedUploadDto,
  CompleteChunkedUploadDto,
  FileVersionDto,
  FileShareDto,
  CreateShareDto,
  CompressDto,
  FileReferenceDto,
  FileReferenceInfoDto,
  FileReferenceStatisticsDto,
  UserStorageUsageDto,
  FileIntegrityResultDto,
  BatchIntegrityResultDto,
  FileShareSummaryDto,
  ActiveSharesQueryDto,
  SetFileTagsDto,
  SetFileVisibilityDto,
  FileAccessTokenDto,
  FileSharePreviewDto,
  FileUploadOptions,
  FileFolderDto,
  CreateFileFolderDto,
  UpdateFileFolderDto,
  MoveFilesToFolderRequest,
  StorageQuotaDto,
  StorageAllowanceDto,
  SetStorageAllowanceDto,
  FileSpaceListingDto,
  FileSpaceListQuery,
  FileSpaceSearchQuery,
  FileSpaceEntryDto,
  RecycleBinEntryDto,
  MoveToRecycleBinDto,
  RecycleBinRestoreResult,
} from './types';

// Aligned with DefaultStorageController [Route("files")]
const BASE = '/files';
// Aligned with DefaultStoragePreviewController [Route("files/preview")]
const PREVIEW_BASE = '/files/preview';
// Aligned with DefaultStorageAdminController [Route("admin/files")]
const ADMIN_BASE = '/admin/files';
// Aligned with DefaultFileFolderAdminController [Route("admin/storage/folders")]
const ADMIN_FOLDER_BASE = '/admin/storage/folders';
// Aligned with DefaultFileFolderController [Route("storage/folders")]
const FOLDER_BASE = '/storage/folders';
// Aligned with DefaultFileSpaceController [Route("storage/space")]
const SPACE_BASE = '/storage/space';
// Aligned with DefaultRecycleBinController [Route("storage/recycle-bin")]
const RECYCLE_BIN_BASE = '/storage/recycle-bin';
// Aligned with DefaultStorageQuotaAdminController [Route("admin/storage/quotas")]
const ADMIN_QUOTA_BASE = '/admin/storage/quotas';

/**
 * Accept either the new options object or a bare progress callback, so existing
 * `upload(file, onProgress)` call sites keep compiling.
 */
function normalizeUploadOptions(
  options?: FileUploadOptions | ((progress: number) => void),
): FileUploadOptions {
  return typeof options === 'function' ? { onProgress: options } : (options ?? {});
}

/**
 * File Storage API (User)
 * Aligned with DefaultStorageController
 */
export function useStorageApi(client: HttpClient) {
  return {
    // ---- Core file operations ----

    /** Get file by ID */
    getById: (id: string) =>
      client.get<FileRecordDto>(`${BASE}/${id}`),

    /** Delete file */
    delete: (id: string) =>
      client.delete<void>(`${BASE}/${id}`),

    /**
     * Upload file. Backend `DefaultStorageController.Upload` returns the full file record.
     *
     * Pass `{ isPublic: true }` for site assets that must be reachable through an
     * anonymous `<img src>`. Avatars do not need it - the backend flags the file
     * public by itself once the id lands in a `[FileField(Public = true)]` field.
     *
     * Pass `{ isTemporary: false }` for a file that is kept without any entity
     * referencing it (a file library entry); see `FileUploadOptions.isTemporary`.
     */
    upload: (file: File, options?: FileUploadOptions | ((progress: number) => void)) => {
      const { onProgress, isPublic, isTemporary } = normalizeUploadOptions(options);
      const additionalData: Record<string, string> = {};
      if (isPublic) additionalData.isPublic = 'true';
      if (isTemporary !== undefined) additionalData.isTemporary = String(isTemporary);
      return client.upload<FileRecordDto>(`${BASE}/upload`, file, {
        onProgress,
        additionalData: Object.keys(additionalData).length > 0 ? additionalData : undefined,
      });
    },

    /** Batch upload files */
    uploadMany: (files: File[], options?: FileUploadOptions | ((progress: number) => void)) => {
      const { onProgress, isPublic } = normalizeUploadOptions(options);
      const formData = new FormData();
      for (const file of files) {
        formData.append('files', file);
      }
      if (isPublic) formData.append('isPublic', 'true');
      return client.uploadFormData<FileRecordDto[]>(`${BASE}/upload/batch`, formData, { onProgress });
    },

    /** Download file */
    download: (id: string) =>
      client.download(`${BASE}/${id}/download`),

    /** Get file preview (inline stream) */
    getPreviewUrl: (id: string) => client.resolveUrl(`${BASE}/${id}/preview`),

    /**
     * Resolve the direct download URL. Goes through `resolveUrl` so the path
     * is deployment-prefix aware (sub-app / IIS virtual dir) instead of a
     * hardcoded `/api/...` that breaks when the app is not mounted at the root.
     */
    getDownloadUrl: (id: string) => client.resolveUrl(`${BASE}/${id}/download`),

    /** Get thumbnail URL */
    getThumbnailUrl: (id: string) => client.resolveUrl(`${BASE}/${id}/thumbnail`),

    /**
     * Get the file's download route (`/api/files/{id}/download`). The URL is
     * neither signed nor expiring: every request is authorized again, so a
     * browser opening a private file needs a bearer token or an access token
     * from `getAccessToken` appended as `?sig=`. `expiresIn` is ignored by the
     * server. Use `getPresignedUrl` for a time-limited direct link.
     */
    getUrl: (id: string, expiresIn?: number) =>
      client.get<string>(`${BASE}/${id}/url`, { params: { expiresIn } }),

    /** Generate a presigned URL for temporary public access */
    getPresignedUrl: (id: string, expiresInSeconds: number = 3600) =>
      client.get<string>(`${BASE}/${id}/presigned-url`, { params: { expiresInSeconds } }),

    /**
     * Mint a short-lived token so the browser can fetch this private file
     * without an Authorization header (see `FileAccessTokenDto`).
     *
     * Prefer `createFileUrlResolver`, which batches and caches these for you -
     * call this directly only for one-off links.
     */
    getAccessToken: (id: string, expiresInSeconds?: number) =>
      client.get<FileAccessTokenDto>(`${BASE}/${id}/access-token`, {
        params: expiresInSeconds ? { expiresInSeconds } : undefined,
      }),

    /**
     * Mint access tokens for several files in one round trip. Files the caller
     * cannot read are omitted from the response rather than failing the batch,
     * so one unreadable id never blanks a whole page of images.
     */
    getAccessTokens: (fileIds: string[], expiresInSeconds?: number) =>
      client.post<FileAccessTokenDto[]>(`${BASE}/access-tokens`, { fileIds, expiresInSeconds }),

    /** Rename file */
    rename: (id: string, newFileName: string) =>
      client.put<FileRecordDto>(`${BASE}/${id}/rename`, { newFileName }),

    /** Copy file */
    copy: (id: string, newFileName?: string) =>
      client.post<FileRecordDto>(`${BASE}/${id}/copy`, { newFileName }),

    /**
     * The signed-in user's storage quota: bytes used and the allowance
     * (`allowanceBytes` null = unlimited). Uploads that would go over it are
     * refused with HTTP 413 and error code `FILE_QUOTA_EXCEEDED`, whose
     * `errorDetails` is a `StorageQuotaExceededDetails`.
     */
    getQuota: () =>
      client.get<StorageQuotaDto>(`${BASE}/quota`),

    // ---- Versioning ----

    /** Create a new file version */
    createVersion: (id: string, file: File, description?: string) =>
      client.upload<FileVersionDto>(`${BASE}/${id}/versions`, file, {
        additionalData: description ? { description } : undefined,
      }),

    /** Get file version history */
    getVersions: (id: string) =>
      client.get<FileVersionDto[]>(`${BASE}/${id}/versions`),

    /** Restore a specific version */
    restoreVersion: (id: string, version: number) =>
      client.post<FileRecordDto>(`${BASE}/${id}/versions/${version}/restore`),

    /**
     * Download one version's bytes as a Blob (the current version pointer does
     * not move). Save it under `versionedFileName(originalName, version)`.
     *
     * There is deliberately no URL helper for this route: it needs a signed-in
     * caller, and a browser navigation (`<a href>`, `window.open`) carries no
     * Authorization header - nor does a `?sig=` token get past that gate, since
     * the route is not anonymous. The bytes come through the client, with the
     * same 401 refresh as every other request.
     */
    downloadVersion: (id: string, version: number) =>
      client.download(`${BASE}/${id}/versions/${version}/download`),

    /**
     * Delete a version that is not current: its record and its stored copy.
     * The server refuses the current version (400) - restore another one first.
     */
    deleteVersion: (id: string, version: number) =>
      client.delete<void>(`${BASE}/${id}/versions/${version}`),

    // ---- Sharing ----

    /** Create a share link for a file */
    createShare: (id: string, data: CreateShareDto) =>
      client.post<FileShareDto>(`${BASE}/${id}/share`, data),

    /** Get share info by token (management view; requires a signed-in caller) */
    getShare: (token: string) =>
      client.get<FileShareDto>(`${BASE}/share/${token}`),

    /**
     * What a share-link RECIPIENT sees before downloading. Anonymous: the whole
     * point of an external share link is that the recipient has no account.
     *
     * Rejects with 404 for a link that is revoked, expired or exhausted -
     * indistinguishable from a token that never existed, so probing tells the
     * caller nothing.
     */
    getSharePreview: (token: string) =>
      client.get<FileSharePreviewDto>(`${BASE}/share/${token}/info`),

    /**
     * Check a share link password WITHOUT consuming an access. Anonymous.
     *
     * Probing the download endpoint instead would burn one unit of the link's
     * quota, so a `maxAccessCount: 1` link would be exhausted before the
     * recipient ever got the file. Wrong passwords still count towards the
     * link's auto-disable threshold.
     */
    verifyShareAccess: (token: string, password?: string) =>
      client.post<boolean>(`${BASE}/share/${token}/verify`, { password }),

    /**
     * Direct download URL for a share link. Anonymous, so it can go straight
     * into an `<a href>` - no Authorization header involved.
     */
    getShareDownloadUrl: (token: string, password?: string) =>
      client.resolveUrl(`${BASE}/share/${token}/download`, password ? { password } : undefined),

    /** Revoke (delete) a share by token */
    revokeShare: (token: string) =>
      client.delete<void>(`${BASE}/share/${token}`),

    /** Download file via share token */
    downloadByShareToken: (token: string, password?: string) =>
      client.download(`${BASE}/share/${token}/download`, { params: { password } }),

    // ---- Compression ----

    /** Compress multiple files into a zip */
    compress: (data: CompressDto) =>
      client.post<FileRecordDto>(`${BASE}/compress`, data),

    /** Decompress a zip file */
    decompress: (id: string) =>
      client.post<FileRecordDto[]>(`${BASE}/${id}/decompress`),

    // ---- Chunked upload ----

    /** Initiate chunked upload */
    initiateChunkedUpload: (data: InitiateChunkedUploadDto) =>
      client.post<FileUploadSessionDto>(`${BASE}/upload/chunk/init`, data),

    /**
     * Upload a single chunk.
     *
     * The part is named `chunk`, the parameter name the endpoint binds
     * (`IFormFile chunk`). `client.upload` names it `file`, which the endpoint
     * never sees, so every chunk would fail with 400 "Chunk file is required".
     */
    uploadChunk: (uploadSessionId: string, chunkIndex: number, chunk: Blob) => {
      const formData = new FormData();
      formData.append('chunk', chunk);
      return client.uploadFormData<FileChunkDto>(`${BASE}/upload/chunk/${uploadSessionId}`, formData, {
        params: { chunkIndex },
      });
    },

    /**
     * Complete chunked upload. Pass `{ folderId }` to put the merged file
     * straight into one of the uploader's folders (the user file space): it is
     * then kept permanently, like `useFileSpaceApi().upload`.
     */
    completeChunkedUpload: (
      uploadSessionId: string,
      dataOrIsTemporary: CompleteChunkedUploadDto | boolean = { isTemporary: false }
    ) =>
      client.post<FileRecordDto>(
        `${BASE}/upload/chunk/${uploadSessionId}/complete`,
        typeof dataOrIsTemporary === 'boolean'
          ? { isTemporary: dataOrIsTemporary }
          : {
              isTemporary: dataOrIsTemporary.isTemporary ?? false,
              ...(dataOrIsTemporary.folderId ? { folderId: dataOrIsTemporary.folderId } : {}),
            }
      ),

    /** Cancel chunked upload */
    cancelChunkedUpload: (uploadSessionId: string) =>
      client.delete<void>(`${BASE}/upload/chunk/${uploadSessionId}`),

    /** Get chunked upload progress */
    getUploadProgress: (uploadSessionId: string) =>
      client.get<FileUploadProgressDto>(`${BASE}/upload/chunk/${uploadSessionId}/progress`),
  };
}

/**
 * File Preview API (User)
 * Aligned with DefaultStoragePreviewController
 */
export function useStoragePreviewApi(client: HttpClient) {
  return {
    /** Check if a file supports preview */
    canPreview: (id: string) =>
      client.get<boolean>(`${PREVIEW_BASE}/${id}/can-preview`),

    /** Get preview URL for a file */
    getPreviewUrl: (id: string) =>
      client.get<string>(`${PREVIEW_BASE}/${id}/preview-url`),

    /** Get preview type (image, pdf, video, audio, text) */
    getPreviewType: (id: string) =>
      client.get<string>(`${PREVIEW_BASE}/${id}/preview-type`),

    /**
     * Download the generated preview (image / PDF / media / text) as a Blob.
     *
     * Fetched with the caller's session rather than returned as a URL: the endpoint
     * is authenticated and does not accept a `?sig=` token, so a bare URL in an
     * `<img>` / `<iframe>` is refused for every file. Show the Blob through
     * `URL.createObjectURL`, or sign a read URL with `createFileUrlResolver`.
     */
    getGeneratedPreview: (id: string) =>
      client.download(`${PREVIEW_BASE}/${id}/preview`),
  };
}

/**
 * Admin File Management API
 * Aligned with DefaultStorageAdminController
 */
export function useAdminFileApi(client: HttpClient) {
  return {
    // ---- Query & Statistics ----

    /** Query files (admin) */
    query: (data: FileQueryDto) =>
      client.post<PagedList<FileRecordDto>>(`${ADMIN_BASE}/query`, data),

    /** Get file statistics */
    getStatistics: () =>
      client.get<FileStorageStatisticsDto>(`${ADMIN_BASE}/statistics`),

    /**
     * Draw thumbnails for stored files that have none: bitmaps, and the first
     * page of PDFs when the host loads Tnzi.Documents. Idempotent, one batch per
     * call; pass `nextCursor` back as `after` until it is null (see ThumbnailBackfillResult).
     */
    backfillThumbnails: (data?: ThumbnailBackfillRequest) =>
      client.post<ThumbnailBackfillResult>(`${ADMIN_BASE}/backfill-thumbnails`, data ?? {}),

    /** Batch delete files */
    batchDelete: (ids: string[]) =>
      client.delete<void>(`${ADMIN_BASE}/batch`, { body: ids }),

    // ---- Temporary files ----

    /** Cleanup temporary files */
    cleanupTemporary: (olderThanHours?: number) =>
      client.post<number>(`${ADMIN_BASE}/cleanup-temporary`, null, {
        params: { olderThanHours },
      }),

    /** Get temporary files */
    getTemporaryFiles: (olderThanHours?: number) =>
      client.get<FileRecordDto[]>(`${ADMIN_BASE}/temporary`, { params: { olderThanHours } }),

    // ---- References ----

    /** Get references for a file */
    getReferences: (id: string) =>
      client.get<FileReferenceDto[]>(`${ADMIN_BASE}/${id}/references`),

    /** Get references by entity */
    getReferencesByEntity: (entityType: string, entityId: string) =>
      client.get<FileReferenceDto[]>(`${ADMIN_BASE}/references`, { params: { entityType, entityId } }),

    /** Get reference statistics */
    getReferenceStatistics: (entityType?: string) =>
      client.get<FileReferenceStatisticsDto>(`${ADMIN_BASE}/references/statistics`, { params: { entityType } }),

    /**
     * Rebuild the reference count of a **temporary** file from its reference rows.
     * Permanent uploads (and MD5-reused files) carry implicit holders that are not
     * recorded as reference rows, so the server refuses them with 400.
     */
    syncReferenceCount: (id: string) =>
      client.post<number>(`${ADMIN_BASE}/${id}/sync-reference-count`),

    /**
     * Rebuild the reference counts of **all temporary files** from their reference rows.
     * Returns the number of records changed; permanent records are never touched.
     */
    syncAllReferenceCounts: () =>
      client.post<number>(`${ADMIN_BASE}/sync-all-reference-counts`),

    /**
     * Validate a file's reference count: temporary files must match their reference rows exactly,
     * permanent files must be at least the reference row count.
     */
    validateReferenceCount: (id: string) =>
      client.get<boolean>(`${ADMIN_BASE}/${id}/validate-reference-count`),

    /** Batch confirm references */
    batchConfirmReferences: (references: FileReferenceInfoDto[]) =>
      client.post<void>(`${ADMIN_BASE}/references/batch-confirm`, references),

    /** Batch update references for an entity */
    batchUpdateReferences: (entityType: string, entityId: string, data: Record<string, string[]>) =>
      client.put<void>(`${ADMIN_BASE}/references/batch-update`, data, {
        params: { entityType, entityId },
      }),

    // ---- User storage usage ----

    /** Get storage usage for a specific user */
    getUserStorageUsage: (userId: string) =>
      client.get<UserStorageUsageDto>(`${ADMIN_BASE}/usage/user/${userId}`),

    /**
     * A user's storage quota: bytes used (files plus old versions) and the
     * effective allowance. Set a per-user allowance with `useAdminStorageQuotaApi`.
     */
    getUserQuota: (userId: string) =>
      client.get<StorageQuotaDto>(`${ADMIN_BASE}/quota/${userId}`),

    /** Get top users by storage usage */
    getTopUsersByStorage: (top: number = 20) =>
      client.get<UserStorageUsageDto[]>(`${ADMIN_BASE}/usage/top-users`, { params: { top } }),

    // ---- Presigned URLs (admin) ----

    /**
     * Generate a presigned download URL (admin). Read credential only: the server
     * fixes the verb to GET, so there is no `httpMethod` parameter any more - a
     * direct-upload (PUT) URL is a write credential and is not issued by this endpoint.
     */
    getPresignedUrl: (id: string, expiresInSeconds: number = 3600) =>
      client.get<string>(`${ADMIN_BASE}/${id}/presigned-url`, { params: { expiresInSeconds } }),

    // ---- Integrity verification ----

    /** Verify integrity of a single file */
    verifyFileIntegrity: (id: string) =>
      client.get<FileIntegrityResultDto>(`${ADMIN_BASE}/${id}/verify-integrity`),

    /**
     * Batch verify file integrity: `maxFiles` (1-500) files after `after`, by
     * file id. Pass the result's `nextCursor` back as `after` to continue.
     */
    batchVerifyIntegrity: (maxFiles: number = 100, after?: string | null) =>
      client.post<BatchIntegrityResultDto>(`${ADMIN_BASE}/verify-integrity`, null, {
        params: after ? { maxFiles, after } : { maxFiles },
      }),

    // ---- Share management ----

    /** Get all shares for a file */
    getSharesByFile: (id: string) =>
      client.get<FileShareSummaryDto[]>(`${ADMIN_BASE}/${id}/shares`),

    /** Query active shares with paging */
    queryActiveShares: (data: ActiveSharesQueryDto) =>
      client.post<PagedList<FileShareSummaryDto>>(`${ADMIN_BASE}/shares/query`, data),

    /** Batch revoke shares */
    batchRevokeShares: (shareIds: string[]) =>
      client.post<number>(`${ADMIN_BASE}/shares/batch-revoke`, shareIds),

    // ---- Tags ----

    /** Set tags for a file */
    setFileTags: (id: string, data: SetFileTagsDto) =>
      client.put<FileRecordDto>(`${ADMIN_BASE}/${id}/tags`, data),

    /**
     * Get files by tag. The tag is a path segment, so it is percent-encoded; ASP.NET Core
     * does not decode an encoded `/` in a route value, so for a tag that contains one use
     * `query({ tag })`, which filters on the same exact match.
     */
    getFilesByTag: (tag: string, pageIndex: number = 1, pageSize: number = 20) =>
      client.get<PagedList<FileRecordDto>>(`${ADMIN_BASE}/by-tag/${encodeURIComponent(tag)}`, { params: { pageIndex, pageSize } }),

    // ---- Metadata ----

    /** Get metadata (key/value map) for a file */
    getMetadata: (id: string) =>
      client.get<Record<string, string>>(`${ADMIN_BASE}/${id}/metadata`),

    /** Set (replace) metadata for a file */
    setMetadata: (id: string, metadata: Record<string, string>) =>
      client.put<FileRecordDto>(`${ADMIN_BASE}/${id}/metadata`, { metadata }),

    // ---- Visibility ----

    /**
     * Make a file publicly readable, or take that back. Public means readable by
     * anyone including unauthenticated callers - right for avatars and site
     * assets, wrong for contracts, cheques and HR documents.
     */
    setFileVisibility: (id: string, data: SetFileVisibilityDto) =>
      client.put<FileRecordDto>(`${ADMIN_BASE}/${id}/visibility`, data),

    /**
     * Backfill the public flag from the backend's `[FileField(Public = true)]`
     * declarations: every file referenced by a field declared public becomes
     * publicly readable. Returns the number of files changed.
     *
     * This is the one-shot repair for data written before those declarations
     * existed (avatars uploaded earlier stay private and render as 404).
     * Idempotent, and it never turns a file back into a private one.
     */
    syncPublicFlags: () =>
      client.post<number>(`${ADMIN_BASE}/sync-public-flags`),
  };
}

/**
 * Admin File Folder API
 * Aligned with DefaultFileFolderAdminController under /admin/storage/folders.
 * The folder tree drives the master pane of the StorageFile browser; the
 * move endpoints let admins reorganize files in bulk.
 */
export function useAdminFileFolderApi(client: HttpClient) {
  return {
    /**
     * Fetch the folder tree rooted at `parentId` (omit for full tree from root).
     * Every user has their own namespace; pass `ownerId` to see one user's tree,
     * otherwise the tree mixes every owner's folders.
     */
    getTree: (parentId?: string | null, ownerId?: string | null) => {
      const params: Record<string, string> = {};
      if (parentId) params.parentId = parentId;
      if (ownerId) params.ownerId = ownerId;
      return client.get<FileFolderDto[]>(`${ADMIN_FOLDER_BASE}/tree`, {
        params: Object.keys(params).length > 0 ? params : undefined,
      });
    },

    /** Lookup a single folder by id. */
    getById: (id: string) =>
      client.get<FileFolderDto>(`${ADMIN_FOLDER_BASE}/${id}`),

    /** Create a folder under an optional parent. */
    create: (data: CreateFileFolderDto) =>
      client.post<FileFolderDto>(ADMIN_FOLDER_BASE, data),

    /** Rename / re-describe / re-sort an existing folder. */
    update: (id: string, data: UpdateFileFolderDto) =>
      client.put<FileFolderDto>(`${ADMIN_FOLDER_BASE}/${id}`, data),

    /** Delete an (empty) folder. */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_FOLDER_BASE}/${id}`),

    /** Move a folder under a new parent (or to root when `newParentId` is null). */
    move: (id: string, newParentId?: string | null) =>
      client.post<void>(`${ADMIN_FOLDER_BASE}/${id}/move`, null, {
        params: newParentId ? { newParentId } : undefined,
      }),

    /** Bulk-move files into a target folder (or root when folderId is null). */
    moveFiles: (data: MoveFilesToFolderRequest) =>
      client.post<void>(`${ADMIN_FOLDER_BASE}/move-files`, data),

    /**
     * Reorder folders inside one parent (drag-and-drop).
     * `ids` is the visible order; folders outside those slots keep their positions.
     */
    reorder: (data: ReorderRequest, parentId?: string | null) =>
      client.post<void>(`${ADMIN_FOLDER_BASE}/reorder`, data, {
        params: parentId ? { parentId } : undefined,
      }),
  };
}

// ---------------------------------------------------------------------------
// Admin storage audit - read-only views over chunk rows and file versions,
// served by Tnzi.Storage.Workspace. Chunks are owned by the upload lifecycle
// (initiate / upload / complete / abort) and a version is restored through the
// user-facing `useStorageApi().restoreVersion`, so neither has a write here.
// ---------------------------------------------------------------------------

const ADMIN_STORAGE_AUDIT_BASE = '/admin/storage/audit';

export function useAdminStorageAuditApi(client: HttpClient) {
  return {
    /** Paged chunk rows, optionally narrowed to one upload session. */
    getChunks: (query?: FileChunkAuditQueryDto) =>
      client.get<PagedList<FileChunkAuditDto>>(`${ADMIN_STORAGE_AUDIT_BASE}/chunks`, { params: query }),
    /** Paged version rows, optionally narrowed to one file and/or current-only. */
    getVersions: (query?: FileVersionAuditQueryDto) =>
      client.get<PagedList<FileVersionAuditDto>>(`${ADMIN_STORAGE_AUDIT_BASE}/versions`, { params: query }),
  };
}

/**
 * The signed-in user's own folders. Aligned with DefaultFileFolderController
 * under /storage/folders. Paths are unique per owner; another user's folders
 * are never visible here.
 *
 * `delete` removes an EMPTY folder permanently. A file space UI deletes through
 * `useRecycleBinApi().moveToRecycleBin` instead, which also takes non-empty
 * folders (with their contents) and can be undone.
 */
export function useFileFolderApi(client: HttpClient) {
  return {
    /** The signed-in user's folder tree below `parentId` (omit for the whole tree). */
    getTree: (parentId?: string | null) =>
      client.get<FileFolderDto[]>(`${FOLDER_BASE}/tree`, {
        params: parentId ? { parentId } : undefined,
      }),

    getById: (id: string) =>
      client.get<FileFolderDto>(`${FOLDER_BASE}/${id}`),

    create: (data: CreateFileFolderDto) =>
      client.post<FileFolderDto>(FOLDER_BASE, data),

    update: (id: string, data: UpdateFileFolderDto) =>
      client.put<FileFolderDto>(`${FOLDER_BASE}/${id}`, data),

    /** Permanently delete an empty folder (see the note on this factory). */
    delete: (id: string) =>
      client.delete<void>(`${FOLDER_BASE}/${id}`),

    /** Move a folder under another of the user's folders (or to the root). */
    move: (id: string, newParentId?: string | null) =>
      client.post<void>(`${FOLDER_BASE}/${id}/move`, null, {
        params: newParentId ? { newParentId } : undefined,
      }),

    /** Put files into a folder (or the root when folderId is null). */
    moveFiles: (data: MoveFilesToFolderRequest) =>
      client.post<void>(`${FOLDER_BASE}/move-files`, data),
  };
}

/**
 * The signed-in user's file space: browse a folder, search by name, upload into
 * a folder. Aligned with DefaultFileSpaceController under /storage/space.
 *
 * Files are opened with the regular file endpoints (`useStorageApi()` download /
 * preview / thumbnail / versions / share) using the entry id.
 *
 * Uploads land as permanent files and are never merged with an earlier upload of
 * the same bytes. Files larger than `Storage:MaxFileSize` go through the chunked
 * upload (`useStorageApi()` initiate / upload chunk / complete with `{ folderId }`).
 */
export function useFileSpaceApi(client: HttpClient) {
  return {
    /** Folders first, then files, paged together. Omit `folderId` for the root. */
    list: (query: FileSpaceListQuery = {}) =>
      client.get<FileSpaceListingDto>(`${SPACE_BASE}/items`, { params: query }),

    /** Search the user's files by name, extension or content type (recycle bin excluded). */
    search: (query: FileSpaceSearchQuery) =>
      client.get<PagedList<FileSpaceEntryDto>>(`${SPACE_BASE}/search`, { params: query }),

    /** Upload a file into a folder (omit `folderId` for the root). */
    upload: (file: File, folderId?: string | null, onProgress?: (progress: number) => void) =>
      client.upload<FileRecordDto>(`${SPACE_BASE}/files`, file, {
        onProgress,
        additionalData: folderId ? { folderId } : undefined,
      }),
  };
}

/**
 * The signed-in user's recycle bin. Aligned with DefaultRecycleBinController
 * under /storage/recycle-bin. Items are purged automatically after
 * `Storage:RecycleBin:RetentionDays` and count against the quota until then.
 */
export function useRecycleBinApi(client: HttpClient) {
  return {
    /** Most recently deleted first. */
    list: (pageIndex: number = 1, pageSize: number = 50) =>
      client.get<PagedList<RecycleBinEntryDto>>(RECYCLE_BIN_BASE, { params: { pageIndex, pageSize } }),

    /** Move files and folders (with everything in them) to the bin. */
    moveToRecycleBin: (data: MoveToRecycleBinDto) =>
      client.post<void>(RECYCLE_BIN_BASE, data),

    /** Restore entries to where they were deleted from (or the root when that folder is gone). */
    restore: (entryIds: string[]) =>
      client.post<RecycleBinRestoreResult>(`${RECYCLE_BIN_BASE}/restore`, { entryIds }),

    /** Delete entries permanently; returns how many were processed. */
    purge: (entryIds: string[]) =>
      client.post<number>(`${RECYCLE_BIN_BASE}/purge`, { entryIds }),

    /** Empty the bin; returns how many entries were purged. */
    empty: () =>
      client.delete<number>(RECYCLE_BIN_BASE),
  };
}

/**
 * Per-user storage allowances set by an administrator (override
 * `Storage:Quota:DefaultUserAllowanceBytes`). Aligned with
 * DefaultStorageQuotaAdminController under /admin/storage/quotas
 * (`storage.file.view`; writes need `storage.file.update`).
 * A user's usage and effective allowance: `useAdminFileApi().getUserQuota`.
 */
export function useAdminStorageQuotaApi(client: HttpClient) {
  return {
    /** Users who have an allowance of their own. */
    list: (pageIndex: number = 1, pageSize: number = 20) =>
      client.get<PagedList<StorageAllowanceDto>>(ADMIN_QUOTA_BASE, { params: { pageIndex, pageSize } }),

    /** A user's own allowance; 404 when the default applies. */
    get: (userId: string) =>
      client.get<StorageAllowanceDto>(`${ADMIN_QUOTA_BASE}/${userId}`),

    /** Set a user's allowance (`allowanceBytes` null = unlimited for this user). */
    set: (userId: string, data: SetStorageAllowanceDto) =>
      client.put<StorageAllowanceDto>(`${ADMIN_QUOTA_BASE}/${userId}`, data),

    /** Remove a user's allowance so the default applies again. */
    remove: (userId: string) =>
      client.delete<void>(`${ADMIN_QUOTA_BASE}/${userId}`),
  };
}
