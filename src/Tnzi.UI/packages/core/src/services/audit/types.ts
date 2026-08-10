/**
 * Audit Module Types - Audit logging and operation tracking
 * Aligned with Tnzi.NET backend Tnzi.Audit module DTOs
 */

import type { PagedQueryDto } from '../../types/pagination';
import { AuditResultType, AuditTrendGroupBy, EntityChangeType } from './metadata';

export { AuditResultType, AuditTrendGroupBy, EntityChangeType };

// ============================================
// Audit Operation Types (backend: AuditDtos.cs)
// ============================================

/**
 * Audit operation DTO (maps to backend AuditOperationDto)
 */
export interface AuditOperationDto {
  id: string;
  functionName?: string | null;
  permissionName?: string | null;
  userId?: string | null;
  userName?: string | null;
  nickName?: string | null;
  ip?: string | null;
  operatingSystem?: string | null;
  browser?: string | null;
  httpMethod?: string | null;
  url?: string | null;
  httpStatusCode?: number | null;
  elapsed: number;
  startTime: Date | string;
  endTime?: Date | string | null;
  resultType: AuditResultType;
  message?: string | null;
  exception?: string | null;
  requestParameters?: string | null;
  responseResult?: string | null;
  creationTime: Date | string;
  entityEntries: AuditEntityEntryDto[];
}

/**
 * Audit entity entry DTO (maps to backend AuditEntityEntryDto)
 */
export interface AuditEntityEntryDto {
  id: string;
  entityTypeName?: string | null;
  entityTypeFullName?: string | null;
  entityId?: string | null;
  operationType: EntityChangeType;
  creationTime: Date | string;
  propertyEntries: AuditPropertyEntryDto[];
}

/**
 * Audit property entry DTO (maps to backend AuditPropertyEntryDto)
 */
export interface AuditPropertyEntryDto {
  id: string;
  propertyName?: string | null;
  propertyDisplayName?: string | null;
  propertyTypeName?: string | null;
  originalValue?: string | null;
  newValue?: string | null;
}

/**
 * Audit operation query parameters (maps to backend AuditOperationQueryDto)
 */
export interface AuditOperationQueryDto extends PagedQueryDto {
  functionName?: string;
  permissionName?: string;
  userId?: string;
  resultType?: AuditResultType;
  startDate?: Date | string;
  endDate?: Date | string;
  ip?: string;
  /** Exact HTTP method filter (e.g. 'GET' / 'POST', case-insensitive). */
  httpMethod?: string;
  /**
   * true = write operations only (POST/PUT/PATCH/DELETE - the "business
   * operations" view); false = read requests only (GET/HEAD/OPTIONS etc.);
   * omitted = all requests (request-level audit log view).
   */
  isWriteOperation?: boolean;
}

// ============================================
// Audit Statistics Types (backend: AuditDtos.cs)
// ============================================

/**
 * Audit operation statistics (maps to backend AuditOperationStatistics)
 */
export interface AuditOperationStatisticsDto {
  totalCount: number;
  successCount: number;
  failedCount: number;
  warningCount: number;
  averageElapsed: number;
  maxElapsed: number;
  minElapsed: number;
}

/**
 * Audit trend data point (maps to backend AuditTrendPointDto)
 */
export interface AuditTrendPointDto {
  period: string;
  totalCount: number;
  successCount: number;
  failedCount: number;
  warningCount: number;
  averageElapsed: number;
}

/**
 * Top function statistics (maps to backend TopFunctionDto)
 */
export interface TopFunctionDto {
  functionName: string;
  hitCount: number;
  averageElapsed: number;
  maxElapsed: number;
  errorCount: number;
  errorRate: number;
}

/**
 * Top user statistics (maps to backend TopUserDto)
 */
export interface TopUserDto {
  userId: string;
  userName?: string | null;
  operationCount: number;
  successCount: number;
  failedCount: number;
  successRate: number;
}

// ============================================
// Record-level read audit (optional capability)
// ============================================

/**
 * One record-level read: who opened WHICH row.
 *
 * Distinct from AuditOperationDto, which answers "who called which endpoint".
 * Privacy questions are usually about the former ("who viewed this informant's
 * file last month") and endpoint logs cannot answer them.
 */
export interface RecordAccessDto {
  id: string;
  /** Position in this user's tamper-evident chain. */
  sequence: number;
  resourceType: string;
  resourceId: string;
  purpose?: string | null;
  userId?: string | null;
  /** Denormalised so a later rename does not erase who it was at the time. */
  userName?: string | null;
  hash: string;
  creationTime: string;
}

export interface RecordAccessQueryDto {
  pageIndex?: number;
  pageSize?: number;
  resourceType?: string;
  resourceId?: string;
  userId?: string;
  purpose?: string;
  startTime?: Date | string;
  endTime?: Date | string;
}

/**
 * Read volume per user.
 *
 * The per-hour quota is the *preventive* gate; this is the *retrospective*
 * view - an account reading ten times its usual volume without crossing the
 * quota is only visible when the volumes sit side by side.
 */
export interface RecordAccessUserStatDto {
  userId?: string | null;
  userName?: string | null;
  accessCount: number;
  /** Distinct records touched, so re-reading one row does not look like breadth. */
  distinctRecordCount: number;
  lastAccessTime: string;
}

// ============================================
// Policy-driven data destruction (optional capability)
// ============================================

/** A destruction certificate: what a retention policy destroyed, and when. */
export interface DataDestructionDto {
  id: string;
  /** Position in the global tamper-evident chain. */
  sequence: number;
  policyName: string;
  entityType: string;
  /** Records with a timestamp older than this were treated as expired. */
  cutoff: string;
  destroyedCount: number;
  /** Expired but NOT destroyed because a litigation hold applied. */
  heldCount: number;
  /** Digest of the destroyed identifiers - never the data itself. */
  identifierDigest: string;
  /** Present only when the backend is configured to keep the list. */
  identifiers?: string | null;
  mode: string;
  encryptionKeyId?: string | null;
  /** Whether that key is confirmed absent from the key ring. */
  isKeyDestroyed: boolean;
  isDryRun: boolean;
  executedByUserId?: string | null;
  hash: string;
  creationTime: string;
}

export interface DataDestructionQueryDto {
  pageIndex?: number;
  pageSize?: number;
  policyName?: string;
  startTime?: Date | string;
  endTime?: Date | string;
  isDryRun?: boolean;
}

/** Per-policy outcome of one destruction cycle. */
export interface DataDestructionPolicyResultDto {
  policyName: string;
  entityType: string;
  cutoff: string;
  destroyedCount: number;
  heldCount: number;
  /** True when the batch cap was hit - more expired data remains. */
  hasMore: boolean;
  certificateId?: string | null;
  error?: string | null;
}

export interface DataDestructionRunDto {
  policies: DataDestructionPolicyResultDto[];
  totalDestroyed: number;
  totalHeld: number;
  isDryRun: boolean;
}

// ============================================
// Legacy type aliases (deprecated)
// ============================================

/** @deprecated Use AuditOperationDto instead */
export type AuditLogDto = AuditOperationDto;

/** @deprecated Use AuditOperationQueryDto instead */
export type AuditLogQueryDto = AuditOperationQueryDto;

/** @deprecated Use AuditOperationStatisticsDto instead */
export type AuditStatisticsDto = AuditOperationStatisticsDto;
