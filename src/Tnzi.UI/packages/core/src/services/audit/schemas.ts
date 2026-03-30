/**
 * Audit Module Schemas - Zod validation schemas
 * Aligned with backend Tnzi.Audit DTOs
 */

import { z } from 'zod';
import { AuditResultType, AuditTrendGroupBy } from './metadata';
import { pagedQuerySchema } from '../../schemas/common';

/**
 * Audit Operation Query Schema (maps to backend AuditOperationQueryDto)
 */
export const auditOperationQuerySchema = pagedQuerySchema.extend({
  functionName: z.string().max(200).optional(),
  permissionName: z.string().max(200).optional(),
  userId: z.string().optional(),
  resultType: z.nativeEnum(AuditResultType).optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  ip: z.string().max(50).optional(),
});

/**
 * Audit Trend Query Schema
 */
export const auditTrendQuerySchema = z.object({
  startDate: z.union([z.date(), z.string()]),
  endDate: z.union([z.date(), z.string()]),
  groupBy: z.nativeEnum(AuditTrendGroupBy).optional(),
});

/**
 * Top N Query Schema (shared by top-functions and top-users)
 */
export const auditTopNQuerySchema = z.object({
  topN: z.number().min(1).max(100).optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
});

/** @deprecated Use auditOperationQuerySchema instead */
export const auditLogQuerySchema = auditOperationQuerySchema;
