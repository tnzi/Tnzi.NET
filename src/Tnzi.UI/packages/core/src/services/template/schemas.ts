/**
 * Template Module Schemas - Zod validation schemas
 */

import { z } from 'zod';
import type { TemplateVariableType } from './metadata';
import { sortedPagedQuerySchema } from '../../schemas/common';

const templateVariableTypes: [TemplateVariableType, ...TemplateVariableType[]] = [
  'string',
  'number',
  'boolean',
  'date',
  'object',
  'array',
];

/**
 * Template Query Schema
 */
export const templateQuerySchema = sortedPagedQuerySchema.extend({
  module: z.string().optional(),
  category: z.string().optional(),
  isActive: z.boolean().optional(),
  keyword: z.string().optional(),
});

/**
 * Template Variable Schema
 */
export const templateVariableSchema = z.object({
  name: z.string().min(1),
  type: z.enum(templateVariableTypes),
  defaultValue: z.unknown().optional(),
  exampleValue: z.unknown().optional(),
  isRequired: z.boolean(),
  description: z.string().nullable().optional(),
});

/**
 * Template Create/Update Schema
 * Aligned with backend TemplateRequestBase
 */
export const templateUpsertSchema = z.object({
  templateName: z.string().min(1).max(200),
  module: z.string().min(1).max(100),
  category: z.string().min(1).max(100),
  subjectTemplate: z.string().default(''),
  contentTemplate: z.string().min(1),
  defaultLayoutName: z.string().max(200).optional(),
  isActive: z.boolean().optional(),
  description: z.string().max(500).optional(),
  metadata: z.string().max(4000).optional(),
});

/**
 * Layout Query Schema
 * Aligned with backend QueryLayoutRequest
 */
export const layoutQuerySchema = sortedPagedQuerySchema.extend({
  module: z.string().optional(),
  category: z.string().optional(),
  isActive: z.boolean().optional(),
  isDefault: z.boolean().optional(),
  keyword: z.string().optional(),
});

/**
 * Layout Create/Update Schema
 * Aligned with backend LayoutRequestBase
 */
export const layoutUpsertSchema = z.object({
  layoutName: z.string().min(1).max(200),
  module: z.string().min(1).max(100),
  category: z.string().min(1).max(100),
  layoutContent: z.string().min(1),
  isActive: z.boolean().optional(),
  isDefault: z.boolean().optional(),
  description: z.string().max(500).optional(),
  metadata: z.string().max(4000).optional(),
});

/**
 * Validate template schema
 * Aligned with backend ValidateTemplateRequest
 */
export const validateTemplateSchema = z.object({
  content: z.string().min(1),
});

/**
 * Preview template schema
 * Aligned with backend PreviewTemplateRequest
 */
export const previewTemplateSchema = z.object({
  content: z.string().min(1),
  layoutContent: z.string().optional(),
  model: z.record(z.unknown()).optional(),
});

/**
 * Template import schema
 * Aligned with backend TemplateImportRequest
 */
export const templateImportSchema = z.object({
  json: z.string().min(1),
  overwriteExisting: z.boolean().optional(),
});

/**
 * Layout import schema
 * Aligned with backend LayoutImportRequest
 */
export const layoutImportSchema = z.object({
  json: z.string().min(1),
  overwriteExisting: z.boolean().optional(),
});

/**
 * Batch activate schema
 * Aligned with backend BatchActivateRequest
 */
export const batchActivateSchema = z.object({
  ids: z.array(z.string()).min(1),
  isActive: z.boolean(),
});

/**
 * Template entity schema
 */
export const templateEntitySchema = z.object({
  id: z.string(),
  templateName: z.string().min(1),
  module: z.string().min(1),
  category: z.string().min(1),
  subjectTemplate: z.string(),
  contentTemplate: z.string().min(1),
  defaultLayoutName: z.string().nullable().optional(),
  isActive: z.boolean(),
  description: z.string().nullable().optional(),
  metadata: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

/**
 * Layout entity schema
 */
export const layoutEntitySchema = z.object({
  id: z.string(),
  layoutName: z.string().min(1),
  module: z.string().min(1),
  category: z.string().min(1),
  layoutContent: z.string().min(1),
  isActive: z.boolean(),
  isDefault: z.boolean(),
  description: z.string().nullable().optional(),
  metadata: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});
