/**
 * AI Module Zod Schemas
 */

import { z } from 'zod';

export const threadStatusSchema = z.number().int().min(0).max(2);
export const messageRoleSchema = z.number().int().min(0).max(3);
export const toolCallStatusSchema = z.number().int().min(0).max(3);
export const providerFeatureSchema = z.enum([
  'chat',
  'vision',
  'tools',
  'streaming',
  'embeddings',
  'fineTuning',
]);

export const agentDtoSchema = z.object({
  id: z.string(),
  name: z.string().min(1),
  provider: z.string().min(1),
  model: z.string().nullable().optional(),
  isEnabled: z.boolean(),
  isDefault: z.boolean(),
  description: z.string().nullable().optional(),
  instructions: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const createAgentSchema = z.object({
  name: z.string().min(1).max(100),
  provider: z.string().min(1).max(100),
  model: z.string().max(100).optional(),
  description: z.string().max(500).optional(),
  instructions: z.string().max(5000).optional(),
  toolGroups: z.array(z.string()).optional(),
  temperature: z.number().min(0).max(2).optional(),
  maxTokens: z.number().int().positive().optional(),
  timeoutSeconds: z.number().int().positive().optional(),
  isEnabled: z.boolean().optional(),
  isDefault: z.boolean().optional(),
  metadata: z.record(z.unknown()).optional(),
});

export const runAgentSchema = z.object({
  message: z.string().min(1),
  threadId: z.string().optional(),
  userId: z.string().optional(),
  context: z.record(z.unknown()).optional(),
});

export const threadDtoSchema = z.object({
  id: z.string(),
  agentId: z.string(),
  userId: z.string(),
  title: z.string().nullable().optional(),
  status: threadStatusSchema,
  messageCount: z.number().int().nonnegative(),
  totalTokens: z.number().int().nonnegative(),
  createdAt: z.union([z.date(), z.string()]),
});
