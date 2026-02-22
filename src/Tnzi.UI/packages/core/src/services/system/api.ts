/**
 * System Module API - Menu and access log operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  MenuInfoDto,
  CreateMenuDto,
  UpdateMenuDto,
  MenuQueryDto,
  AccessLogInfoDto,
  AccessLogQueryDto,
  AccessLogStatisticsDto,
} from './types';

const ADMIN_MENU_BASE = '/admin/menus';
const ADMIN_ACCESS_LOG_BASE = '/admin/access-logs';

/**
 * Admin Menu Management API
 */
export function useAdminMenuApi(client: HttpClient) {
  return {
    /** Get menu list */
    getList: (params?: MenuQueryDto) =>
      client.get<MenuInfoDto[]>(ADMIN_MENU_BASE, { params }),

    /** Get menu by ID */
    getById: (id: string) =>
      client.get<MenuInfoDto>(`${ADMIN_MENU_BASE}/${id}`),

    /** Get user menu tree */
    getUserTree: (userId: string) =>
      client.get<MenuInfoDto[]>(`${ADMIN_MENU_BASE}/user/${userId}/tree`),

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
    batchUpdateOrders: (orders: Array<{ id: string; sortOrder: number }>) =>
      client.put<void>(`${ADMIN_MENU_BASE}/batch/orders`, orders),
  };
}

/**
 * User Menu API
 */
export function useMenuApi(client: HttpClient) {
  return {
    /** Get user menu tree by user ID */
    getUserTree: (userId: string) =>
      client.get<MenuInfoDto[]>(`${ADMIN_MENU_BASE}/user/${userId}/tree`),
  };
}

/**
 * Admin Access Log API
 */
export function useAdminAccessLogApi(client: HttpClient) {
  return {
    /** Get access log list */
    getList: (params?: AccessLogQueryDto) =>
      client.get<PagedList<AccessLogInfoDto>>(ADMIN_ACCESS_LOG_BASE, {
        params: {
          ...params,
          startTime: params?.startTime ?? params?.startDate,
          endTime: params?.endTime ?? params?.endDate,
        },
      }),

    /** Get access log by ID */
    getById: (id: string) =>
      client.get<AccessLogInfoDto>(`${ADMIN_ACCESS_LOG_BASE}/${id}`),

    /** Get access statistics */
    getStatistics: (startTime?: Date | string, endTime?: Date | string) =>
      client.get<AccessLogStatisticsDto>(`${ADMIN_ACCESS_LOG_BASE}/statistics`, {
        params: { startTime, endTime },
      }),

    /** Delete expired access logs */
    deleteExpired: (days?: number) =>
      client.delete<void>(`${ADMIN_ACCESS_LOG_BASE}/expired`, {
        params: { days },
      }),
  };
}
