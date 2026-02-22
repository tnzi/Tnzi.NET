/**
 * Template Module API - Template and layout operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  TemplateInfoDto,
  TemplateEntityDto,
  CreateTemplateDto,
  UpdateTemplateDto,
  TemplateQueryDto,
  LayoutInfoDto,
  LayoutEntityDto,
  CreateLayoutDto,
  UpdateLayoutDto,
  LayoutQueryDto,
} from './types';

// Aligned with backend controllers
const ADMIN_BASE = '/admin/templates';
const ADMIN_LAYOUT_BASE = '/admin/layouts';

/**
 * Admin Template Management API
 */
export function useAdminTemplateApi(client: HttpClient) {
  return {
    /** Get template list (GET) */
    getList: (params?: TemplateQueryDto) =>
      client.get<PagedList<TemplateInfoDto>>(ADMIN_BASE, { params }),

    /** Get template by ID */
    getById: (id: string) =>
      client.get<TemplateEntityDto>(`${ADMIN_BASE}/${id}`),

    /** Get template by name, module, category */
    getByName: (name: string, module: string, category?: string) =>
      client.get<TemplateEntityDto>(`${ADMIN_BASE}/name/${encodeURIComponent(name)}`, {
        params: { module, category },
      }),

    /** Create template */
    create: (data: CreateTemplateDto) =>
      client.post<TemplateEntityDto>(ADMIN_BASE, data),

    /** Update template */
    update: (id: string, data: UpdateTemplateDto) =>
      client.put<TemplateEntityDto>(`${ADMIN_BASE}/${id}`, data),

    /** Delete template */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_BASE}/${id}`),
  };
}

/**
 * Admin Layout Management API
 */
export function useAdminLayoutApi(client: HttpClient) {
  return {
    /** Get layout list (GET) */
    getList: (params?: LayoutQueryDto) =>
      client.get<PagedList<LayoutInfoDto>>(ADMIN_LAYOUT_BASE, { params }),

    /** Get layout by ID */
    getById: (id: string) =>
      client.get<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/${id}`),

    /** Create layout */
    create: (data: CreateLayoutDto) =>
      client.post<LayoutEntityDto>(ADMIN_LAYOUT_BASE, data),

    /** Update layout */
    update: (id: string, data: UpdateLayoutDto) =>
      client.put<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/${id}`, data),

    /** Delete layout */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_LAYOUT_BASE}/${id}`),
  };
}
