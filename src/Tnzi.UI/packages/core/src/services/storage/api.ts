/**
 * Storage Module API - File storage, versioning, sharing, and admin operations
 * Aligned with backend DefaultStorageController, DefaultStoragePreviewController,
 * and DefaultStorageAdminController
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  FileRecordDto,
  FileQueryDto,
  FileStorageStatisticsDto,
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
} from './types';

// Aligned with DefaultStorageController [Route("files")]
const BASE = '/files';
// Aligned with DefaultStoragePreviewController [Route("files/preview")]
const PREVIEW_BASE = '/files/preview';
// Aligned with DefaultStorageAdminController [Route("admin/files")]
const ADMIN_BASE = '/admin/files';
// Aligned with DefaultFileFolderAdminController [Route("admin/storage/folders")]
const ADMIN_FOLDER_BASE = '/admin/storage/folders';

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
     */
    upload: (file: File, options?: FileUploadOptions | ((progress: number) => void)) => {
      const { onProgress, isPublic } = normalizeUploadOptions(options);
      return client.upload<FileRecordDto>(`${BASE}/upload`, file, {
        onProgress,
        additionalData: isPublic ? { isPublic: 'true' } : undefined,
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

    /** Get file URL (signed/expirable) */
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

    /** Upload a single chunk */
    uploadChunk: (uploadSessionId: string, chunkIndex: number, chunk: File) =>
      client.upload<FileChunkDto>(`${BASE}/upload/chunk/${uploadSessionId}`, chunk, { params: { chunkIndex } }),

    /** Complete chunked upload */
    completeChunkedUpload: (
      uploadSessionId: string,
      dataOrIsTemporary: CompleteChunkedUploadDto | boolean = { isTemporary: false }
    ) =>
      client.post<FileRecordDto>(`${BASE}/upload/chunk/${uploadSessionId}/complete`, {
        isTemporary:
          typeof dataOrIsTemporary === 'boolean'
            ? dataOrIsTemporary
            : dataOrIsTemporary.isTemporary ?? false,
      }),

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

    /** Get generated preview stream URL */
    getGeneratedPreviewUrl: (id: string) =>
      client.resolveUrl(`${PREVIEW_BASE}/${id}/preview`),
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

    /** Sync reference count for a file */
    syncReferenceCount: (id: string) =>
      client.post<number>(`${ADMIN_BASE}/${id}/sync-reference-count`),

    /** Sync all reference counts */
    syncAllReferenceCounts: () =>
      client.post<number>(`${ADMIN_BASE}/sync-all-reference-counts`),

    /** Validate reference count for a file */
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

    /** Get top users by storage usage */
    getTopUsersByStorage: (top: number = 20) =>
      client.get<UserStorageUsageDto[]>(`${ADMIN_BASE}/usage/top-users`, { params: { top } }),

    // ---- Presigned URLs (admin) ----

    /** Generate a presigned URL (admin, supports httpMethod parameter) */
    getPresignedUrl: (id: string, expiresInSeconds: number = 3600, httpMethod: string = 'GET') =>
      client.get<string>(`${ADMIN_BASE}/${id}/presigned-url`, { params: { expiresInSeconds, httpMethod } }),

    // ---- Integrity verification ----

    /** Verify integrity of a single file */
    verifyFileIntegrity: (id: string) =>
      client.get<FileIntegrityResultDto>(`${ADMIN_BASE}/${id}/verify-integrity`),

    /** Batch verify file integrity */
    batchVerifyIntegrity: (maxFiles: number = 100) =>
      client.post<BatchIntegrityResultDto>(`${ADMIN_BASE}/verify-integrity`, null, { params: { maxFiles } }),

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

    /** Get files by tag */
    getFilesByTag: (tag: string, pageIndex: number = 1, pageSize: number = 20) =>
      client.get<PagedList<FileRecordDto>>(`${ADMIN_BASE}/by-tag/${tag}`, { params: { pageIndex, pageSize } }),

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
    /** Fetch the folder tree rooted at `parentId` (omit for full tree from root). */
    getTree: (parentId?: string | null) =>
      client.get<FileFolderDto[]>(`${ADMIN_FOLDER_BASE}/tree`, {
        params: parentId ? { parentId } : undefined,
      }),

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
  };
}
