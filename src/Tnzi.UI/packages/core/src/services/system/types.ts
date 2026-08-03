/**
 * System Module Types - System settings, menus, access logs
 * Aligned with Tnzi.NET backend System module
 */

import type { AuditedEntity } from '../../types/entities';
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
 * A single health-check entry, as written by `Tnzi.HealthChecks`
 * (`HealthChecksModule.WriteDetailedResponseAsync`).
 */
export interface HealthCheckEntryDto {
  name: string;
  status: HealthStatus;
  /** Elapsed milliseconds for this individual check. */
  duration: number;
  description?: string | null;
  /** Check-specific payload; null when the check reported no data. */
  data?: Record<string, unknown> | null;
  /** Exception message when the check threw. */
  exception?: string | null;
}

/**
 * Detailed health-check payload served at the configured health path
 * (`HealthChecks:Path`, default `/health`).
 *
 * NOTE: this endpoint is NOT an `ApiResult` envelope and does NOT live under
 * the API base - it is mapped directly on the host via `MapHealthChecks`, and
 * the HTTP status code alone carries success/failure. Fetch it directly rather
 * than through `HttpClient`, which would try to normalize the envelope.
 */
export interface HealthCheckResponseDto {
  status: HealthStatus;
  /** Total elapsed milliseconds across every check. */
  totalDuration: number;
  entries: HealthCheckEntryDto[];
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
  type: 'String' | 'Text' | 'Int' | 'Decimal' | 'Boolean' | 'Select' | 'Password' | 'Duration';
  isEncrypted: boolean;
  isReadOnly: boolean;
  isRequired: boolean;
  min?: number | null;
  max?: number | null;
  /** Regex constraint for String/Text values (.NET syntax, whole-value match; validated on save). */
  pattern?: string | null;
  options?: string[] | null;
  /**
   * Optional in-group subsection label (display-only). Fields sharing a
   * subsection collapse into one section in the panel; fields without one
   * render in the default area above the sections.
   */
  subsection?: string | null;
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
  /**
   * Whether this is a framework built-in config group (from a `Tnzi.*` assembly).
   * A consuming application's own `[RuntimeSetting]` group is `false`. The admin
   * "Built-in menus" toggle hides only built-in groups; consumer config always
   * stays. Defaults `false` (fail-open when the backend predates this field -
   * an unknown group is treated as consumer config and never hidden).
   */
  isBuiltIn?: boolean;
  /**
   * Whether the current user may modify this group (holds
   * `{group}.settings.{slug}.update` or is super-admin). `false` = the user has
   * view but not update permission; the panel renders read-only. Defaults true
   * (fail-open when the backend predates this field).
   */
  canEdit?: boolean;
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

// ============================================
// Appearance (Global Theme Snapshots)
// ============================================

/**
 * Mirror of Tnzi.System.Dtos.ThemeSnapshotDto.
 *
 * `theme` is an opaque snapshot document owned by the FRONT-END of the scope it
 * belongs to (the admin console stores layout mode and tab-bar visibility; the
 * chat app stores its own surface tokens). The backend never interprets it,
 * which is why one endpoint pair serves every product. null when that scope has
 * no saved theme (clients fall back to local defaults).
 */
export interface GlobalThemeSnapshotDto {
  theme: Record<string, unknown> | null;
  /** Last save time (UTC ISO string); null when unset */
  updatedAt?: string | null;
}

/** Mirror of Tnzi.System.Dtos.SaveThemeSnapshotDto */
export interface SaveGlobalThemeSnapshotDto {
  /** Theme snapshot document; must be a JSON object */
  theme: Record<string, unknown>;
}

/** @deprecated Renamed to {@link GlobalThemeSnapshotDto} when themes became scoped. */
export type AdminGlobalThemeDto = GlobalThemeSnapshotDto;

/** @deprecated Renamed to {@link SaveGlobalThemeSnapshotDto} when themes became scoped. */
export type SaveAdminGlobalThemeDto = SaveGlobalThemeSnapshotDto;
