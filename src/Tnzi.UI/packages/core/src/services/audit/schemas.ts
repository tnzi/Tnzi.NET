/**
 * Audit Module Schemas - Zod validation schemas
 */

import { z } from 'zod';
import { AuditResultType } from './metadata';
import { sortedPagedQuerySchema } from '../../schemas/common';

/**
 * Audit Log Query Schema
 */
export const auditLogQuerySchema = sortedPagedQuerySchema.extend({
  functionName: z.string().optional(),
  permissionName: z.string().optional(),
  userId: z.string().optional(),
  userName: z.string().optional(),
  resultType: z.nativeEnum(AuditResultType).optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  ipAddress: z.string().optional(),
  httpMethod: z.string().optional(),
  httpPath: z.string().optional(),
  minDuration: z.number().optional(),
  maxDuration: z.number().optional(),
  httpStatusCode: z.number().optional(),
  correlationId: z.string().optional(),
});

/**
 * Audit Config Schema
 */
export const auditConfigSchema = z.object({
  isEnabled: z.boolean(),
  logAllOperations: z.boolean(),
  logParameters: z.boolean(),
  logReturnValue: z.boolean(),
  logEntityChanges: z.boolean(),
  maxParameterLength: z.number().min(0),
  maxReturnValueLength: z.number().min(0),
  ignoredFunctions: z.array(z.string()),
  ignoredEntities: z.array(z.string()),
  retentionDays: z.number().min(0),
});

/**
 * Export Audit Logs Schema
 */
export const exportAuditLogsSchema = z.object({
  query: auditLogQuerySchema,
  format: z.enum(['csv', 'excel', 'json']),
  includeEntityChanges: z.boolean(),
  includeStackTrace: z.boolean(),
});
