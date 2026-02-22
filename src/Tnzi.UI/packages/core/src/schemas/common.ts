/**
 * Common Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Basic Schemas
// ============================================

export const uuidSchema = z.string().uuid();
export const objectIdSchema = z.string().min(1);
export const idSchema = z.union([z.string().min(1), z.number().int().positive()]);

export const dateStringSchema = z.string().datetime();
export const dateSchema = z.union([z.date(), dateStringSchema]);

export const positiveNumberSchema = z.number().positive();
export const nonNegativeNumberSchema = z.number().nonnegative();

// ============================================
// Entity Schemas
// ============================================

export const entityBaseSchema = z.object({
  id: idSchema,
});

export const creationAuditedEntitySchema = entityBaseSchema.extend({
  creationTime: dateSchema,
});

export const auditedEntitySchema = creationAuditedEntitySchema.extend({
  lastModificationTime: dateSchema.nullable().optional(),
});

export const fullAuditedEntitySchema = auditedEntitySchema.extend({
  creatorId: z.string().nullable().optional(),
  lastModifierId: z.string().nullable().optional(),
  isDeleted: z.boolean(),
  deleterId: z.string().nullable().optional(),
  deletionTime: dateSchema.nullable().optional(),
  tenantId: z.string().nullable().optional(),
});

// ============================================
// Pagination Schemas
// ============================================

export const pagedQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
});

export const sortedPagedQuerySchema = pagedQuerySchema.extend({
  sortBy: z.string().optional(),
  sortDirection: z.enum(['asc', 'desc', 'ascending', 'descending']).optional(),
});

export const searchPagedQuerySchema = pagedQuerySchema.extend({
  keyword: z.string().optional(),
});

export const fullPagedQuerySchema = sortedPagedQuerySchema.merge(searchPagedQuerySchema).extend({
  startDate: dateSchema.optional(),
  endDate: dateSchema.optional(),
});

export function pagedListSchema<T extends z.ZodTypeAny>(itemSchema: T) {
  return z.object({
    pageIndex: z.number().int(),
    pageSize: z.number().int(),
    totalCount: z.number().int(),
    totalPages: z.number().int(),
    hasPreviousPage: z.boolean(),
    hasNextPage: z.boolean(),
    items: z.array(itemSchema),
  });
}

// ============================================
// API Result Schemas
// ============================================

export function apiResultSchema<T extends z.ZodTypeAny>(dataSchema: T) {
  return z.object({
    succeeded: z.boolean(),
    message: z.string().optional(),
    code: z.number().optional(),
    errorCode: z.string().optional(),
    errorDetails: z.record(z.unknown()).optional(),
    data: dataSchema,
    Code: z.number(),
    Success: z.boolean(),
  });
}

// ============================================
// Utility Schemas
// ============================================

export const selectOptionSchema = z.object({
  label: z.string(),
  value: z.union([z.string(), z.number()]),
  disabled: z.boolean().optional(),
  description: z.string().optional(),
  icon: z.string().optional(),
});

export const keyValueSchema = z.object({
  key: z.string(),
  value: z.unknown(),
});

export const nameValueSchema = z.object({
  name: z.string(),
  value: z.unknown(),
});
