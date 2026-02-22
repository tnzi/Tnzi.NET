/**
 * AI Module Types - AI Agent and chatbot integration
 * Aligned with Tnzi.NET backend AI module
 */

import type { AuditedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import {
  ThreadStatus,
  MessageRole,
  ToolCallStatus,
  ProviderFeature,
} from './metadata';

export {
  ThreadStatus,
  MessageRole,
  ToolCallStatus,
  ProviderFeature,
};

// ============================================
// Agent Types
// ============================================

/**
 * Agent DTO
 */
export interface AgentDto extends AuditedEntity<string> {
  name: string;
  description?: string | null;
  instructions?: string | null;
  provider: string;
  model?: string | null;
  toolGroups?: string[] | null;
  temperature?: number | null;
  maxTokens?: number | null;
  timeoutSeconds?: number | null;
  isEnabled: boolean;
  isDefault: boolean;
  metadata?: Record<string, unknown> | null;
}

/**
 * Agent list item
 */
export interface AgentListItemDto {
  id: string;
  name: string;
  description?: string | null;
  provider: string;
  model?: string | null;
  isEnabled: boolean;
  isDefault: boolean;
  creationTime: Date | string;
}

/**
 * Create agent request
 */
export interface CreateAgentDto {
  name: string;
  description?: string;
  instructions?: string;
  provider: string;
  model?: string;
  toolGroups?: string[];
  temperature?: number;
  maxTokens?: number;
  timeoutSeconds?: number;
  isEnabled?: boolean;
  isDefault?: boolean;
  metadata?: Record<string, unknown>;
}

/**
 * Update agent request
 */
export interface UpdateAgentDto {
  name?: string;
  description?: string;
  instructions?: string;
  model?: string;
  toolGroups?: string[];
  temperature?: number;
  maxTokens?: number;
  timeoutSeconds?: number;
  isEnabled?: boolean;
  isDefault?: boolean;
  metadata?: Record<string, unknown>;
}

/**
 * Agent query parameters
 */
export interface AgentQueryDto extends SortedPagedQueryDto {
  provider?: string;
  isEnabled?: boolean;
  keyword?: string;
}

// ============================================
// Conversation Types
// ============================================

/**
 * Thread (conversation) DTO
 */
export interface ThreadDto {
  id: string;
  agentId: string;
  agentName?: string | null;
  userId: string;
  title?: string | null;
  status: ThreadStatus;
  messageCount: number;
  totalTokens: number;
  lastMessageAt?: Date | string | null;
  createdAt: Date | string;
  metadata?: Record<string, unknown> | null;
}

/**
 * Thread message DTO
 */
export interface ThreadMessageDto {
  id: string;
  threadId: string;
  role: MessageRole;
  content: string;
  tokenCount?: number | null;
  model?: string | null;
  finishReason?: string | null;
  createdAt: Date | string;
  metadata?: Record<string, unknown> | null;
  toolCalls?: ToolCallDto[];
}

/**
 * Tool call DTO
 */
export interface ToolCallDto {
  id: string;
  type: string;
  name: string;
  arguments: string;
  result?: string | null;
  status: ToolCallStatus;
}

/**
 * Thread query parameters
 */
export interface ThreadQueryDto extends SortedPagedQueryDto {
  agentId?: string;
  userId?: string;
  status?: ThreadStatus;
  startDate?: Date | string;
  endDate?: Date | string;
  keyword?: string;
}

// ============================================
// Run Agent Types
// ============================================

/**
 * Run agent request
 */
export interface RunAgentDto {
  message: string;
  threadId?: string;
  userId?: string;
  context?: Record<string, unknown>;
}

/**
 * Agent response DTO
 */
export interface AgentResponseDto {
  threadId: string;
  messageId: string;
  content: string;
  finishReason?: string | null;
  model?: string | null;
  usage?: TokenUsageDto;
  toolCalls?: ToolCallDto[];
}

/**
 * Streaming agent response (SSE)
 */
export interface StreamingAgentResponseDto {
  threadId: string;
  messageId: string;
  delta: string;
  finishReason?: string | null;
  toolCalls?: ToolCallDto[];
  isComplete: boolean;
}

/**
 * Token usage DTO
 */
export interface TokenUsageDto {
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
}

// ============================================
// Tool Types
// ============================================

/**
 * Tool definition DTO
 */
export interface ToolDefinitionDto {
  name: string;
  description?: string | null;
  parameters: string; // JSON Schema string
  isEnabled: boolean;
  groupName?: string | null;
}

/**
 * Tool group DTO
 */
export interface ToolGroupDto {
  name: string;
  displayName: string;
  description?: string | null;
  tools: ToolDefinitionDto[];
  isEnabled: boolean;
}

/**
 * Tool execution request
 */
export interface ToolExecutionDto {
  toolName: string;
  arguments: string; // JSON string
  threadId?: string;
}

/**
 * Tool execution result
 */
export interface ToolExecutionResultDto {
  toolCallId: string;
  success: boolean;
  result?: string | null;
  error?: string | null;
  executionTime: number;
}

// ============================================
// Provider Types
// ============================================

/**
 * AI provider DTO
 */
export interface AiProviderDto {
  name: string;
  displayName: string;
  type: string;
  isEnabled: boolean;
  models: AiModelDto[];
  defaultModel?: string | null;
  features: ProviderFeature[];
}

/**
 * AI model DTO
 */
export interface AiModelDto {
  id: string;
  name: string;
  displayName: string;
  contextWindow: number;
  maxOutputTokens: number;
  supportsVision: boolean;
  supportsTools: boolean;
  supportsStreaming: boolean;
  inputPrice: number; // per 1M tokens
  outputPrice: number; // per 1M tokens
}

// ============================================
// Statistics Types
// ============================================

/**
 * AI usage statistics
 */
export interface AiUsageStatisticsDto {
  totalRequests: number;
  totalTokens: number;
  totalCost: number;
  byProvider: Record<string, ProviderStatistics>;
  byModel: Record<string, ModelStatistics>;
  dailyStats: DailyAiStats[];
}

/**
 * Provider statistics
 */
export interface ProviderStatistics {
  requests: number;
  tokens: number;
  cost: number;
  args?: Record<string, unknown>;
}

/**
 * Model statistics
 */
export interface ModelStatistics {
  requests: number;
  promptTokens: number;
  completionTokens: number;
  cost: number;
  avgLatency: number;
}

/**
 * Daily AI statistics
 */
export interface DailyAiStats {
  date: string;
  requests: number;
  tokens: number;
  cost: number;
  avgLatency: number;
}
