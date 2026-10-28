/**
 * Storage Module Types - File storage and management
 * Aligned with Tnzi.NET backend Storage module
 */

import type { CreationAuditedEntity } from '../../types/entities';
import type { PagedList, PagedQueryDto } from '../../types/pagination';

// ============================================
// File Types
// ============================================

/**
 * File record DTO - the single response shape every file endpoint returns
 * (upload included). Mirrors `Tnzi.Storage/Dtos/FileRecordDto.cs`, which is a
 * deliberately narrow safe subset: it carries NO url / thumbnailUrl. Build
 * content URLs from the id via `useStorageApi().getPreviewUrl(id)` /
 * `getDownloadUrl(id)` / `getThumbnailUrl(id)`, which resolve through the
 * client baseUrl and stay deployment-prefix aware.
 */
export interface FileRecordDto extends CreationAuditedEntity<string> {
  fileName: string;
  originalName: string;
  extension: string;
  contentType: string;
  size: number;
  /**
   * @deprecated 后端 DefaultStorageController 已把对外契约收窄为安全 DTO,不再返回该内部存储路径字段。
   * 保留为可选仅为向后兼容(消费者早已容错);勿在新代码中依赖。
   */
  path?: string | null;
  /**
   * @deprecated 后端 DefaultStorageController 已把对外契约收窄为安全 DTO,不再返回该 MD5 校验字段。
   * 保留为可选仅为向后兼容;勿在新代码中依赖。
   */
  md5Hash?: string | null;
  provider: string;
  referenceCount: number;
  /**
   * @deprecated 后端 DefaultStorageController 已把对外契约收窄为安全 DTO,不再返回该内部缩略图路径字段。
   * 保留为可选仅为向后兼容;勿在新代码中依赖。
   */
  thumbnailPath?: string | null;
  /** The uploader's user id. The DTO carries no name; resolve one through the user API if needed. */
  creatorId?: string | null;
  /** Owning folder id; null = root / unfiled. */
  folderId?: string | null;
  /** True while the file is an unreferenced upload awaiting cleanup. */
  isTemporary: boolean;
  /**
   * True when the file is readable by anyone, including unauthenticated
   * callers - i.e. it can be rendered with a plain `<img src>` that carries no
   * Authorization header. Avatars and site assets are public; everything else
   * defaults to false and is readable only by its owner or an admin holding
   * `storage.file.view`.
   *
   * The backend sets this automatically whenever a file id is written into an
   * entity field declared `[FileField(Public = true)]` (user avatars, group
   * avatars), so avatar uploads do NOT need to pass `isPublic` themselves.
   */
  isPublic: boolean;
  /**
   * Custom tags. The backend stores + serializes these as a single
   * comma-separated string (e.g. `"invoice,2026,paid"`), not an array - split
   * on `,` (trimming + dropping blanks) to read individual tags. Null / empty
   * when the file has no tags.
   */
  tags?: string | null;
}

/**
 * File query request
 * Aligned with backend FileQueryRequest
 */
export interface FileQueryDto extends PagedQueryDto {
  /**
   * `CreationTime`, `Size` or `OriginalName` (case-insensitive). Anything else, or
   * nothing, sorts newest first.
   */
  sortBy?: string;
  /**
   * Sort direction, `'asc'` or `'desc'`. Wins over {@link descending}. This
   * endpoint does not read the `sortDescending` name other lists use.
   */
  sortOrder?: 'asc' | 'desc';
  /** Descending when true; used only when {@link sortOrder} is unset. Default false. */
  descending?: boolean;
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
  /**
   * Folder filter. Set to a folder id to list its direct children only.
   * Used together with {@link includeUnfiled}=false to scope to a specific folder;
   * set {@link includeUnfiled}=true to scope to the root (FolderId IS NULL).
   * Leaving both undefined keeps the legacy "no folder filter" behaviour.
   */
  folderId?: string;
  /** Show only files not in any folder (root). Set with `folderId` undefined. */
  includeUnfiled?: boolean;
}

/**
 * Folder DTO - output of /admin/storage/folders/tree.
 * `children` is populated by the tree endpoint; flat lookup endpoints leave it undefined.
 */
