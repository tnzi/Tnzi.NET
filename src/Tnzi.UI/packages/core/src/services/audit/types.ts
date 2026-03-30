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
// Legacy type aliases (deprecated)
// ============================================

/** @deprecated Use AuditOperationDto instead */
export type AuditLogDto = AuditOperationDto;

/** @deprecated Use AuditOperationQueryDto instead */
export type AuditLogQueryDto = AuditOperationQueryDto;

/** @deprecated Use AuditOperationStatisticsDto instead */
export type AuditStatisticsDto = AuditOperationStatisticsDto;
