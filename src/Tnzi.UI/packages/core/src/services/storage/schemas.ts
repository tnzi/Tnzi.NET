/**
 * Storage Module Zod Schemas
 * Aligned with Tnzi.NET backend Storage module DTOs
 */

import { z } from 'zod';

// ============================================
// File Schemas
// ============================================

export const fileRecordDtoSchema = z.object({
  id: z.string(),
  fileName: z.string().min(1),
  originalName: z.string().min(1),
  extension: z.string(),
  contentType: z.string(),
  size: z.number().int().nonnegative(),
  path: z.string().nullable().optional(),
  md5Hash: z.string().nullable().optional(),
  provider: z.string(),
  referenceCount: z.number().int().nonnegative(),
  thumbnailPath: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  creatorId: z.string().nullable().optional(),
  creatorName: z.string().nullable().optional(),
  url: z.string().url(),
  thumbnailUrl: z.string().url().nullable().optional(),
});

export const fileQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  extension: z.string().optional(),
  contentType: z.string().optional(),
  minSize: z.number().int().nonnegative().optional(),
  maxSize: z.number().int().positive().optional(),
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
  creatorId: z.string().optional(),
  provider: z.string().optional(),
  originalName: z.string().optional(),
  tag: z.string().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

export const fileUploadResultSchema = z.object({
  id: z.string(),
  fileName: z.string(),
  originalName: z.string(),
  url: z.string().url(),
  size: z.number().int().nonnegative(),
  contentType: z.string(),
});

export const chunkUploadSchema = z.object({
  fileId: z.string().nullable().optional(),
  fileName: z.string().min(1),
  totalSize: z.number().int().positive(),
  chunkSize: z.number().int().positive(),
  chunkIndex: z.number().int().nonnegative(),
  totalChunks: z.number().int().positive(),
  md5Hash: z.string().optional(),
});

export const initiateChunkedUploadSchema = z.object({
  fileName: z.string().min(1),
  totalSize: z.number().int().positive(),
  chunkSize: z.number().int().positive(),
  md5Hash: z.string().optional(),
});

export const completeChunkedUploadSchema = z.object({
  isTemporary: z.boolean().optional(),
});

export const fileUploadSessionSchema = z.object({
  id: z.string(),
  fileName: z.string().min(1),
  totalSize: z.number().int().nonnegative(),
  chunkSize: z.number().int().positive(),
  totalChunks: z.number().int().nonnegative(),
  uploadedChunks: z.number().int().nonnegative(),
  uploadedSize: z.number().int().nonnegative(),
  md5Hash: z.string().nullable().optional(),
  isCompleted: z.boolean(),
  isCancelled: z.boolean(),
  completedTime: z.union([z.date(), z.string()]).nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  creatorId: z.string().nullable().optional(),
  expiresAt: z.union([z.date(), z.string()]),
});

export const fileChunkSchema = z.object({
  id: z.string(),
  uploadSessionId: z.string(),
  chunkIndex: z.number().int().nonnegative(),
  chunkSize: z.number().int().nonnegative(),
  chunkPath: z.string().nullable().optional(),
  md5Hash: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
});

export const fileUploadProgressSchema = z.object({
  uploadSessionId: z.string(),
  fileName: z.string(),
  totalSize: z.number().int().nonnegative(),
  uploadedSize: z.number().int().nonnegative(),
  totalChunks: z.number().int().nonnegative(),
  uploadedChunks: z.number().int().nonnegative(),
  progressPercentage: z.number().min(0).max(100),
  isCompleted: z.boolean(),
  isCancelled: z.boolean(),
});

// ============================================
// File Version Schemas
// ============================================

export const fileVersionSchema = z.object({
  id: z.string(),
  fileId: z.string(),
  version: z.number().int().positive(),
  path: z.string(),
  size: z.number().int().nonnegative(),
  md5Hash: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  isCurrent: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
  creatorId: z.string().nullable().optional(),
});

// ============================================
// File Share Schemas
// ============================================

export const fileShareSchema = z.object({
  id: z.string(),
  fileId: z.string(),
  shareToken: z.string(),
  expiresAt: z.union([z.date(), z.string()]).nullable().optional(),
  accessCount: z.number().int().nonnegative(),
  maxAccessCount: z.number().int().positive().nullable().optional(),
  requirePassword: z.boolean(),
  isEnabled: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
  creatorId: z.string().nullable().optional(),
});

export const createShareSchema = z.object({
  expiresAt: z.union([z.date(), z.string()]).nullable().optional(),
  maxAccessCount: z.number().int().positive().nullable().optional(),
  password: z.string().max(128).nullable().optional(),
});

