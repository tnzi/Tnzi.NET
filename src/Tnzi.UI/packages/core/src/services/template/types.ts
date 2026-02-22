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
 * Template info DTO
 */
export interface TemplateInfoDto extends AuditedEntity<string> {
  templateName: string;
  module: string;
  category: string;
  subjectTemplate: string;
  contentTemplate: string;
  defaultLayoutName?: string | null;
  isActive: boolean;
  description?: string | null;
  metadata?: string | null;
  variables?: TemplateVariableDto[];
  usageCount?: number;
}

/**
 * Template entity DTO (detail CRUD endpoints)
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

/**
 * Template preview request
 */
export interface TemplatePreviewDto {
  templateName: string;
  layoutName?: string;
  variables: Record<string, unknown>;
}

/**
 * Template preview result
 */
export interface TemplatePreviewResultDto {
  subject: string;
  content: string;
  isHtml: boolean;
}

// ============================================
// Template Request Types
// ============================================

/**
 * Create template request
 */
export interface CreateTemplateDto {
  templateName: string;
  module: string;
  category: string;
  subjectTemplate: string;
  contentTemplate: string;
  defaultLayoutName?: string;
  isActive?: boolean;
  description?: string;
  variables?: TemplateVariableDto[];
}

/**
 * Update template request
 */
export interface UpdateTemplateDto {
  subjectTemplate?: string;
  contentTemplate?: string;
  defaultLayoutName?: string;
  isActive?: boolean;
  description?: string;
  variables?: TemplateVariableDto[];
}

/**
 * Template list query
 */
export interface TemplateQueryDto extends SortedPagedQueryDto {
  module?: string;
  category?: string;
  templateName?: string;
  isActive?: boolean;
  keyword?: string;
}

// ============================================
// Layout Types
// ============================================

/**
 * Layout info DTO
 */
export interface LayoutInfoDto extends AuditedEntity<string> {
  layoutName: string;
  module: string;
  category?: string | null;
  layoutContent: string;
  content?: string;
  isActive: boolean;
  isDefault?: boolean;
  description?: string | null;
  metadata?: string | null;
  usageCount?: number;
}

/**
 * Layout entity DTO (detail CRUD endpoints)
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
 */
export interface CreateLayoutDto {
  layoutName: string;
  module: string;
  category?: string;
  content: string;
  isActive?: boolean;
  description?: string;
}

/**
 * Update layout request
 */
export interface UpdateLayoutDto {
  content?: string;
  isActive?: boolean;
  description?: string;
}

/**
 * Layout list query
 */
export interface LayoutQueryDto extends SortedPagedQueryDto {
  module?: string;
  category?: string;
  layoutName?: string;
  isActive?: boolean;
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

// ============================================
// Template Category Types
// ============================================

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

// ============================================
// Template Validation Types
// ============================================

/**
 * Template validation request
 */
export interface ValidateTemplateDto {
  subjectTemplate?: string;
  contentTemplate: string;
  variables?: TemplateVariableDto[];
}

/**
 * Template validation result
 */
export interface TemplateValidationResultDto {
  isValid: boolean;
  errors: TemplateValidationError[];
  warnings: TemplateValidationWarning[];
  detectedVariables: string[];
}

/**
 * Template validation error
 */
export interface TemplateValidationError {
  line: number;
  column: number;
  message: string;
  code: string;
}

/**
 * Template validation warning
 */
export interface TemplateValidationWarning {
  line: number;
  column: number;
  message: string;
  code: string;
}

// ============================================
// Template Import/Export Types
// ============================================

/**
 * Template export data
 */
export interface TemplateExportDto {
  templates: TemplateInfoDto[];
  layouts: LayoutInfoDto[];
  includeVariables: boolean;
}

/**
 * Template import request
 */
export interface TemplateImportDto {
  data: TemplateExportDto;
  overwrite: boolean;
  activateImported: boolean;
}

/**
 * Template import result
 */
export interface TemplateImportResultDto {
  importedTemplates: number;
  importedLayouts: number;
  skippedTemplates: string[];
  skippedLayouts: string[];
  errors: string[];
}
