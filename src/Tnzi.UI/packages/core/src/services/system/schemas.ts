/**
 * System Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Menu Schemas
// ============================================

export const menuMetaSchema = z.object({
  title: z.string().optional(),
  icon: z.string().optional(),
  keepAlive: z.boolean().optional(),
  hideMenu: z.boolean().optional(),
  hideBreadcrumb: z.boolean().optional(),
  hideChildrenInMenu: z.boolean().optional(),
  currentActiveMenu: z.string().optional(),
  ignoreRoute: z.boolean().optional(),
  frameSrc: z.string().optional(),
  frameBlank: z.boolean().optional(),
});

const menuInfoBaseSchema = z.object({
  id: z.string(),
  parentId: z.string().nullable().optional(),
  name: z.string().min(1),
  displayName: z.string().min(1),
  icon: z.string().nullable().optional(),
  path: z.string().nullable().optional(),
  component: z.string().nullable().optional(),
  redirect: z.string().nullable().optional(),
  sortOrder: z.number().int(),
  isHidden: z.boolean(),
  isExternal: z.boolean(),
  permission: z.string().nullable().optional(),
  badge: z.string().nullable().optional(),
  badgeType: z.enum(['primary', 'success', 'warning', 'danger', 'info']).nullable().optional(),
  query: z.record(z.string()).nullable().optional(),
  meta: menuMetaSchema.nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
});

export const menuInfoDtoSchema: z.ZodType<z.infer<typeof menuInfoBaseSchema> & { children: z.infer<typeof menuInfoBaseSchema>[] }> = z.lazy(() =>
  menuInfoBaseSchema.extend({
    children: z.array(menuInfoDtoSchema),
  })
);

export const createMenuSchema = z.object({
  parentId: z.string().nullable().optional(),
  name: z.string().min(1).max(100),
  displayName: z.string().min(1).max(100),
  icon: z.string().max(100).optional(),
  path: z.string().max(200).optional(),
  component: z.string().max(200).optional(),
  redirect: z.string().max(200).optional(),
  sortOrder: z.number().int().optional(),
  isHidden: z.boolean().optional(),
  isExternal: z.boolean().optional(),
  permission: z.string().max(200).optional(),
  badge: z.string().max(50).optional(),
  badgeType: z.enum(['primary', 'success', 'warning', 'danger', 'info']).optional(),
  query: z.record(z.string()).optional(),
  meta: menuMetaSchema.optional(),
});

export const updateMenuSchema = createMenuSchema.partial();

export const menuQuerySchema = z.object({
  parentId: z.string().nullable().optional(),
  isHidden: z.boolean().optional(),
  permission: z.string().optional(),
  keyword: z.string().optional(),
});

// ============================================
// Access Log Schemas
// ============================================

export const accessLogInfoDtoSchema = z.object({
  id: z.string(),
  userId: z.string().nullable().optional(),
  userName: z.string().nullable().optional(),
  path: z.string().min(1),
  method: z.string(),
  ipAddress: z.string().nullable().optional(),
  userAgent: z.string().nullable().optional(),
  statusCode: z.number().int(),
  responseTime: z.number().nonnegative(),
  requestSize: z.number().nonnegative().nullable().optional(),
  responseSize: z.number().nonnegative().nullable().optional(),
  queryString: z.string().nullable().optional(),
  requestBody: z.string().nullable().optional(),
  responseBody: z.string().nullable().optional(),
  exception: z.string().nullable().optional(),
  correlationId: z.string().nullable().optional(),
  ipCountry: z.string().nullable().optional(),
  ipProvince: z.string().nullable().optional(),
  ipCity: z.string().nullable().optional(),
  ipFullAddress: z.string().nullable().optional(),
  ipLatitude: z.number().nullable().optional(),
  ipLongitude: z.number().nullable().optional(),
  uaBrowser: z.string().nullable().optional(),
  uaBrowserVersion: z.string().nullable().optional(),
  uaOperatingSystem: z.string().nullable().optional(),
  uaOsVersion: z.string().nullable().optional(),
  uaDeviceType: z.string().nullable().optional(),
  uaDeviceBrand: z.string().nullable().optional(),
  uaDeviceModel: z.string().nullable().optional(),
  uaIsMobile: z.boolean(),
  uaIsBot: z.boolean(),
  uaBotName: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
});

export const accessLogQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  userId: z.string().optional(),
  userName: z.string().optional(),
  path: z.string().optional(),
  method: z.string().optional(),
  ipAddress: z.string().optional(),
  statusCode: z.number().int().optional(),
  minResponseTime: z.number().nonnegative().optional(),
  maxResponseTime: z.number().positive().optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  isMobile: z.boolean().optional(),
  isBot: z.boolean().optional(),
  country: z.string().optional(),
  correlationId: z.string().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

export const accessLogStatisticsSchema = z.object({
  totalRequests: z.number().int().nonnegative(),
  uniqueUsers: z.number().int().nonnegative(),
  successRequests: z.number().int().nonnegative(),
  errorRequests: z.number().int().nonnegative(),
  averageResponseTime: z.number().nonnegative(),
});

// ============================================
// Settings Schemas
// ============================================

export const systemSettingDtoSchema = z.object({
  key: z.string().min(1),
  value: z.string(),
  description: z.string().nullable().optional(),
  category: z.string(),
  isPublic: z.boolean(),
  lastModifiedAt: z.union([z.date(), z.string()]),
  lastModifiedBy: z.string().nullable().optional(),
});

export const updateSettingSchema = z.object({
  value: z.string(),
});

export const batchUpdateSettingsSchema = z.object({
  settings: z.record(z.string()),
});

export const settingQuerySchema = z.object({
  category: z.string().optional(),
  isPublic: z.boolean().optional(),
  keyword: z.string().optional(),
});

// ============================================
// System Info Schemas
// ============================================

export const systemInfoDtoSchema = z.object({
  version: z.string(),
  environment: z.string(),
  machineName: z.string(),
  osDescription: z.string(),
  runtimeVersion: z.string(),
  startTime: z.union([z.date(), z.string()]),
  uptime: z.string(),
  uptimeSeconds: z.number().int().nonnegative(),
});

export const healthStatusSchema = z.number().int().min(0).max(2);

export const healthCheckResultSchema = z.object({
  name: z.string(),
  status: healthStatusSchema,
  description: z.string().nullable().optional(),
  duration: z.number().nonnegative(),
  data: z.record(z.unknown()).optional(),
  exception: z.string().nullable().optional(),
});

export const healthCheckResponseSchema = z.object({
  status: healthStatusSchema,
  totalDuration: z.number().nonnegative(),
  checks: z.array(healthCheckResultSchema),
});

// ============================================
// Maintenance Schemas
// ============================================

export const maintenanceInfoDtoSchema = z.object({
  isEnabled: z.boolean(),
  message: z.string().nullable().optional(),
  allowedIps: z.array(z.string()),
  startTime: z.union([z.date(), z.string()]).nullable().optional(),
  endTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const updateMaintenanceSchema = z.object({
  isEnabled: z.boolean(),
  message: z.string().max(500).optional(),
  allowedIps: z.array(z.string()).optional(),
  startTime: z.union([z.date(), z.string()]).nullable().optional(),
  endTime: z.union([z.date(), z.string()]).nullable().optional(),
});

// ============================================
// Cache Schemas
// ============================================

export const cachePrefixStatisticsSchema = z.object({
  keyCount: z.number().int().nonnegative(),
  size: z.number().nonnegative(),
  hitRate: z.number().min(0).max(1),
});

export const cacheStatisticsSchema = z.object({
  totalKeys: z.number().int().nonnegative(),
  totalSize: z.number().nonnegative(),
  hitRate: z.number().min(0).max(1),
  missRate: z.number().min(0).max(1),
  byPrefix: z.record(cachePrefixStatisticsSchema),
});

export const clearCacheSchema = z.object({
  prefix: z.string().optional(),
  keys: z.array(z.string()).optional(),
  clearAll: z.boolean().optional(),
});