export const fileShareSummarySchema = z.object({
  id: z.string(),
  fileId: z.string(),
  originalName: z.string(),
  shareToken: z.string(),
  expiresAt: z.union([z.date(), z.string()]).nullable().optional(),
  accessCount: z.number().int().nonnegative(),
  maxAccessCount: z.number().int().positive().nullable().optional(),
  requirePassword: z.boolean(),
  isEnabled: z.boolean(),
  isExpired: z.boolean(),
  isExhausted: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
  creatorId: z.string().nullable().optional(),
});

export const activeSharesQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(20),
  fileId: z.string().optional(),
  creatorId: z.string().optional(),
  includeExpired: z.boolean().optional(),
  includeDisabled: z.boolean().optional(),
});

// ============================================
// Compression Schemas
// ============================================

export const compressSchema = z.object({
  fileIds: z.array(z.string()).min(1),
  zipFileName: z.string().max(256).nullable().optional(),
});

// ============================================
// Statistics Schemas
// ============================================

export const fileTypeStatisticsSchema = z.object({
  count: z.number().int().nonnegative(),
  size: z.number().nonnegative().optional(),
  totalSize: z.number().nonnegative().optional(),
  extensions: z.record(z.number()).optional(),
});

export const storageStatisticsSchema = z.object({
  totalFiles: z.number().int().nonnegative(),
  totalSize: z.number().nonnegative(),
  filesByType: z.record(fileTypeStatisticsSchema),
  filesByProvider: z.record(z.number()).optional(),
  sizeByProvider: z.record(z.number()).optional(),
});

export const userStorageUsageSchema = z.object({
  userId: z.string().nullable().optional(),
  fileCount: z.number().int().nonnegative(),
  totalSize: z.number().nonnegative(),
  formattedSize: z.string(),
});

// ============================================
// Integrity Schemas
// ============================================

export const fileIntegrityStatusSchema = z.enum(['Healthy', 'Missing', 'Corrupted', 'Error']);

export const fileIntegrityResultSchema = z.object({
  fileId: z.string(),
  originalName: z.string(),
  physicalFileExists: z.boolean(),
  md5Matches: z.boolean().nullable().optional(),
  expectedMd5: z.string().nullable().optional(),
  actualMd5: z.string().nullable().optional(),
  status: fileIntegrityStatusSchema,
  error: z.string().nullable().optional(),
});

export const batchIntegrityResultSchema = z.object({
  totalChecked: z.number().int().nonnegative(),
  healthy: z.number().int().nonnegative(),
  missing: z.number().int().nonnegative(),
  corrupted: z.number().int().nonnegative(),
  errors: z.number().int().nonnegative(),
  problems: z.array(fileIntegrityResultSchema),
});

// ============================================
// Tags Schemas
// ============================================

export const setFileTagsSchema = z.object({
  tags: z.array(z.string()),
});

// ============================================
// Reference Schemas
// ============================================

export const fileReferenceSchema = z.object({
  id: z.string(),
  fileId: z.string(),
  entityType: z.string(),
  entityId: z.string(),
  fieldName: z.string(),
  isTemporary: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
});

export const fileReferenceInfoSchema = z.object({
  fileId: z.string(),
  entityType: z.string(),
  entityId: z.string(),
  fieldName: z.string(),
});

export const fileReferenceStatisticsSchema = z.object({
  totalReferences: z.number().int().nonnegative(),
  permanentReferences: z.number().int().nonnegative(),
  temporaryReferences: z.number().int().nonnegative(),
  referencesByEntityType: z.record(z.number()),
});

// ============================================
// Provider Schemas
// ============================================

export const storageProviderSchema = z.object({
  name: z.string(),
  type: z.string(),
  isEnabled: z.boolean(),
  isDefault: z.boolean(),
  maxFileSize: z.number().int().positive(),
  allowedExtensions: z.array(z.string()),
  baseUrl: z.string().url().nullable().optional(),
});

export const presignedUrlRequestSchema = z.object({
  fileName: z.string().min(1),
  contentType: z.string(),
  size: z.number().int().positive(),
  expiresIn: z.number().int().positive().optional(),
});

export const presignedUrlResponseSchema = z.object({
  uploadUrl: z.string().url(),
  fileKey: z.string(),
  expiresAt: z.union([z.date(), z.string()]),
  headers: z.record(z.string()).optional(),
});

// ============================================
// Folder Schemas
// ============================================

export const folderDtoSchema: z.ZodTypeAny = z.object({
  id: z.string(),
  name: z.string().min(1),
  path: z.string(),
  parentId: z.string().nullable().optional(),
  fileCount: z.number().int().nonnegative(),
  totalSize: z.number().nonnegative(),
  creationTime: z.union([z.date(), z.string()]),
  children: z.array(z.lazy(() => folderDtoSchema)).optional(),
});

export const createFolderSchema = z.object({
  name: z.string().min(1).max(100),
  parentId: z.string().nullable().optional(),
});
