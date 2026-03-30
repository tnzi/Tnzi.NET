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
  ValidateTemplateDto,
  TemplateValidationResultDto,
  PreviewTemplateDto,
  TemplateImportDto,
  TemplateImportResultDto,
  TemplateVariablesDto,
  BatchActivateDto,
  LayoutInfoDto,
  LayoutEntityDto,
  CreateLayoutDto,
  UpdateLayoutDto,
  LayoutQueryDto,
  LayoutImportDto,
  LayoutImportResultDto,
} from './types';

// Aligned with backend controllers
const ADMIN_BASE = '/admin/templates';
const ADMIN_LAYOUT_BASE = '/admin/layouts';

/**
 * Admin Template Management API
 */
export function useAdminTemplateApi(client: HttpClient) {
  return {
    /** Get template list (paged) */
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

    /** Batch delete templates */
    deleteBatch: (ids: string[]) =>
      client.delete<void>(`${ADMIN_BASE}/batch`, { body: ids }),

    /** Validate template syntax */
    validate: (data: ValidateTemplateDto) =>
      client.post<TemplateValidationResultDto>(`${ADMIN_BASE}/validate`, data),

    /** Clone template */
    clone: (id: string, newName: string) =>
      client.post<TemplateEntityDto>(`${ADMIN_BASE}/${id}/clone`, undefined, {
        params: { newName },
      }),

    /** Export templates as JSON */
    export: (module?: string, category?: string) =>
      client.get<string>(`${ADMIN_BASE}/export`, {
        params: { module, category },
      }),

    /** Import templates from JSON */
    import: (data: TemplateImportDto) =>
      client.post<TemplateImportResultDto>(`${ADMIN_BASE}/import`, data),

    /** Discover template variables */
    getVariables: (id: string) =>
      client.get<TemplateVariablesDto>(`${ADMIN_BASE}/${id}/variables`),

    /** Batch activate or deactivate templates */
    batchActivate: (data: BatchActivateDto) =>
      client.post<number>(`${ADMIN_BASE}/batch-activate`, data),

    /** Preview template rendering */
    preview: (data: PreviewTemplateDto) =>
      client.post<string>(`${ADMIN_BASE}/preview`, data),
  };
}

/**
 * Admin Layout Management API
 */
export function useAdminLayoutApi(client: HttpClient) {
  return {
    /** Get layout list (paged) */
    getList: (params?: LayoutQueryDto) =>
      client.get<PagedList<LayoutInfoDto>>(ADMIN_LAYOUT_BASE, { params }),

    /** Get layout by ID */
    getById: (id: string) =>
      client.get<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/${id}`),

    /** Get layout by name, module, category */
    getByName: (name: string, module: string, category?: string) =>
      client.get<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/name/${encodeURIComponent(name)}`, {
        params: { module, category },
      }),

    /** Get default layout for module and category */
    getDefault: (module: string, category: string) =>
      client.get<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/default`, {
        params: { module, category },
      }),

    /** Create layout */
    create: (data: CreateLayoutDto) =>
      client.post<LayoutEntityDto>(ADMIN_LAYOUT_BASE, data),

    /** Update layout */
    update: (id: string, data: UpdateLayoutDto) =>
      client.put<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/${id}`, data),

    /** Delete layout */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_LAYOUT_BASE}/${id}`),

    /** Batch delete layouts */
    deleteBatch: (ids: string[]) =>
      client.delete<void>(`${ADMIN_LAYOUT_BASE}/batch`, { body: ids }),

    /** Clone layout */
    clone: (id: string, newName: string) =>
      client.post<LayoutEntityDto>(`${ADMIN_LAYOUT_BASE}/${id}/clone`, undefined, {
        params: { newName },
      }),

    /** Export layouts as JSON */
    export: (module?: string, category?: string) =>
      client.get<string>(`${ADMIN_LAYOUT_BASE}/export`, {
        params: { module, category },
      }),

    /** Import layouts from JSON */
    import: (data: LayoutImportDto) =>
      client.post<LayoutImportResultDto>(`${ADMIN_LAYOUT_BASE}/import`, data),

    /** Batch activate or deactivate layouts */
    batchActivate: (data: BatchActivateDto) =>
      client.post<number>(`${ADMIN_LAYOUT_BASE}/batch-activate`, data),
  };
}
