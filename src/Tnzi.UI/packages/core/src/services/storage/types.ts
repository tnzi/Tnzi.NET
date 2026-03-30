/**
 * Storage Module Types - File storage and management
 * Aligned with Tnzi.NET backend Storage module
 */

import type { CreationAuditedEntity } from '../../types/entities';
import type { PagedQueryDto, SortedPagedQueryDto } from '../../types/pagination';

// ============================================
// File Types
// ============================================

/**
 * File record DTO
 */
export interface FileRecordDto extends CreationAuditedEntity<string> {
  fileName: string;
  originalName: string;
  extension: string;
  contentType: string;
  size: number;
  path?: string | null;
  md5Hash?: string | null;
  provider: string;
  referenceCount: number;
  thumbnailPath?: string | null;
  creatorId?: string | null;
  creatorName?: string | null;
  url: string;
  thumbnailUrl?: string | null;
}

/**
 * File query request
 * Aligned with backend FileQueryRequest
 */
export interface FileQueryDto extends SortedPagedQueryDto {
  extension?: string;
  contentType?: string;
  minSize?: number;
  maxSize?: number;
  startTime?: Date | string;
  endTime?: Date | string;
  creatorId?: string;
  provider?: string;
  originalName?: string;
  tag?: string;
}

/**
 * File upload result
 */
export interface FileUploadResultDto {
  id: string;
  fileName: string;
  originalName: string;
  url: string;
  size: number;
  contentType: string;
}

/**
 * Chunk upload request (for large files)
 */
export interface ChunkUploadDto {
  fileId?: string | null;
  fileName: string;
  totalSize: number;
  chunkSize: number;
  chunkIndex: number;
  totalChunks: number;
  chunkData: Blob;
  md5Hash?: string;
}

/**
 * Initiate chunked upload request
 * Aligned with backend InitiateChunkedUploadRequest
 */
export interface InitiateChunkedUploadDto {
  fileName: string;
  totalSize: number;
  chunkSize: number;
  md5Hash?: string;
}

/**
 * Complete chunked upload request
 * Aligned with backend CompleteChunkedUploadRequest
 */
export interface CompleteChunkedUploadDto {
  isTemporary?: boolean;
}

/**
 * Chunk upload result
 */
export interface ChunkUploadResultDto {
  fileId: string;
  chunkIndex: number;
  uploadedChunks: number;
  totalChunks: number;
  isComplete: boolean;
  file?: FileUploadResultDto;
}

/**
 * Upload session DTO
 * Aligned with backend FileUploadSession entity
 */
export interface FileUploadSessionDto {
  id: string;
  fileName: string;
  totalSize: number;
  chunkSize: number;
  totalChunks: number;
  uploadedChunks: number;
  uploadedSize: number;
  md5Hash?: string | null;
  isCompleted: boolean;
  isCancelled: boolean;
  completedTime?: Date | string | null;
  creationTime: Date | string;
  creatorId?: string | null;
  expiresAt: Date | string;
}

/**
 * File chunk DTO
 * Aligned with backend FileChunk entity
 */
export interface FileChunkDto {
  id: string;
  uploadSessionId: string;
  chunkIndex: number;
  chunkSize: number;
  chunkPath?: string | null;
  md5Hash?: string | null;
  creationTime: Date | string;
}

/**
 * File upload progress
 * Aligned with backend FileUploadProgress DTO
 */
export interface FileUploadProgressDto {
  uploadSessionId: string;
  fileName: string;
  totalSize: number;
  uploadedSize: number;
  totalChunks: number;
  uploadedChunks: number;
  progressPercentage: number;
  isCompleted: boolean;
  isCancelled: boolean;
}

// ============================================
// File Version Types
// ============================================

/**
 * File version DTO
 * Aligned with backend FileVersion entity
 */
export interface FileVersionDto {
  id: string;
  fileId: string;
  version: number;
  path: string;
  size: number;
  md5Hash?: string | null;
  description?: string | null;
  isCurrent: boolean;
  creationTime: Date | string;
  creatorId?: string | null;
}

// ============================================
// File Share Types
// ============================================

/**
 * File share DTO
 * Aligned with backend FileShare entity
 */
export interface FileShareDto {
  id: string;
  fileId: string;
  shareToken: string;
  expiresAt?: Date | string | null;
  accessCount: number;
  maxAccessCount?: number | null;
  requirePassword: boolean;
  isEnabled: boolean;
  creationTime: Date | string;
  creatorId?: string | null;
}

/**
 * Create share request
 * Aligned with backend CreateShareRequest
 */
export interface CreateShareDto {
  expiresAt?: Date | string | null;
  maxAccessCount?: number | null;
  password?: string | null;
}

/**
 * File share summary DTO (for admin listing)
 * Aligned with backend FileShareSummaryDto
 */
