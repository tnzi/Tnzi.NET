/**
 * Template Module Types - Template management for notifications
 * Aligned with Tnzi.NET backend Template module
 */

import type { AuditedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import type { TemplateVariableType } from './metadata';

export type { TemplateVariableType };

// ============================================
// Template Types
// ============================================

/**
 * Template info DTO (paged list item, no large text fields)
 * Aligned with backend TemplateInfoDto
 */
export interface TemplateInfoDto extends AuditedEntity<string> {
  templateName: string;
  module: string;
  category: string;
  defaultLayoutName?: string | null;
  isActive: boolean;
  description?: string | null;
  /** Origin of this row — "Database" | "FileSystem". */
  source?: string;
  /** True for filesystem-fallback rows (shipped with binaries, not editable). */
  isReadOnly?: boolean;
  /** Absolute filesystem path of the source template (file-source rows only). */
  filePath?: string | null;
  /** Subject template (Razor source) — populated inline for file-source rows. */
  subjectTemplate?: string | null;
  /** Content template (Razor source) — populated inline for file-source rows. */
  contentTemplate?: string | null;
}

/**
 * Template entity DTO (detail CRUD endpoints)
 * Aligned with backend TemplateDto
 */
export interface TemplateEntityDto extends AuditedEntity<string> {
  templateName: string;
  module: string;
  category: string;
  subjectTemplate: string;
  contentTemplate: string;
  defaultLayoutName?: string | null;
  isActive: boolean;
  description?: string | null;
  metadata?: string | null;
}

/**
 * Template variable definition
 */
export interface TemplateVariableDto {
  name: string;
  type: TemplateVariableType;
  description?: string | null;
  isRequired: boolean;
  defaultValue?: unknown;
  exampleValue?: unknown;
}

// ============================================
// Template Request Types
// ============================================

/**
 * Create template request
 * Aligned with backend CreateTemplateRequest (extends TemplateRequestBase)
 */
export interface CreateTemplateDto {
  templateName: string;
  module: string;
  category: string;
  subjectTemplate?: string;
  contentTemplate: string;
  defaultLayoutName?: string;
  isActive?: boolean;
  description?: string;
  metadata?: string;
}

/**
 * Update template request
 * Aligned with backend UpdateTemplateRequest (extends TemplateRequestBase)
 */
export interface UpdateTemplateDto {
  templateName: string;
  module: string;
  category: string;
  subjectTemplate?: string;
  contentTemplate: string;
  defaultLayoutName?: string;
  isActive?: boolean;
  description?: string;
  metadata?: string;
}

/**
 * Template list query
 * Aligned with backend QueryTemplateRequest
 */
export interface TemplateQueryDto extends SortedPagedQueryDto {
  module?: string;
  category?: string;
  isActive?: boolean;
  keyword?: string;
  /** Include filesystem-backed templates (Templates/{module}/.../*.cshtml). */
  includeFileSource?: boolean;
}

/**
 * Validate template request
 * Aligned with backend ValidateTemplateRequest
 */
export interface ValidateTemplateDto {
  content: string;
}

/**
 * Template validation result
 * Aligned with backend TemplateValidationResult
 */
export interface TemplateValidationResultDto {
  isValid: boolean;
  errors: string[];
}

/**
 * Preview template request
 * Aligned with backend PreviewTemplateRequest
 */
export interface PreviewTemplateDto {
  content: string;
  layoutContent?: string;
  model?: Record<string, unknown>;
}

// ============================================
// Template Import/Export Types
// ============================================

/**
 * Template import request
 * Aligned with backend TemplateImportRequest
 */
export interface TemplateImportDto {
  json: string;
  overwriteExisting?: boolean;
}

/**
 * Template import result
 * Aligned with backend TemplateImportResultDto
 */
export interface TemplateImportResultDto {
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  errors: string[];
}

/**
 * Template variables discovery result
 * Aligned with backend TemplateVariablesDto
 */
export interface TemplateVariablesDto {
  templateId: string;
  templateName: string;
  subjectVariables: string[];
  contentVariables: string[];
  allVariables: string[];
}

/**
 * Batch activate/deactivate request
 * Aligned with backend BatchActivateRequest
 */
export interface BatchActivateDto {
  ids: string[];
  isActive: boolean;
}

// ============================================
// Layout Types
// ============================================

/**
 * Layout info DTO (paged list item, no large text fields)
 * Aligned with backend LayoutInfoDto
 */
export interface LayoutInfoDto extends AuditedEntity<string> {
  layoutName: string;
  module: string;
  category: string;
  isActive: boolean;
  isDefault: boolean;
  description?: string | null;
  /** Origin of this row — "Database" | "FileSystem". */
  source?: string;
  /** True for filesystem-fallback rows. */
  isReadOnly?: boolean;
  /** Absolute filesystem path of the source layout. */
  filePath?: string | null;
}

/**
 * Layout entity DTO (detail CRUD endpoints)
 * Aligned with backend LayoutDto
 */
export interface LayoutEntityDto extends AuditedEntity<string> {
  layoutName: string;
  module: string;
  category: string;
  layoutContent: string;
  isActive: boolean;
  isDefault: boolean;
  description?: string | null;
  metadata?: string | null;
}

/**
 * Create layout request
 * Aligned with backend CreateLayoutRequest (extends LayoutRequestBase)
 */
export interface CreateLayoutDto {
  layoutName: string;
  module: string;
  category: string;
  layoutContent: string;
  isActive?: boolean;
  isDefault?: boolean;
  description?: string;
  metadata?: string;
}

/**
 * Update layout request
 * Aligned with backend UpdateLayoutRequest (extends LayoutRequestBase)
 */
export interface UpdateLayoutDto {
  layoutName: string;
  module: string;
  category: string;
  layoutContent: string;
  isActive?: boolean;
  isDefault?: boolean;
  description?: string;
  metadata?: string;
}

/**
 * Layout list query
 * Aligned with backend QueryLayoutRequest
 */
export interface LayoutQueryDto extends SortedPagedQueryDto {
  module?: string;
  category?: string;
  isActive?: boolean;
  isDefault?: boolean;
  keyword?: string;
  /** Include filesystem-backed layouts. */
  includeFileSource?: boolean;
}

// ============================================
// Layout Import/Export Types
// ============================================

/**
 * Layout import request
 * Aligned with backend LayoutImportRequest
 */
export interface LayoutImportDto {
  json: string;
  overwriteExisting?: boolean;
}

/**
 * Layout import result
 * Aligned with backend LayoutImportResultDto
 */
export interface LayoutImportResultDto {
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  errors: string[];
}

// ============================================
// Template Rendering Types
// ============================================

/**
 * Render template request
 */
export interface RenderTemplateDto {
  templateName: string;
  layoutName?: string;
  variables: Record<string, unknown>;
  culture?: string;
}

/**
 * Rendered template result
 */
export interface RenderedTemplateDto {
  templateId: string;
  templateName: string;
  subject: string;
  content: string;
  isHtml: boolean;
  usedLayout?: string | null;
}

/**
 * Template category DTO
 */
export interface TemplateCategoryDto {
  module: string;
  name: string;
  displayName: string;
  description?: string | null;
  templateCount: number;
}
