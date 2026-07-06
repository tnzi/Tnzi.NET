/**
 * System Module Types - System settings, menus, access logs
 * Aligned with Tnzi.NET backend System module
 */

import type { AuditedEntity, OrderedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import { HealthStatus } from './metadata';
import type { MenuBadgeType, FeatureRequirementType } from './metadata';

export { HealthStatus };
export type { MenuBadgeType, FeatureRequirementType };

// ============================================
// Enums
// ============================================

// The backend registers a global JsonStringEnumConverter, so every enum-typed
// response field serializes as its PascalCase member name (and input still
// accepts both the name and the legacy integer). These enums use string member
// values to match the wire shape exactly.

/**
 * Menu type
 */
export enum MenuType {
  Directory = 'Directory',
  Menu = 'Menu',
  Button = 'Button',
}

/**
 * Setting value type
 */
export enum SettingValueType {
  String = 'String',
  Integer = 'Integer',
  Boolean = 'Boolean',
  Json = 'Json',
}

/**
 * Setting scope
 */
export enum SettingScope {
  Global = 'Global',
  Tenant = 'Tenant',
  User = 'User',
}

/**
 * Access log trend interval
 */
export enum AccessLogTrendInterval {
  Daily = 'Daily',
  Weekly = 'Weekly',
  Monthly = 'Monthly',
}

/**
 * Top endpoint sort criteria
 */
export enum TopEndpointSortBy {
  Hits = 'Hits',
  AverageResponseTime = 'AverageResponseTime',
  Errors = 'Errors',
}

// ============================================
// Menu Types
// ============================================

/**
 * Menu info DTO
 */
export interface MenuInfoDto extends OrderedEntity<string> {
  parentId?: string | null;
  /** Route name this row overrides (e.g. "identity.users"); null = custom node. */
  menuKey?: string | null;
  name: string;
  displayName: string;
  icon?: string | null;
  path?: string | null;
  component?: string | null;
  redirect?: string | null;
  isHidden: boolean;
  isExternal: boolean;
  permission?: string | null;
  type: MenuType;
  badge?: string | null;
  badgeType?: MenuBadgeType | null;
  query?: Record<string, string> | null;
  meta?: MenuMetaDto | null;
  children: MenuInfoDto[];
  creationTime: Date | string;
  lastModificationTime?: Date | string | null;
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
 * Menu tree node (backend MenuTreeNode)
 */
export interface MenuTreeNode {
  id: string;
  parentId?: string | null;
  /** Route name this row overrides (e.g. "identity.users"); null = custom node. */
  menuKey?: string | null;
  name: string;
  icon?: string | null;
  path?: string | null;
  component?: string | null;
  sortOrder: number;
  isHidden: boolean;
  permission?: string | null;
  type: MenuType;
  children: MenuTreeNode[];
}

/**
 * Create menu request
 */
export interface CreateMenuDto {
  parentId?: string;
  menuKey?: string;
  name: string;
  displayName?: string;
  icon?: string;
  path?: string;
  component?: string;
  redirect?: string;
  sortOrder?: number;
  isHidden?: boolean;
  isExternal?: boolean;
  permission?: string;
  type?: MenuType;
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
  menuKey?: string;
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
  type?: MenuType;
  badge?: string;
  badgeType?: MenuBadgeType;
  query?: Record<string, string>;
  meta?: MenuMetaDto;
}

/**
 * Menu order update DTO
 */
export interface MenuOrderDto {
  id: string;
  sortOrder: number;
}

/**
 * Menu seed result (backend MenuSeedResultDto). Seed upserts by menuKey:
 * inserts missing rows, skips existing ones (protects operator overrides).
 */
export interface MenuSeedResultDto {
  inserted: number;
  skipped: number;
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
 * Access log trend data point
 */
export interface AccessLogTrendDataPoint {
  label: string;
  startTime: Date | string;
  totalRequests: number;
  successRequests: number;
  errorRequests: number;
  uniqueUsers: number;
  averageResponseTime: number;
}

/**
 * Access log trend DTO
 */
export interface AccessLogTrendDto {
  interval: AccessLogTrendInterval;
  startDate: Date | string;
  endDate: Date | string;
  dataPoints: AccessLogTrendDataPoint[];
}

/**
 * Top endpoint statistics DTO
 */
export interface TopEndpointDto {
  path: string;
  method: string;
  totalHits: number;
  successHits: number;
  errorHits: number;
  averageResponseTime: number;
  maxResponseTime: number;
  errorRate: number;
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
  minStatusCode?: number;
  maxStatusCode?: number;
  minResponseTime?: number;
  maxResponseTime?: number;
  startTime?: Date | string;
  endTime?: Date | string;
  keyword?: string;
  isMobile?: boolean;
  isBot?: boolean;
  country?: string;
  correlationId?: string;
}

// ============================================
// System Settings Types
// ============================================

/**
 * Setting DTO (backend SettingDto)
 */
export interface SettingDto {
  id: string;
  key: string;
  value: string;
  description?: string | null;
  group?: string | null;
  isSystem: boolean;
  sortOrder: number;
  valueType: SettingValueType;
  scope: SettingScope;
  scopeId?: string | null;
  creationTime: Date | string;
}

/**
 * Create setting request
 */
export interface CreateSettingDto {
  key: string;
  value: string;
  description?: string;
  group?: string;
  sortOrder?: number;
  valueType?: SettingValueType;
  scope?: SettingScope;
  scopeId?: string;
}

/**
 * Update setting request
 */
export interface UpdateSettingDto {
  value: string;
  description?: string;
  group?: string;
  sortOrder?: number;
  valueType?: SettingValueType;
}

/**
 * Setting group info DTO
 */
export interface SettingGroupDto {
  groupName: string;
  settingCount: number;
}

// ============================================
// System Info Types
// ============================================

/**
 * System module info
 */
export interface SystemModuleInfoDto {
  name: string;
  assembly: string;
  isEnabled: boolean;
  loadOrder: number;
}

/**
 * System information (backend SystemInfoDto)
 */
export interface SystemInfoDto {
  appName: string;
  frameworkVersion: string;
  runtimeVersion: string;
  operatingSystem: string;
  startTime: Date | string;
  uptime: string;
  environment: string;
  loadedModules: SystemModuleInfoDto[];
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
// Settings Center Types
// ============================================

/** Mirror of Tnzi.System.Dtos.SettingsCenterFieldDto */
export interface SettingsCenterFieldDto {
  key: string;
  label: string;
  i18nKey?: string | null;
  description?: string | null;
  type: 'String' | 'Text' | 'Int' | 'Decimal' | 'Boolean' | 'Select' | 'Password';
  isEncrypted: boolean;
  isReadOnly: boolean;
  isRequired: boolean;
  min?: number | null;
  max?: number | null;
  options?: string[] | null;
  value?: string | null;
  defaultValue?: string | null;
  isOverridden: boolean;
  isSet: boolean;
}

/** Mirror of Tnzi.System.Dtos.SettingsCenterGroupDto */
export interface SettingsCenterGroupDto {
  key: string;
  moduleName: string;
  displayName: string;
  i18nKey?: string | null;
  description?: string | null;
  icon?: string | null;
  order: number;
  fields: SettingsCenterFieldDto[];
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