export interface FileFolderDto {
  id: string;
  name: string;
  parentId?: string | null;
  /** Hierarchical path like "/root/sub1/sub2". */
  path: string;
  sortOrder: number;
  description?: string | null;
  /**
   * The user whose file space this folder belongs to (null for folders created
   * without a signed-in user). Paths are unique per owner: two users can both
   * have `/Documents`.
   */
  ownerId?: string | null;
  /** Files directly under this folder (excluding sub-folder contents and files in the recycle bin). */
  fileCount: number;
  /** Populated by the tree endpoint; undefined on flat lookups. */
  children?: FileFolderDto[] | null;
}

export interface CreateFileFolderDto {
  name: string;
  parentId?: string | null;
  description?: string | null;
  sortOrder?: number;
}

export interface UpdateFileFolderDto {
  name?: string | null;
  description?: string | null;
  sortOrder?: number | null;
}

export interface MoveFilesToFolderRequest {
  fileIds: string[];
  /** Target folder id; null moves files to the root (unfiled). */
  folderId?: string | null;
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
  /**
   * Put the merged file straight into one of the uploader's own folders (the
   * user file space; needs Tnzi.Storage.Workspace). Cannot be combined with
   * `isTemporary: true`.
   */
  folderId?: string | null;
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
  /** @deprecated 后端对外契约已收窄, createVersion/getVersions 不再返回原始存储路径(内部字段)。 */
  path?: string | null;
  size: number;
  /** @deprecated 后端对外契约已收窄, createVersion/getVersions 不再返回 md5Hash(内部字段, 由审计端点提供)。 */
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
  /** When the link was last used successfully (null = never used). */
  lastAccessedAt?: Date | string | null;
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
 * Body of `POST /admin/files/backfill-thumbnails`. Mirrors
 * `Tnzi.Storage/Dtos/StorageDtos.cs` ThumbnailBackfillRequest. Both fields are
 * optional: an empty body scans every file that has no thumbnail, 100 at a time.
 */
export interface ThumbnailBackfillRequest {
  /** Only these files; null / omitted = every candidate. */
  fileIds?: string[] | null;
  /** Files handled per call (rendering is serial). 1-500, default 100. */
  maxFiles?: number;
  /** Resume after this file id: the previous batch's `nextCursor`. Omit to start over. */
  after?: string | null;
}

/**
 * Result of one backfill batch. Pass `nextCursor` back as `after` and loop
 * until it is null. Candidates are walked by file id, so a file that cannot be
 * drawn (an encrypted PDF) is reported in `failedFileIds` once per walk and no
 * longer holds back the files behind it.
 */
export interface ThumbnailBackfillResult {
  scanned: number;
  generated: number;
  failed: number;
  remaining: number;
  failedFileIds: string[];
  /** Where the next batch starts; null when the walk is complete. */
  nextCursor?: string | null;
}

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
// Storage quota
// ============================================

/**
 * A user's storage quota. Aligned with backend StorageQuotaDto.
 *
 * `usedBytes` is what the quota is judged against: the user's file records
 * (temporary uploads and files in the recycle bin included) plus what
 * sub-modules keep for them (old file versions). It can differ from
 * `UserStorageUsageDto.totalSize`, which counts file records only.
 */
export interface StorageQuotaDto {
  userId: string;
  fileCount: number;
  usedBytes: number;
  /** null = unlimited */
  allowanceBytes?: number | null;
  /** null when unlimited; 0 when already over */
  remainingBytes?: number | null;
  /** True when usage is above the allowance (after an administrator lowered it). */
  isExceeded: boolean;
}

/**
 * `errorDetails` of a refused write (error code `FILE_QUOTA_EXCEEDED`, HTTP 413).
 * Aligned with backend StorageQuotaExceededDetails.
 */
export interface StorageQuotaExceededDetails {
  usedBytes: number;
  requestedBytes: number;
  allowanceBytes: number;
}

/** Error code of a write refused because it would exceed the owner's quota. */
export const FILE_QUOTA_EXCEEDED_ERROR_CODE = 'FILE_QUOTA_EXCEEDED';

/** An administrator-set allowance for one user. Aligned with backend StorageAllowanceDto. */
export interface StorageAllowanceDto {
  userId: string;
  /** null = unlimited for this user */
  allowanceBytes?: number | null;
  note?: string | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Aligned with backend SetStorageAllowanceRequest. */
export interface SetStorageAllowanceDto {
  /** null = unlimited for this user; never negative */
  allowanceBytes?: number | null;
  note?: string | null;
}

// ============================================
// User file space
// ============================================

/** Mirrors backend FileSpaceEntryKind (member names on the wire). */
export enum FileSpaceEntryKind {
  Folder = 'Folder',
  File = 'File',
}

/**
 * Sort field of a file space listing or search. Mirrors backend FileSpaceSortField.
 * Folders always come before files; sorting by size orders folders by name.
 */
export enum FileSpaceSortField {
  Name = 'Name',
  Time = 'Time',
  Size = 'Size',
}

/** One folder or file in the signed-in user's file space. Aligned with backend FileSpaceEntryDto. */
export interface FileSpaceEntryDto {
  kind: FileSpaceEntryKind;
  /** Folder id or file id (a file id works with the `files/{id}/...` endpoints). */
  id: string;
  name: string;
  /** Containing folder; null = root */
  parentId?: string | null;
  /** Path of the containing folder ("/" for the root). */
  location: string;
  /** Bytes; 0 for folders */
  size: number;
  extension?: string | null;
  contentType?: string | null;
  hasThumbnail: boolean;
  isPublic: boolean;
  creationTime: string;
}

export interface FileSpaceBreadcrumbDto {
  id: string;
  name: string;
}

/** A folder's contents. Aligned with backend FileSpaceListingDto. */
export interface FileSpaceListingDto {
  /** null = root */
  folderId?: string | null;
  path: string;
  /** Root excluded; the listed folder is the last item. */
  breadcrumbs: FileSpaceBreadcrumbDto[];
  /** Folders first, then files, paged together. */
  entries: PagedList<FileSpaceEntryDto>;
}

export interface FileSpaceListQuery {
  /** Omit for the root */
  folderId?: string | null;
  sortBy?: FileSpaceSortField;
  descending?: boolean;
  pageIndex?: number;
  /** Default 50, at most 200 */
  pageSize?: number;
}

export interface FileSpaceSearchQuery {
  /** Part of the file name, case-insensitive */
  keyword?: string;
  /** For example ".pdf" (the dot is optional) */
  extension?: string;
  /** Prefix match: "image/" finds every image */
  contentType?: string;
  /** Default Time */
  sortBy?: FileSpaceSortField;
  /** Default true (newest first) */
  descending?: boolean;
  pageIndex?: number;
  pageSize?: number;
}

// ============================================
// Recycle bin
// ============================================

/** Mirrors backend RecycleBinItemType. */
export enum RecycleBinItemType {
  File = 'File',
  Folder = 'Folder',
}

/** One item in the signed-in user's recycle bin. Aligned with backend RecycleBinEntryDto. */
export interface RecycleBinEntryDto {
  /** Entry id: restore and purge take this, not the file or folder id. */
  id: string;
  itemType: RecycleBinItemType;
  itemId: string;
  name: string;
  /** Where it was deleted from ("/" for the root). */
  originalLocation: string;
  /** Bytes (for a folder, of every file in it). Counted against the quota until purged. */
  size: number;
  fileCount: number;
  extension?: string | null;
  contentType?: string | null;
  deletedAt: string;
  /** When it is purged automatically; null when automatic purging is off. */
  purgeAt?: string | null;
}

/** Aligned with backend MoveToRecycleBinRequest. */
export interface MoveToRecycleBinDto {
  fileIds?: string[];
  /** Folders go with everything in them. */
  folderIds?: string[];
}

/** Aligned with backend RecycleBinRestoreResult. */
export interface RecycleBinRestoreResult {
  restored: number;
  /** Entries whose original folder was gone, so they went back to the root. */
  restoredToRoot: string[];
  /** Folders renamed because their name was taken again: entry id -> new name. */
  renamed: Record<string, string>;
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
  /** Pass back as `after` for the next batch; null when every file has been checked. */
  nextCursor?: string | null;
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

/**
 * Set file visibility request
 * Aligned with backend SetFileVisibilityRequest
 */
export interface SetFileVisibilityDto {
  /**
   * True makes the file readable by anyone (including unauthenticated
   * callers); false restores the default owner/admin-only policy.
   */
  isPublic: boolean;
}

/**
 * What a share-link RECIPIENT is shown before downloading. Aligned with backend
 * `FileSharePreviewDto`.
 *
 * Deliberately narrower than `FileShareDto`: no file id (an anonymous visitor
 * has no business learning internal ids), no access counts, no creator.
 */
export interface FileSharePreviewDto {
  /** File name as uploaded, so the recipient can tell what they are about to open. */
  fileName: string;
  /** Size in bytes. */
  size: number;
  /** MIME type, for picking an icon. */
  contentType?: string | null;
  /** True when the link asks for a password before handing over the file. */
  requirePassword: boolean;
  /** ISO timestamp at which the link stops working (null = no expiry). */
  expiresAt?: string | null;
}

/**
 * A short-lived token that lets the browser fetch ONE private file without an
 * Authorization header. Aligned with backend `FileAccessTokenDto`.
 *
 * It exists because `<img>`, `<a download>`, `<video>` and `<iframe>` cannot
 * send a bearer token: without it, "private" would mean "unrenderable", even
 * for the person who uploaded the file. Append it to any read URL of that file
 * as the `sig` query parameter.
 *
 * Minting runs the full read check server-side, so holding a token proves the
 * minting session was allowed to read that one file - nothing more. It is not
 * an authorization credential: it never grants writes.
 */
export interface FileAccessTokenDto {
  /** File this token is valid for. It never works for another file. */
  fileId: string;
  /** Opaque signed token; pass it as the `sig` query parameter. */
  token: string;
  /** ISO timestamp at which the token stops working. */
  expiresAt: string;
}

/**
 * Options for a file upload.
 *
 * A bare progress callback is still accepted in the `options` position for
 * backwards compatibility - `upload(file, (p) => ...)` keeps working.
 */
export interface FileUploadOptions {
  /** Upload progress callback (0-100). */
  onProgress?: (progress: number) => void;
  /**
   * Mark the uploaded file publicly readable. Use it for site assets and other
   * resources consumed through an anonymous `<img src>`.
   *
   * NOT needed for avatars: writing the returned id into a
   * `[FileField(Public = true)]` field (user avatar, group avatar) makes the
   * backend flag the file public on its own.
   */
  isPublic?: boolean;
  /**
   * Lifetime of a single `upload` (not `uploadMany`, which always stores
   * permanent records). Omitted, the server stores a temporary upload: it is
   * held only by `[FileField]` references, so once background cleanup is on, an
   * upload no entity field references is removed after the retention period.
   * That suits "upload, then write the id into a record" (avatars, attachments).
   *
   * Pass `false` for a file that is uploaded to be kept as is, such as a file
   * library entry: the upload itself holds it until it is deleted.
   */
  isTemporary?: boolean;
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

// ---------------------------------------------------------------------------
// Admin storage audit (Tnzi.Storage.Workspace `DefaultStorageAuditAdminController`)
// ---------------------------------------------------------------------------

/** One uploaded chunk row as the admin audit view reports it. */
export interface FileChunkAuditDto {
  id: string;
  uploadSessionId: string;
  chunkIndex: number;
  chunkSize: number;
  md5Hash?: string | null;
  creationTime: string;
}

/** One file version row as the admin audit view reports it. */
export interface FileVersionAuditDto {
  id: string;
  fileId: string;
  version: number;
  path: string;
  size: number;
  md5Hash?: string | null;
  description?: string | null;
  isCurrent: boolean;
  creationTime: string;
  creatorId?: string | null;
}

export interface FileChunkAuditQueryDto {
  pageIndex?: number;
  pageSize?: number;
  uploadSessionId?: string;
}

export interface FileVersionAuditQueryDto {
  pageIndex?: number;
  pageSize?: number;
  fileId?: string;
  currentOnly?: boolean;
}
