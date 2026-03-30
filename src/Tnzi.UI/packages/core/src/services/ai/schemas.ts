/**
 * AI Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const agentExecutionModeSchema = z.number().int().min(0).max(4);
export const agentRunStatusSchema = z.number().int().min(0).max(5);
export const agentRunNodeStatusSchema = z.number().int().min(0).max(7);
export const workflowExecutionModeSchema = z.number().int().min(0).max(2);
export const workflowExecutionStatusSchema = z.number().int().min(0).max(4);
export const evaluationRunStatusSchema = z.number().int().min(0).max(2);
export const usageGranularitySchema = z.number().int().min(0).max(2);
export const quotaWarningLevelSchema = z.number().int().min(0).max(2);
export const skillScopeSchema = z.number().int().min(0).max(2);
export const skillSourceSchema = z.number().int().min(0).max(1);
export const reasoningEffortSchema = z.number().int().min(0).max(3);

// ============================================
// Agent Schemas
// ============================================

export const agentDtoSchema = z.object({
  id: z.string(),
  name: z.string().min(1),
  provider: z.string().min(1),
  model: z.string().nullable().optional(),
  isEnabled: z.boolean(),
  executionMode: agentExecutionModeSchema,
  description: z.string().nullable().optional(),
  instructions: z.string().nullable().optional(),
  creationTime: z.string(),
  lastModificationTime: z.string().nullable().optional(),
});

export const createAgentSchema = z.object({
  name: z.string().min(1).max(200),
  provider: z.string().min(1).max(50),
  model: z.string().max(100).optional(),
  description: z.string().max(1000).optional(),
  instructions: z.string().max(4000).optional(),
  toolGroups: z.array(z.string()).optional(),
  temperature: z.number().min(0).max(2).optional(),
  maxTokens: z.number().int().positive().optional(),
  timeoutSeconds: z.number().int().positive().optional(),
  isEnabled: z.boolean().optional(),
  executionMode: agentExecutionModeSchema.optional(),
});

export const runAgentSchema = z.object({
  message: z.string().max(10000).optional(),
  threadId: z.string().optional(),
  userId: z.string().optional(),
});

// ============================================
// Chat Schemas
// ============================================

export const chatRequestSchema = z.object({
  message: z.string().max(10000).optional(),
  agentId: z.string().optional(),
  threadId: z.string().optional(),
  provider: z.string().max(50).optional(),
  model: z.string().max(100).optional(),
  userId: z.string().optional(),
});

// ============================================
// Thread Schemas
// ============================================

export const threadListQuerySchema = z.object({
  agentId: z.string().optional(),
  keyword: z.string().max(100).optional(),
  startTime: z.string().optional(),
  endTime: z.string().optional(),
});

export const updateThreadTitleSchema = z.object({
  title: z.string().min(1).max(200),
});

export const messageFeedbackSchema = z.object({
  rating: z.boolean(),
  tags: z.array(z.string()).optional(),
  comment: z.string().max(2000).optional(),
});

// ============================================
// Skill Schemas
// ============================================

export const createSkillSchema = z.object({
  slug: z.string().min(1).max(64),
  name: z.string().min(1).max(200),
  content: z.string().min(1),
  description: z.string().max(1000).optional(),
  whenToUse: z.string().max(2000).optional(),
  enabled: z.boolean().optional(),
});

export const updateSkillSchema = z.object({
  name: z.string().max(200).optional(),
  description: z.string().max(1000).optional(),
  content: z.string().optional(),
  enabled: z.boolean().optional(),
});

// ============================================
// Workflow Schemas
// ============================================

export const createWorkflowSchema = z.object({
  name: z.string().min(1).max(200),
  description: z.string().max(1000).optional(),
  steps: z.array(z.object({
    stepId: z.string().optional(),
    agentId: z.string().optional(),
    order: z.number().int(),
  })),
  executionMode: workflowExecutionModeSchema.optional(),
  isEnabled: z.boolean().optional(),
});

export const runWorkflowSchema = z.object({
  input: z.string().min(1).max(10000),
  userId: z.string().optional(),
});

// ============================================
// Quota Schemas
// ============================================

export const setQuotaSchema = z.object({
  userId: z.string(),
  dailyTokenLimit: z.number().int().positive(),
  monthlyTokenLimit: z.number().int().positive(),
  warningThreshold: z.number().min(0).max(1).optional(),
  criticalThreshold: z.number().min(0).max(1).optional(),
});

export const resetQuotaSchema = z.object({
  userId: z.string(),
  resetDaily: z.boolean(),
  resetMonthly: z.boolean(),
});
