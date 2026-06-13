/**
 * System Module API - Settings, menus, access logs, and system info
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  MenuInfoDto,
  MenuTreeNode,
  CreateMenuDto,
  UpdateMenuDto,
  MenuOrderDto,
  MenuQueryDto,
  SettingDto,
  CreateSettingDto,
  UpdateSettingDto,
  SettingGroupDto,
  SystemInfoDto,
  AccessLogInfoDto,
  AccessLogQueryDto,
  AccessLogStatisticsDto,
  AccessLogTrendDto,
  TopEndpointDto,
  SettingsCenterGroupDto,
} from './types';
import { type AccessLogTrendInterval, type TopEndpointSortBy } from './types';

const ADMIN_MENU_BASE = '/admin/menus';
const ADMIN_SETTING_BASE = '/admin/settings';
const ADMIN_ACCESS_LOG_BASE = '/admin/access-logs';

/**
 * Admin Menu Management API
 */
export function useAdminMenuApi(client: HttpClient) {
  return {
    /** Get all menus */
    getList: (params?: MenuQueryDto) =>
      client.get<MenuInfoDto[]>(ADMIN_MENU_BASE, { params }),

    /** Get menu by ID */
    getById: (id: string) =>
      client.get<MenuInfoDto>(`${ADMIN_MENU_BASE}/${id}`),

    /** Get user menu tree */
    getUserTree: (userId: string) =>
      client.get<MenuTreeNode[]>(`${ADMIN_MENU_BASE}/user/${userId}/tree`),

    /** Create menu */
    create: (data: CreateMenuDto) =>
      client.post<MenuInfoDto>(ADMIN_MENU_BASE, data),

    /** Update menu */
    update: (id: string, data: UpdateMenuDto) =>
      client.put<MenuInfoDto>(`${ADMIN_MENU_BASE}/${id}`, data),

    /** Delete menu */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_MENU_BASE}/${id}`),

    /** Batch delete menus */
    batchDelete: (ids: string[]) =>
      client.delete<void>(`${ADMIN_MENU_BASE}/batch`, { body: ids }),

    /** Batch update menu orders */
    batchUpdateOrders: (orders: MenuOrderDto[]) =>
      client.put<void>(`${ADMIN_MENU_BASE}/batch/orders`, orders),

    /** Move menu to new parent */
    move: (id: string, newParentId?: string) =>
      client.put<void>(`${ADMIN_MENU_BASE}/${id}/move`, undefined, {
        params: { newParentId },
      }),
  };
}

/**
 * User Menu API
 */
export function useMenuApi(client: HttpClient) {
  return {
    /** Get user menu tree by user ID */
    getUserTree: (userId: string) =>
      client.get<MenuTreeNode[]>(`${ADMIN_MENU_BASE}/user/${userId}/tree`),
  };
}

/**
 * Admin Setting Management API
 */
export function useAdminSettingApi(client: HttpClient) {
  return {
    /** Get all settings (optionally filtered by group) */
    getList: (group?: string) =>
      client.get<SettingDto[]>(ADMIN_SETTING_BASE, {
        params: { group },
      }),

    /** Get setting by ID */
    getById: (id: string) =>
      client.get<SettingDto>(`${ADMIN_SETTING_BASE}/${id}`),

    /** Create setting */
    create: (data: CreateSettingDto) =>
      client.post<SettingDto>(ADMIN_SETTING_BASE, data),

    /** Update setting */
    update: (id: string, data: UpdateSettingDto) =>
      client.put<SettingDto>(`${ADMIN_SETTING_BASE}/${id}`, data),

    /** Delete setting */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_SETTING_BASE}/${id}`),

    /** Batch delete settings */
    batchDelete: (ids: string[]) =>
      client.delete<void>(`${ADMIN_SETTING_BASE}/batch`, { body: ids }),

    /** Get system information (version, modules, uptime, environment) */
    getSystemInfo: () =>
      client.get<SystemInfoDto>(`${ADMIN_SETTING_BASE}/system-info`),

    /** Get setting groups with counts */
    getGroups: () =>
      client.get<SettingGroupDto[]>(`${ADMIN_SETTING_BASE}/groups`),
  };
}

const ADMIN_SETTINGS_CENTER_BASE = '/admin/settings-center';

/**
 * Admin Settings Center API — schema-driven module settings
 * (definitions + per-group save/reset).
 */
export function useAdminSettingsCenterApi(client: HttpClient) {
  return {
    /** All definition groups with schema + effective values */
    getDefinitions: () =>
      client.get<SettingsCenterGroupDto[]>(`${ADMIN_SETTINGS_CENTER_BASE}/definitions`),

    /** Save changed fields of one group (null value = remove override) */
    saveGroup: (groupKey: string, changedValues: Record<string, string | null>) =>
      client.put<SettingsCenterGroupDto>(`${ADMIN_SETTINGS_CENTER_BASE}/groups/${groupKey}`, changedValues),

    /** Remove all overrides of one group (restore defaults) */
    resetGroup: (groupKey: string) =>
      client.delete<SettingsCenterGroupDto>(`${ADMIN_SETTINGS_CENTER_BASE}/groups/${groupKey}/overrides`),
  };
}

/**
 * Admin Access Log API
 */
export function useAdminAccessLogApi(client: HttpClient) {
  return {
    /** Get access log list (paged) */
    getList: (params?: AccessLogQueryDto) =>
      client.get<PagedList<AccessLogInfoDto>>(ADMIN_ACCESS_LOG_BASE, {
        params,
      }),

    /** Get access log by ID */
    getById: (id: string) =>
      client.get<AccessLogInfoDto>(`${ADMIN_ACCESS_LOG_BASE}/${id}`),

    /** Record an access log entry */
    logAccess: (data: AccessLogInfoDto) =>
      client.post<void>(ADMIN_ACCESS_LOG_BASE, data),

    /** Get access statistics */
    getStatistics: (startTime?: Date | string, endTime?: Date | string) =>
      client.get<AccessLogStatisticsDto>(`${ADMIN_ACCESS_LOG_BASE}/statistics`, {
        params: { startTime, endTime },
      }),

    /** Get access log trend (daily/weekly/monthly) */
    getTrend: (params?: {
      interval?: AccessLogTrendInterval;
      startDate?: Date | string;
      endDate?: Date | string;
    }) =>
      client.get<AccessLogTrendDto>(`${ADMIN_ACCESS_LOG_BASE}/statistics/trend`, {
        params,
      }),

    /** Get top endpoints (most hits, slowest, most errors) */
    getTopEndpoints: (params?: {
      startDate?: Date | string;
      endDate?: Date | string;
      top?: number;
      sortBy?: TopEndpointSortBy;
    }) =>
      client.get<TopEndpointDto[]>(`${ADMIN_ACCESS_LOG_BASE}/statistics/top-endpoints`, {
        params,
      }),

    /** Delete expired access logs */
    deleteExpired: (days?: number) =>
      client.delete<void>(`${ADMIN_ACCESS_LOG_BASE}/expired`, {
        params: { days },
      }),

    /** Batch delete access logs */
    batchDelete: (ids: string[]) =>
      client.delete<void>(`${ADMIN_ACCESS_LOG_BASE}/batch`, { body: ids }),
  };
}
