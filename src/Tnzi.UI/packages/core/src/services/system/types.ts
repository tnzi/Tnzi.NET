/**
 * System Module Types - System configuration, menus, access logs
 * Aligned with Tnzi.NET backend System module
 */

import type { AuditedEntity, OrderedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import { HealthStatus } from './metadata';
import type { MenuBadgeType, FeatureRequirementType } from './metadata';

export { HealthStatus };
export type { MenuBadgeType, FeatureRequirementType };

// ============================================
// Menu Types
// ============================================

/**
 * Menu info DTO
 */
export interface MenuInfoDto extends OrderedEntity<string> {
  parentId?: string | null;
  name: string;
  displayName: string;
  icon?: string | null;
  path?: string | null;
  component?: string | null;
  redirect?: string | null;
  isHidden: boolean;
  isExternal: boolean;
  permission?: string | null;
  badge?: string | null;
  badgeType?: MenuBadgeType | null;
  query?: Record<string, string> | null;
  meta?: MenuMetaDto | null;
  children: MenuInfoDto[];
  creationTime: Date | string;
}

/**
 * Menu metadata
 */
export interface MenuMetaDto {
  title?: string;
  icon?: string;
  keepAlive?: boolean;
  hideMenu?: boolean;
  hideBreadcrumb?: boolean;
  hideChildrenInMenu?: boolean;
  currentActiveMenu?: string;
  ignoreRoute?: boolean;
  frameSrc?: string;
  frameBlank?: boolean;
}

/**
 * Create menu request
 */
export interface CreateMenuDto {
  parentId?: string;
  name: string;
  displayName: string;
  icon?: string;
  path?: string;
  component?: string;
  redirect?: string;
  sortOrder?: number;
  isHidden?: boolean;
  isExternal?: boolean;
  permission?: string;
  badge?: string;
  badgeType?: MenuBadgeType;
  query?: Record<string, string>;
  meta?: MenuMetaDto;
}

/**
 * Update menu request
 */
export interface UpdateMenuDto {
  parentId?: string;
  name?: string;
  displayName?: string;
  icon?: string;
  path?: string;
  component?: string;
  redirect?: string;
  sortOrder?: number;
  isHidden?: boolean;
  isExternal?: boolean;
  permission?: string;
  badge?: string;
  badgeType?: MenuBadgeType;
  query?: Record<string, string>;
  meta?: MenuMetaDto;
}

/**
 * Menu query parameters
 */
export interface MenuQueryDto {
  parentId?: string;
  isHidden?: boolean;
  permission?: string;
  keyword?: string;
}

// ============================================
// Access Log Types
// ============================================

/**
 * Access log info DTO
 */
export interface AccessLogInfoDto extends AuditedEntity<string> {
  userId?: string | null;
  userName?: string | null;
  path: string;
  method: string;
  ipAddress?: string | null;
  userAgent?: string | null;
  statusCode: number;
  responseTime: number;
  requestSize?: number | null;
  responseSize?: number | null;
  queryString?: string | null;
  requestBody?: string | null;
  responseBody?: string | null;
  exception?: string | null;
  correlationId?: string | null;
  // GeoIP information
  ipCountry?: string | null;
  ipProvince?: string | null;
  ipCity?: string | null;
  ipFullAddress?: string | null;
  ipLatitude?: number | null;
  ipLongitude?: number | null;
  // User agent parsed
  uaBrowser?: string | null;
  uaBrowserVersion?: string | null;
  uaOperatingSystem?: string | null;
  uaOsVersion?: string | null;
  uaDeviceType?: string | null;
  uaDeviceBrand?: string | null;
  uaDeviceModel?: string | null;
  uaIsMobile: boolean;
  uaIsBot: boolean;
  uaBotName?: string | null;
}

/**
 * Access log statistics DTO
 */
export interface AccessLogStatisticsDto {
  totalRequests: number;
  uniqueUsers: number;
  successRequests: number;
  errorRequests: number;
  averageResponseTime: number;
}

/**
 * Access log query parameters
 */
export interface AccessLogQueryDto extends SortedPagedQueryDto {
  userId?: string;
  userName?: string;
  path?: string;
  method?: string;
  ipAddress?: string;
  statusCode?: number;
  minResponseTime?: number;
  maxResponseTime?: number;
  startDate?: Date | string;
  endDate?: Date | string;
  startTime?: Date | string;
  endTime?: Date | string;
  isMobile?: boolean;
  isBot?: boolean;
  country?: string;
  correlationId?: string;
}

// ============================================
// System Settings Types
// ============================================

/**
 * System setting DTO
 */
export interface SystemSettingDto {
  key: string;
  value: string;
  description?: string | null;
  category: string;
  isPublic: boolean;
  lastModifiedAt: Date | string;
  lastModifiedBy?: string | null;
}

/**
 * Update setting request
 */
export interface UpdateSettingDto {
  value: string;
}

/**
 * Batch update settings request
 */
export interface BatchUpdateSettingsDto {
  settings: Record<string, string>;
}

/**
 * Setting query parameters
 */
export interface SettingQueryDto {
  category?: string;
  isPublic?: boolean;
  keyword?: string;
}

// ============================================
// System Info Types
// ============================================

/**
 * System information
 */
export interface SystemInfoDto {
  version: string;
  environment: string;
  machineName: string;
  osDescription: string;
  runtimeVersion: string;
  startTime: Date | string;
  uptime: string;
  uptimeSeconds: number;
}

/**
 * System health check result
 */
export interface HealthCheckResultDto {
  name: string;
  status: HealthStatus;
  description?: string | null;
  duration: number;
  data?: Record<string, unknown>;
  exception?: string | null;
}

/**
 * Overall health check response
 */
export interface HealthCheckResponseDto {
  status: HealthStatus;
  totalDuration: number;
  checks: HealthCheckResultDto[];
}

// ============================================
// Feature Flag Types
// ============================================

/**
 * Feature flag DTO
 */
export interface FeatureFlagDto {
  name: string;
  isEnabled: boolean;
  description?: string | null;
  requirements?: FeatureRequirementDto[];
}

/**
 * Feature requirement
 */
export interface FeatureRequirementDto {
  type: FeatureRequirementType;
  value: string;
}

// ============================================
// Maintenance Types
// ============================================

/**
 * Maintenance mode info
 */
export interface MaintenanceInfoDto {
  isEnabled: boolean;
  message?: string | null;
  allowedIps: string[];
  startTime?: Date | string | null;
  endTime?: Date | string | null;
}

/**
 * Update maintenance mode request
 */
export interface UpdateMaintenanceDto {
  isEnabled: boolean;
  message?: string;
  allowedIps?: string[];
  startTime?: Date | string;
  endTime?: Date | string;
}

// ============================================
// Cache Management Types
// ============================================

/**
 * Cache statistics
 */
export interface CacheStatisticsDto {
  totalKeys: number;
  totalSize: number;
  hitRate: number;
  missRate: number;
  byPrefix: Record<string, CachePrefixStatistics>;
}

/**
 * Cache prefix statistics
 */
export interface CachePrefixStatistics {
  keyCount: number;
  size: number;
  hitRate: number;
}

/**
 * Cache clear request
 */
export interface ClearCacheDto {
  prefix?: string;
  keys?: string[];
  clearAll?: boolean;
}
