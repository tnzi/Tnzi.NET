/**
 * Storage Module API - File storage and folder operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  FileRecordDto,
  FileQueryDto,
  FileUploadResultDto,
  FileStorageStatisticsDto,
  FileUploadSessionDto,
  FileChunkDto,
  InitiateChunkedUploadDto,
  CompleteChunkedUploadDto,
} from './types';

// Aligned with DefaultFileStorageController [Route("files")]
const BASE = '/files';
// Aligned with DefaultAdminStorageController [Route("admin/files")]
const ADMIN_BASE = '/admin/files';

/**
 * File Storage API (User)
 */
export function useStorageApi(client: HttpClient) {
  return {
    /** Get file by ID */
    getById: (id: string) =>
      client.get<FileRecordDto>(`${BASE}/${id}`),

    /** Upload file */
    upload: (file: File, onProgress?: (progress: number) => void) =>
      client.upload<FileUploadResultDto>(`${BASE}/upload`, file, { onProgress }),

    /** Download file */
    download: (id: string) =>
      client.download(`${BASE}/${id}/download`),

    /** Get file preview URL */
    getPreviewUrl: (id: string) => client.resolveUrl(`${BASE}/${id}/preview`),

    /** Get thumbnail URL */
    getThumbnailUrl: (id: string) => client.resolveUrl(`${BASE}/${id}/thumbnail`),

    /** Get file URL (signed/expirable) */
    getUrl: (id: string, expiresIn?: number) =>
      client.get<string>(`${BASE}/${id}/url`, { params: { expiresIn } }),

    /** Rename file */
    rename: (id: string, newName: string) =>
      client.put<FileRecordDto>(`${BASE}/${id}/rename`, { newFileName: newName }),

    /** Copy file */
    copy: (id: string, newName?: string) =>
      client.post<FileRecordDto>(`${BASE}/${id}/copy`, { newFileName: newName }),

    /** Initiate chunked upload */
    initiateChunkedUpload: (data: InitiateChunkedUploadDto) =>
      client.post<FileUploadSessionDto>(`${BASE}/upload/chunk/init`, data),

    /** Upload chunk */
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
  };
}

/**
 * Admin File Management API
 */
export function useAdminFileApi(client: HttpClient) {
  return {
    /** Query files (admin) */
    query: (data: FileQueryDto) =>
      client.post<PagedList<FileRecordDto>>(`${ADMIN_BASE}/query`, data),

    /** Get file statistics */
    getStatistics: () =>
      client.get<FileStorageStatisticsDto>(`${ADMIN_BASE}/statistics`),

    /** Batch delete files */
    batchDelete: (ids: string[]) =>
      client.delete<void>(`${ADMIN_BASE}/batch`, { body: ids }),

    /** Cleanup temporary files */
    cleanupTemporary: (olderThanHours?: number) =>
      client.post<number>(`${ADMIN_BASE}/cleanup-temporary`, null, {
        params: { olderThanHours },
      }),

    /** Get temporary files */
    getTemporaryFiles: (olderThanHours?: number) =>
      client.get<FileRecordDto[]>(`${ADMIN_BASE}/temporary`, { params: { olderThanHours } }),

    /** Sync reference count */
    syncReferenceCount: (id: string) =>
      client.post<number>(`${ADMIN_BASE}/${id}/sync-reference-count`),

    /** Sync all reference counts */
    syncAllReferenceCounts: () =>
      client.post<number>(`${ADMIN_BASE}/sync-all-reference-counts`),
  };
}
