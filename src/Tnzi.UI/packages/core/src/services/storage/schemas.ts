/**
 * Storage Module Zod Schemas
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
