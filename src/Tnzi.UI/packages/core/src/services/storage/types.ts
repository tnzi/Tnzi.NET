/**
 * Storage Module Types - File storage and management
 * Aligned with Tnzi.NET backend Storage module
 */

import type { CreationAuditedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';

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
 */
export interface InitiateChunkedUploadDto {
  fileName: string;
  totalSize: number;
  chunkSize: number;
  md5Hash?: string;
}

/**
 * Complete chunked upload request
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
 * File reference info (for tracking file usage)
 */
export interface FileReferenceDto {
  id: string;
  fileId: string;
  entityType: string;
  entityId: string;
  propertyName: string;
  creationTime: Date | string;
}

/**
 * Bulk file operation request
 */
export interface BulkFileOperationDto {
  fileIds: string[];
  operation: 'delete' | 'move' | 'copy';
  targetFolderId?: string;
}