export interface FileShareSummaryDto {
  id: string;
  fileId: string;
  originalName: string;
  shareToken: string;
  expiresAt?: Date | string | null;
  accessCount: number;
  maxAccessCount?: number | null;
  requirePassword: boolean;
  isEnabled: boolean;
  isExpired: boolean;
  isExhausted: boolean;
  creationTime: Date | string;
  creatorId?: string | null;
}

/**
 * Active shares query request
 * Aligned with backend ActiveSharesQueryRequest
 */
export interface ActiveSharesQueryDto extends PagedQueryDto {
  fileId?: string;
  creatorId?: string;
  includeExpired?: boolean;
  includeDisabled?: boolean;
}

// ============================================
// File Compression Types
// ============================================

/**
 * Compress files request
 * Aligned with backend CompressRequest
 */
export interface CompressDto {
  fileIds: string[];
  zipFileName?: string | null;
}

// ============================================
// Storage Statistics
// ============================================

/**
 * File type statistics
 */
export interface FileTypeStatisticsDto {
  count: number;
  size?: number;
  totalSize?: number;
  extensions?: Record<string, number>;
}

/**
 * Storage statistics
 */
export interface StorageStatisticsDto {
  totalFiles: number;
  totalSize: number;
  filesByType: Record<string, FileTypeStatisticsDto>;
  filesByProvider?: Record<string, number>;
  sizeByProvider?: Record<string, number>;
}

/**
 * Backend alias (FileStorageStatistics)
 */
export type FileStorageStatisticsDto = StorageStatisticsDto;

/**
 * User storage usage statistics
 * Aligned with backend UserStorageUsage DTO
 */
export interface UserStorageUsageDto {
  userId?: string | null;
  fileCount: number;
  totalSize: number;
  formattedSize: string;
}

// ============================================
// File Integrity Types
// ============================================

/**
 * File integrity status
 * Aligned with backend FileIntegrityStatus enum
 */
export type FileIntegrityStatus = 'Healthy' | 'Missing' | 'Corrupted' | 'Error';

/**
 * File integrity verification result
 * Aligned with backend FileIntegrityResult DTO
 */
export interface FileIntegrityResultDto {
  fileId: string;
  originalName: string;
  physicalFileExists: boolean;
  md5Matches?: boolean | null;
  expectedMd5?: string | null;
  actualMd5?: string | null;
  status: FileIntegrityStatus;
  error?: string | null;
}

/**
 * Batch integrity verification result
 * Aligned with backend BatchIntegrityResult DTO
 */
export interface BatchIntegrityResultDto {
  totalChecked: number;
  healthy: number;
  missing: number;
  corrupted: number;
  errors: number;
  problems: FileIntegrityResultDto[];
}

// ============================================
// File Tags Types
// ============================================

/**
 * Set file tags request
 * Aligned with backend SetFileTagsRequest
 */
export interface SetFileTagsDto {
  tags: string[];
}

// ============================================
// Provider Types
// ============================================

/**
 * Storage provider info
 */
export interface StorageProviderDto {
  name: string;
  type: string;
  isEnabled: boolean;
  isDefault: boolean;
  maxFileSize: number;
  allowedExtensions: string[];
  baseUrl?: string | null;
}

/**
 * Presigned URL request
 */
export interface PresignedUrlRequestDto {
  fileName: string;
  contentType: string;
  size: number;
  expiresIn?: number;
}

/**
 * Presigned URL response
 */
export interface PresignedUrlResponseDto {
  uploadUrl: string;
  fileKey: string;
  expiresAt: Date | string;
  headers?: Record<string, string>;
}

// ============================================
// Folder Types (if supported)
// ============================================

/**
 * Folder DTO
 */
export interface FolderDto {
  id: string;
  name: string;
  path: string;
  parentId?: string | null;
  fileCount: number;
  totalSize: number;
  creationTime: Date | string;
  children?: FolderDto[];
}

/**
 * Create folder request
 */
export interface CreateFolderDto {
  name: string;
  parentId?: string | null;
}

// ============================================
// File Reference Types
// ============================================

/**
 * File reference DTO (full)
 * Aligned with backend FileReferenceDto
 */
export interface FileReferenceDto {
  id: string;
  fileId: string;
  entityType: string;
  entityId: string;
  fieldName: string;
  isTemporary: boolean;
  creationTime: Date | string;
}

/**
 * File reference info (compact, for batch operations)
 * Aligned with backend FileReferenceInfo
 */
export interface FileReferenceInfoDto {
  fileId: string;
  entityType: string;
  entityId: string;
  fieldName: string;
}

/**
 * File reference statistics
 * Aligned with backend FileReferenceStatistics DTO
 */
export interface FileReferenceStatisticsDto {
  totalReferences: number;
  permanentReferences: number;
  temporaryReferences: number;
  referencesByEntityType: Record<string, number>;
}

/**
 * Bulk file operation request
 */
export interface BulkFileOperationDto {
  fileIds: string[];
  operation: 'delete' | 'move' | 'copy';
  targetFolderId?: string;
}
