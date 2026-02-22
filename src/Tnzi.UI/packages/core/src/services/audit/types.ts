/**
 * Audit Module Types - Audit logging and operation tracking
 * Aligned with Tnzi.NET backend Audit module
 */

import type { AuditedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import { AuditResultType, EntityChangeType } from './metadata';

export { AuditResultType, EntityChangeType };

// ============================================
// Audit Log Types
// ============================================

/**
 * Audit log DTO
 */
export interface AuditLogDto extends AuditedEntity<string> {
  functionName: string;
  functionDisplayName?: string | null;
  permissionName?: string | null;
  userId?: string | null;
  userName?: string | null;
  operationTime: Date | string;
  resultType: AuditResultType;
  exceptionMessage?: string | null;
  exceptionStackTrace?: string | null;
  parameters?: string | null;
  returnValue?: string | null;
  executionDuration: number;
  ipAddress?: string | null;
  userAgent?: string | null;
  browserInfo?: string | null;
  httpMethod?: string | null;
  httpPath?: string | null;
  httpQueryString?: string | null;
  httpStatusCode?: number | null;
  correlationId?: string | null;
  tenantId?: string | null;
}

/**
 * Audit operation DTO (aggregated view)
 */
export interface AuditOperationDto {
  functionName: string;
  functionDisplayName?: string | null;
  permissionName?: string | null;
  totalCalls: number;
  successCount: number;
  failedCount: number;
  averageDuration: number;
  maxDuration: number;
  minDuration: number;
  lastExecutedAt: Date | string;
}

/**
 * Audit log query parameters
 */
export interface AuditLogQueryDto extends SortedPagedQueryDto {
  functionName?: string;
  permissionName?: string;
  userId?: string;
  userName?: string;
  resultType?: AuditResultType;
  startDate?: Date | string;
  endDate?: Date | string;
  ipAddress?: string;
  httpMethod?: string;
  httpPath?: string;
  minDuration?: number;
  maxDuration?: number;
  httpStatusCode?: number;
  correlationId?: string;
}

// ============================================
// Audit Entity Change Types
// ============================================

/**
 * Entity change DTO
 */
export interface EntityChangeDto {
  id: string;
  auditLogId: string;
  entityType: string;
  entityId: string;
  changeType: EntityChangeType;
  changeTime: Date | string;
  userId?: string | null;
  userName?: string | null;
  propertyChanges: PropertyChangeDto[];
}

/**
 * Property change DTO
 */
export interface PropertyChangeDto {
  propertyName: string;
  propertyType: string;
  oldValue?: string | null;
  newValue?: string | null;
}

// ============================================
// Audit Statistics Types
// ============================================

/**
 * Audit statistics
 */
export interface AuditStatisticsDto {
  totalOperations: number;
  successRate: number;
  averageDuration: number;
  byResultType: Record<AuditResultType, number>;
  byHour: HourlyStatistics[];
  topOperations: OperationStatistics[];
  topUsers: UserStatistics[];
  topErrors: ErrorStatistics[];
}

/**
 * Hourly statistics
 */
export interface HourlyStatistics {
  hour: number;
  count: number;
  avgDuration: number;
  errorCount: number;
}

/**
 * Operation statistics
 */
export interface OperationStatistics {
  functionName: string;
  displayName?: string | null;
  count: number;
  avgDuration: number;
  errorRate: number;
}

/**
 * User statistics
 */
export interface UserStatistics {
  userId: string;
  userName?: string | null;
  operationCount: number;
  avgDuration: number;
  errorCount: number;
}

/**
 * Error statistics
 */
export interface ErrorStatistics {
  functionName: string;
  exceptionType?: string | null;
  message: string;
  count: number;
  lastOccurrence: Date | string;
}

// ============================================
// Audit Configuration Types
// ============================================

/**
 * Audit configuration
 */
export interface AuditConfigDto {
  isEnabled: boolean;
  logAllOperations: boolean;
  logParameters: boolean;
  logReturnValue: boolean;
  logEntityChanges: boolean;
  maxParameterLength: number;
  maxReturnValueLength: number;
  ignoredFunctions: string[];
  ignoredEntities: string[];
  retentionDays: number;
}

// ============================================
// Audit Export Types
// ============================================

/**
 * Export audit logs request
 */
export interface ExportAuditLogsDto {
  query: AuditLogQueryDto;
  format: 'csv' | 'excel' | 'json';
  includeEntityChanges: boolean;
  includeStackTrace: boolean;
}

/**
 * Export result
 */
export interface AuditExportResultDto {
  id: string;
  status: 'pending' | 'processing' | 'completed' | 'failed';
  downloadUrl?: string | null;
  expiresAt?: Date | string | null;
  error?: string | null;
}
