/**
 * AI Module Types - Aligned with Tnzi.NET backend AI module
 *
 * Covers: Agents, Chat, Threads, Agent Runs, Skills, Workflows,
 * Evaluations, Quotas, Usage Analytics, MCP, MCP Tool Analytics,
 * Providers, Personas, Skill Categories, Artifacts, User Profile
 */

import type { PagedQueryDto } from '../../types/pagination';

// ============================================
// Enums
// ============================================

/** Agent execution mode */
export enum AgentExecutionMode {
  Single = 0,
  Handoff = 1,
  AgentAsTools = 2,
  Router = 3,
}

/** Agent run status */
export enum AgentRunStatus {
  Pending = 0,
  Running = 1,
  AwaitingApproval = 2,
  Completed = 3,
  Failed = 4,
  Cancelled = 5,
}

/** Agent run node status */
export enum AgentRunNodeStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
  Skipped = 4,
  AwaitingApproval = 5,
  Approved = 6,
  Rejected = 7,
}

/** Workflow execution mode */
export enum WorkflowExecutionMode {
  Sequential = 0,
  Parallel = 1,
  Dag = 2,
}

/** Workflow execution status */
export enum WorkflowExecutionStatus {
  Running = 0,
  Completed = 1,
  Failed = 2,
  Paused = 3,
  AwaitingApproval = 4,
}

/** Evaluation run status */
export enum EvaluationRunStatus {
  Running = 0,
  Completed = 1,
  Failed = 2,
}

/** Usage analytics time granularity */
export enum UsageGranularity {
  Daily = 0,
  Weekly = 1,
  Monthly = 2,
}

/** Quota warning level */
export enum QuotaWarningLevel {
  None = 0,
  Warning = 1,
  Critical = 2,
}

/** Skill scope */
export enum SkillScope {
  System = 0,
  Tenant = 1,
  User = 2,
}

/**
 * Shared-resource visibility scope (Provider / Persona).
 * System rows are shared across tenants; Tenant rows are tenant-private.
 * Mirrors the backend ResourceScope enum (serialized as numbers).
 */
export enum ResourceScope {
  System = 0,
  Tenant = 1,
  User = 2,
}

/** Skill source */
export enum SkillSource {
  FileSystem = 0,
  Database = 1,
  Plugin = 2,
  Managed = 3,
  Project = 4,
}

/** Reasoning effort */
export enum ReasoningEffort {
  None = 0,
  Low = 1,
  Medium = 2,
  High = 3,
}

// ============================================
// Agent Types
// ============================================

/** Agent DTO */
export interface AgentDto {
  id: string;
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
  executionMode: AgentExecutionMode;
  executionConfig?: AgentExecutionConfigDto | null;
  domains?: string[] | null;
  roles?: string[] | null;
  qualityTier: number;
  latencyTier: number;
  costTier: number;
  /** Persona FK — links the agent to an AgentPersona's soul / role template. */
  personaId?: string | null;
  /** Assigned knowledge base IDs (RAG). Retrieval is scoped to these at runtime. */
  knowledgeBaseIds?: string[] | null;
  /** Assigned skill slugs. Only these skills are visible to the agent at runtime. */
  skillSlugs?: string[] | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Agent execution configuration */
export interface AgentExecutionConfigDto {
  handoff?: HandoffExecutionConfigDto | null;
  router?: RouterExecutionConfigDto | null;
  agentAsTools?: AgentAsToolsExecutionConfigDto | null;
}

export interface HandoffExecutionConfigDto {
  targets: Record<string, string>;
  maxHandoffs?: number | null;
  allowReturnToSource?: boolean | null;
}

export interface RouterExecutionConfigDto {
  targets: Record<string, string>;
  allowDirectResponse?: boolean | null;
}

export interface AgentAsToolsExecutionConfigDto {
  agents: Record<string, string>;
}

/** Create agent request */
export interface CreateAgentDto {
  name: string;
  description?: string | null;
  instructions?: string | null;
  provider: string;
  model?: string | null;
  toolGroups?: string[] | null;
  temperature?: number | null;
  maxTokens?: number | null;
  timeoutSeconds?: number | null;
  isEnabled?: boolean;
  executionMode?: AgentExecutionMode;
  executionConfig?: AgentExecutionConfigDto | null;
  domains?: string[] | null;
  roles?: string[] | null;
  qualityTier?: number;
  latencyTier?: number;
  costTier?: number;
  /** Persona FK (optional). */
  personaId?: string | null;
  /** Assigned knowledge base IDs (RAG). */
  knowledgeBaseIds?: string[] | null;
  /** Assigned skill slugs. */
  skillSlugs?: string[] | null;
}

/** Update agent request */
export interface UpdateAgentDto {
  name?: string | null;
  description?: string | null;
  instructions?: string | null;
  provider?: string | null;
  model?: string | null;
  toolGroups?: string[] | null;
  temperature?: number | null;
  maxTokens?: number | null;
  timeoutSeconds?: number | null;
  isEnabled?: boolean | null;
  executionMode?: AgentExecutionMode | null;
  executionConfig?: AgentExecutionConfigDto | null;
  domains?: string[] | null;
  roles?: string[] | null;
  qualityTier?: number | null;
  latencyTier?: number | null;
  costTier?: number | null;
  /**
   * Persona FK. Pass a uuid to link, pass an empty-guid string
   * ("00000000-0000-0000-0000-000000000000") to unlink, omit to leave unchanged.
   */
  personaId?: string | null;
  /** Assigned knowledge base IDs (RAG). Pass an empty array to clear all. */
  knowledgeBaseIds?: string[] | null;
  /** Assigned skill slugs. Pass an empty array to clear all. */
  skillSlugs?: string[] | null;
  changeNote?: string | null;
}

/** Tool group catalog entry (assignable tool groups for an agent). */
export interface ToolGroupDto {
  name: string;
  toolCount: number;
  toolNames: string[];
}

/** Agent memory entry (admin-curated long-term memory). */
export interface AgentMemoryDto {
  id: string;
  content: string;
  category?: string | null;
  importance: number;
  source?: string | null;
  accessCount: number;
  lastAccessedTime?: string | null;
  creationTime: string;
}

/** Agent memory list query. */
export interface AgentMemoryListQueryDto extends PagedQueryDto {
  category?: string | null;
  keyword?: string | null;
}

/** Create agent memory request. */
export interface CreateAgentMemoryDto {
  content: string;
  category?: string | null;
  importance?: number;
}

/** Update agent memory request. */
export interface UpdateAgentMemoryDto {
  content?: string | null;
  category?: string | null;
  importance?: number | null;
}

/** Agent list query parameters */
export interface AgentListQueryDto extends PagedQueryDto {
  keyword?: string | null;
  provider?: string | null;
  isEnabled?: boolean | null;
  executionMode?: AgentExecutionMode | null;
  domain?: string | null;
  role?: string | null;
  minQualityTier?: number | null;
  maxLatencyTier?: number | null;
  maxCostTier?: number | null;
}

/** Run agent request */
export interface RunAgentRequestDto {
  message?: string | null;
  content?: ContentPartDto[] | null;
  threadId?: string | null;
  userId?: string | null;
}

/** Agent response */
export interface AgentResponseDto {
  content: string;
  finishReason?: string | null;
  model?: string | null;
  usage?: TokenUsageDto | null;
  citations?: CitationDto[] | null;
  handoffPath?: string[] | null;
  finalAgentName?: string | null;
  reasoning?: string | null;
}

/** Token usage */
export interface TokenUsageDto {
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  cachedInputTokens: number;
  cacheCreationTokens: number;
}

/** RAG citation */
export interface CitationDto {
  sourceName?: string | null;
  sourceLink?: string | null;
  text: string;
  score?: number | null;
}

/** Agent version snapshot */
export interface AgentVersionDto {
  id: string;
  agentId: string;
  version: number;
  changeNote?: string | null;
  configSnapshot: string;
  creationTime: string;
}

/** Agent version query */
export interface AgentVersionQueryDto extends PagedQueryDto {}

/** Agent validation result */
export interface AgentValidationResultDto {
  agentId: string;
  agentName: string;
  isValid: boolean;
  checks: ValidationCheckDto[];
}

export interface ValidationCheckDto {
  name: string;
  passed: boolean;
  details?: string | null;
}

/** Agent health summary */
export interface AgentHealthSummaryDto {
  totalAgents: number;
  healthyAgents: number;
  unhealthyAgents: number;
  disabledAgents: number;
  unhealthyDetails: AgentValidationResultDto[];
}

/**
 * Configure A/B test request.
 * Backend stores this in `Agent.Configuration` JSON; routes a `trafficPercentB`
 * slice of traffic to version B (experiment) vs version A (control).
 */
export interface ConfigureAbTestDto {
  /** Version number for variant A (control group). */
  versionA: number;
  /** Version number for variant B (experiment group). */
  versionB: number;
  /** Traffic percentage routed to variant B (0-100). */
  trafficPercentB: number;
}

// ============================================
// Chat Types
// ============================================

/** Chat request */
export interface ChatRequestDto {
  message?: string | null;
  content?: ContentPartDto[] | null;
  agentId?: string | null;
  threadId?: string | null;
  provider?: string | null;
  model?: string | null;
  toolGroups?: string[] | null;
  userId?: string | null;
  reasoningEffort?: ReasoningEffort | null;
}

/** Chat response */
export interface ChatResponseDto {
  content: string;
  finishReason?: string | null;
  model?: string | null;
  usage?: TokenUsageDto | null;
  threadId?: string | null;
  handoffPath?: string[] | null;
  citations?: CitationDto[] | null;
  reasoning?: string | null;
  /** Persisted user message ID for this turn (null when persistence was skipped). */
  userMessageId?: string | null;
  /** Persisted assistant message ID for this turn (null when persistence was skipped). */
  assistantMessageId?: string | null;
}

/** Tool call detail (name + duration) */
export interface ToolCallDetailDto {
  name: string;
  durationMs?: number | null;
  input?: string | null;
  output?: string | null;
}

/** Component reference for rich frontend rendering */
export interface ComponentRefDto {
  type: string;
  props?: Record<string, unknown> | null;
}

/**
 * Chat stream event — SSE delta model (each event contains incremental content, not cumulative).
 * Maps to backend StreamEvent class.
 */
export interface ChatStreamEvent {
  /** Incremental text (non-cumulative) */
  delta?: string | null;
  /** Reasoning incremental text (e.g., DeepSeek-R1 thinking process) */
  reasoningDelta?: string | null;
  /** Finish reason (stop, error, guardrail_rejected, etc.) */
  finishReason?: string | null;
  /** Model used */
  model?: string | null;
  /** Thread ID */
  threadId?: string | null;
  /** Token usage (only in final event) */
  usage?: TokenUsageDto | null;
  /** Whether this is the terminal event */
  isDone?: boolean;
  /** Whether this is an error event */
  isError?: boolean;
  /** Error message */
  errorMessage?: string | null;
  /** Error code */
  errorCode?: string | null;
  /** Whether a tool call is in progress (for heartbeat/loading UI) */
  isToolCall?: boolean;
  /** Tool names being called */
  toolCallNames?: string[] | null;
  /** Tool call details */
  toolCalls?: ToolCallDetailDto[] | null;
  /** Agent name change (Handoff scenario) */
  agentName?: string | null;
  /** RAG citations (only when isDone=true) */
  citations?: CitationDto[] | null;
  /** Workflow phase */
  phase?: string | null;
  /** Workflow node type */
  nodeType?: string | null;
  /** Workflow node ID */
  nodeId?: string | null;
  /** Workflow node name */
  nodeName?: string | null;
  /** Worker agent name */
  workerName?: string | null;
  /** Review verdict */
  reviewVerdict?: string | null;
  /** Awaiting human approval */
  awaitingApproval?: boolean;
  /** Associated run ID */
  runId?: string | null;
  /** Rich component reference */
  componentRef?: ComponentRefDto | null;
  /** Persisted user message ID (populated on the terminal event when the turn is persisted; null when persistence was skipped). */
  userMessageId?: string | null;
  /** Persisted assistant message ID (populated on the terminal event when the turn is persisted). Clients use this for message-scoped APIs such as feedback submission. */
  assistantMessageId?: string | null;
}

/** Content part (polymorphic: text | image | file) */
export interface ContentPartDto {
  type: 'text' | 'image' | 'file';
}

export interface TextContentPartDto extends ContentPartDto {
  type: 'text';
  text: string;
}

export interface ImageContentPartDto extends ContentPartDto {
  type: 'image';
  url?: string | null;
  base64Data?: string | null;
  mediaType?: string | null;
}

export interface FileContentPartDto extends ContentPartDto {
  type: 'file';
  fileId: string;
  fileName?: string | null;
}

// ============================================
// Thread Types
// ============================================

/** Agent thread */
export interface AgentThreadDto {
  id: string;
  agentId?: string | null;
  /** Resolved agent display name; null when the agent was deleted or the thread is agent-less. */
  agentName?: string | null;
  title?: string | null;
  messageCount: number;
  lastActivityTime: string;
  creationTime: string;
}

/** Create thread request */
export interface CreateAgentThreadDto {
  agentId?: string | null;
  title?: string | null;
}

/** Thread list query */
export interface ThreadListQueryDto extends PagedQueryDto {
  agentId?: string | null;
  keyword?: string | null;
  startTime?: string | null;
  endTime?: string | null;
}

/** Update thread title request */
export interface UpdateThreadTitleDto {
  title: string;
}

/** Thread detail with messages */
export interface AgentThreadDetailDto {
  id: string;
  agentId?: string | null;
  agentName?: string | null;
  title?: string | null;
  metadata?: string | null;
  messageCount: number;
  lastActivityTime: string;
  creationTime: string;
  messages: ThreadMessageDto[];
}

/** Thread message */
export interface ThreadMessageDto {
  id: string;
  role: string;
  content: string;
  toolCalls?: string | null;
  usage?: string | null;
  order: number;
  creationTime: string;
  componentRefs?: string | null;
  feedbackRating?: boolean | null;
  feedbackTags?: string | null;
  feedbackComment?: string | null;
  feedbackTime?: string | null;
}

/** Thread export data */
export interface ThreadExportDto {
  id: string;
  agentId?: string | null;
  agentName?: string | null;
  title?: string | null;
  metadata?: string | null;
  messageCount: number;
  lastActivityTime: string;
  creationTime: string;
  exportedAt: string;
  messages: ThreadMessageDto[];
}

// ============================================
// Message Feedback Types
// ============================================

/** Message feedback submission */
export interface MessageFeedbackDto {
  rating: boolean;
  tags?: string[] | null;
  comment?: string | null;
}

/** Agent feedback statistics */
export interface AgentFeedbackStatsDto {
  agentId: string;
  agentName: string;
  totalRated: number;
  positiveCount: number;
  negativeCount: number;
  positiveRate: number;
  tagDistribution: Record<string, number>;
}

// ============================================
// Agent Run Types
// ============================================

/** Agent run record */
export interface AgentRunDto {
  id: string;
  agentId?: string | null;
  threadId?: string | null;
  workflowDefinitionId?: string | null;
  workflowExecutionId?: string | null;
  status: AgentRunStatus;
  executionMode: AgentExecutionMode;
  inputSummary: string;
  outputSummary?: string | null;
  totalInputTokens: number;
  totalOutputTokens: number;
  durationMs: number;
  error?: string | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Agent run node */
export interface AgentRunNodeDto {
  id: string;
  runId: string;
  nodeType: string;
  nodeName: string;
  agentId?: string | null;
  status: AgentRunNodeStatus;
  inputSummary?: string | null;
  output?: string | null;
  inputTokens: number;
  outputTokens: number;
  durationMs: number;
  error?: string | null;
  retryCount: number;
  orderIndex: number;
  creationTime: string;
}

/** Agent run trace */
export interface AgentRunTraceDto {
  id: string;
  runId: string;
  nodeId?: string | null;
  eventType: string;
  eventData?: string | null;
  durationMs: number;
  creationTime: string;
}

/** Agent run query */
export interface AgentRunQueryDto extends PagedQueryDto {
  agentId?: string | null;
  workflowDefinitionId?: string | null;
  status?: AgentRunStatus | null;
  executionMode?: AgentExecutionMode | null;
  startTime?: string | null;
  endTime?: string | null;
}

/** Agent run statistics */
export interface AgentRunStatsDto {
  totalRuns: number;
  pendingRuns: number;
  runningRuns: number;
  awaitingApprovalRuns: number;
  completedRuns: number;
  failedRuns: number;
  cancelledRuns: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  averageDurationMs: number;
  successRate: number;
  timestamp: string;
}

/** Approve run request */
export interface ApproveRunDto {
  comment?: string | null;
}

/** Reject run request */
export interface RejectRunDto {
  comment?: string | null;
}

// ============================================
// Quota Types
// ============================================

/** User quota */
export interface UserQuotaDto {
  id: string;
  userId: string;
  dailyTokenLimit: number;
  monthlyTokenLimit: number;
  currentDailyUsage: number;
  currentMonthlyUsage: number;
  remainingDailyQuota: number;
  remainingMonthlyQuota: number;
  dailyUsagePercentage: number;
  monthlyUsagePercentage: number;
  lastResetDate: string;
  isEnabled: boolean;
  warningThreshold: number;
  criticalThreshold: number;
  warningLevel: QuotaWarningLevel;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Set quota request */
export interface SetQuotaDto {
  userId: string;
  dailyTokenLimit: number;
  monthlyTokenLimit: number;
  warningThreshold?: number | null;
  criticalThreshold?: number | null;
}

/** Reset quota request */
export interface ResetQuotaDto {
  userId: string;
  resetDaily: boolean;
  resetMonthly: boolean;
}

/** User quota paged query */
export interface UserQuotaQueryDto extends PagedQueryDto {
  userId?: string | null;
  isEnabled?: boolean | null;
}

/** Budget check status (USD cost budget). 0=WithinBudget / 1=WarningThreshold / 2=BudgetExceeded */
export type BudgetStatus = 0 | 1 | 2;

/** Per-agent USD spend breakdown inside a budget summary. */
export interface AgentSpendDto {
  agentId?: string | null;
  agentName: string;
  spendUsd: number;
  /** Per-agent budget cap when `PerAgentBudgets` is configured. */
  agentBudgetLimitUsd?: number | null;
  requestCount: number;
}

/** USD cost budget summary for a tenant/time-range (advisory, UsageLog-aggregated). */
export interface BudgetSummaryDto {
  periodStart: string;
  periodEnd: string;
  currentSpendUsd: number;
  budgetLimitUsd: number;
  /** Usage ratio 0-1. */
  usagePercentage: number;
  status: BudgetStatus;
  byAgent: AgentSpendDto[];
}

// ============================================
// Usage Analytics Types
// ============================================

/** Usage summary */
export interface UsageSummaryDto {
  totalRequests: number;
  successfulRequests: number;
  failedRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageDurationMs: number;
  successRate: number;
  totalEstimatedCostUsd: number;
}

/** Usage summary query */
export interface UsageSummaryQueryDto {
  startTime: string;
  endTime: string;
  provider?: string | null;
  model?: string | null;
  agentId?: string | null;
}

/** Usage log record */
export interface UsageLogDto {
  id: string;
  agentId?: string | null;
  threadId?: string | null;
  provider: string;
  model: string;
  operationType: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  durationMs: number;
  isSuccess: boolean;
  errorMessage?: string | null;
  creationTime: string;
  estimatedCostUsd?: number | null;
  cachedInputTokens: number;
  cacheCreationTokens: number;
}

/** Usage log query */
export interface UsageLogQueryDto extends PagedQueryDto {
  startTime?: string | null;
  endTime?: string | null;
  provider?: string | null;
  model?: string | null;
  operationType?: string | null;
  isSuccess?: boolean | null;
  agentId?: string | null;
}

/** Usage by provider */
export interface ProviderUsageDto {
  provider: string;
  totalRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageDurationMs: number;
  totalEstimatedCostUsd: number;
}

/** Usage by model */
export interface ModelUsageDto {
  provider: string;
  model: string;
  totalRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageDurationMs: number;
  totalEstimatedCostUsd: number;
}

/** Usage trend data point */
export interface UsageTrendPointDto {
  period: string;
  periodStart: string;
  totalRequests: number;
  successfulRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageDurationMs: number;
  totalEstimatedCostUsd: number;
}

/** Usage trend query */
export interface UsageTrendQueryDto {
  startTime: string;
  endTime: string;
  granularity?: UsageGranularity;
  provider?: string | null;
  model?: string | null;
  agentId?: string | null;
}

/** Usage by agent */
export interface AgentUsageDto {
  agentId: string;
  agentName: string;
  totalRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageDurationMs: number;
  successRate: number;
  totalEstimatedCostUsd: number;
}

/** Cost summary */
export interface CostSummaryDto {
  totalCostUsd: number;
  totalRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  averageCostPerRequest: number;
  byProvider: ProviderCostDto[];
  byModel: ModelCostDto[];
}

/** Cost by provider */
export interface ProviderCostDto {
  provider: string;
  totalCostUsd: number;
  totalRequests: number;
  costPercentage: number;
}

/** Cost by model */
export interface ModelCostDto {
  provider: string;
  model: string;
  totalCostUsd: number;
  totalRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  costPercentage: number;
}

// ============================================
// Provider Types
// ============================================

/** Provider default model info */
export interface ProviderDefaultModelDto {
  providerName: string;
  defaultModel?: string | null;
}

/**
 * Provider source — 'Database' (admin-entered entity, writable) or
 * 'Configuration' (appsettings AI:Providers, read-only: update/delete/testConnection return 400).
 */
export type ProviderSource = 'Database' | 'Configuration';

/** Provider dropdown option (enabled providers only) — for the Agent config Provider select. */
export interface ProviderOptionDto {
  id: string;
  name: string;
  providerType: string;
  defaultModel?: string | null;
  /** Source — 'Database' (entity) or 'Configuration' (appsettings AI:Providers). */
  source: ProviderSource;
}

/** Provider model list — live from /v1/models when available, else a static fallback. */
export interface ProviderModelsDto {
  models: string[];
  /** "live" = fetched from the provider's /v1/models; "fallback" = static curated list. */
  source: string;
}

/** Provider entity DTO — never exposes plaintext or ciphertext API key */
export interface ProviderDto {
  id: string;
  name: string;
  providerType: string;
  endpoint?: string | null;
  defaultModel?: string | null;
  priority: number;
  isEnabled: boolean;
  description?: string | null;
  hasApiKey: boolean;
  /** Visibility scope — System (shared across tenants) or Tenant (tenant-private). */
  scope: ResourceScope;
  /** Owning tenant — populated when scope=Tenant. */
  tenantId?: string | null;
  /**
   * Source — 'Database' (entity, writable) or 'Configuration' (appsettings
   * AI:Providers; synthetic stable id, pinned to the top of the list,
   * update/delete/testConnection rejected with 400, listModels works).
   */
  source: ProviderSource;
  /** Creation time — null for Configuration-sourced entries. */
  creationTime: string | null;
  lastModificationTime?: string | null;
}

/** Provider list paged query */
export interface ProviderQueryDto {
  pageIndex?: number;
  pageSize?: number;
  providerType?: string | null;
  isEnabled?: boolean | null;
  keyword?: string | null;
}

/** Provider create request */
export interface CreateProviderDto {
  name: string;
  providerType: string;
  endpoint?: string | null;
  apiKey?: string | null;
  defaultModel?: string | null;
  priority?: number;
  isEnabled?: boolean;
  description?: string | null;
  /** Optional explicit scope; when omitted the server infers (tenant context -> Tenant, else System). */
  scope?: ResourceScope | null;
}

/** Provider update request — apiKey: null = keep, '' = clear, non-empty = rotate */
export interface UpdateProviderDto {
  name?: string | null;
  providerType?: string | null;
  endpoint?: string | null;
  apiKey?: string | null;
  defaultModel?: string | null;
  priority?: number | null;
  isEnabled?: boolean | null;
  description?: string | null;
}

/** Provider connection test result */
export interface ProviderTestResultDto {
  success: boolean;
  message?: string | null;
  latencyMs: number;
}

// ============================================
// Evaluation Types
// ============================================

/** Evaluation run */
export interface EvaluationRunDto {
  id: string;
  agentId: string;
  caseCount: number;
  passedCount: number;
  averageScore: number;
  status: EvaluationRunStatus;
  duration: string;
  creationTime: string;
}

/** Evaluation run detail */
export interface EvaluationRunDetailDto extends EvaluationRunDto {
  resultsJson: string;
}

/** A single point in an agent's evaluation score trend. */
export interface EvaluationTrendPointDto {
  runId: string;
  /** ISO-8601 timestamp. */
  date: string;
  /** Average score (0-1). */
  score: number;
  /** Pass rate (0-1). */
  passRate: number;
}

/** Evaluation score trend across an agent's recent runs. */
export interface EvaluationTrendDto {
  agentId: string;
  points: EvaluationTrendPointDto[];
}

/** Aggregate evaluation stats for one agent version (used in A/B comparison). */
export interface VersionStatsDto {
  versionNumber: number;
  runCount: number;
  averageScore: number;
  averagePassRate: number;
  totalCases: number;
  totalPassed: number;
}

/** Side-by-side comparison of two agent versions' evaluation stats. */
export interface VersionComparisonDto {
  agentId: string;
  versionA: VersionStatsDto;
  versionB: VersionStatsDto;
  /** Score delta (B - A); positive means B scored higher. */
  scoreDelta: number;
  /** Winning version number (higher score), or null on a tie. */
  winner?: number | null;
}

/** Evaluation run query */
export interface EvaluationRunQueryDto extends PagedQueryDto {
  agentId?: string | null;
  status?: EvaluationRunStatus | null;
}

/** Evaluation case (input + optional expected output) */
export interface EvaluationCaseDto {
  input: string;
  expectedOutput?: string | null;
}

/** Create-and-run evaluation request */
export interface CreateEvaluationRunDto {
  agentId: string;
  versionNumber?: number | null;
  cases: EvaluationCaseDto[];
}

/** Batch evaluation target */
export interface BatchEvaluationTargetDto {
  agentId: string;
  versionNumber?: number | null;
}

/** Batch evaluation request */
export interface BatchEvaluationDto {
  targets: BatchEvaluationTargetDto[];
  cases: EvaluationCaseDto[];
}

/** Batch evaluation result */
export interface BatchEvaluationResultDto {
  results: EvaluationRunDetailDto[];
  totalDuration: string;
}

// ============================================
// Skill Types
// ============================================

/** Skill summary (for list display) */
export interface SkillSummaryDto {
  id: string;
  slug: string;
  scope: SkillScope;
  name: string;
  description?: string | null;
  whenToUse?: string | null;
  tags: string[];
  priority: number;
  version?: string | null;
  author?: string | null;
  enabled: boolean;
  source: SkillSource;
  /**
   * True when this row originates from a non-database source (file system /
   * plugin / managed / embedded / project). Admin UI hides Edit/Delete
   * buttons for read-only rows.
   */
  isReadOnly?: boolean;
  /**
   * Absolute path of the source SKILL.md (file-source rows only). Null for
   * database rows.
   */
  filePath?: string | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Skill detail (full content and parameters) */
export interface SkillDetailDto extends SkillSummaryDto {
  content: string;
  parameters: SkillParameterDto[];
  allowedToolGroups?: string[] | null;
  requiredModel?: string | null;
  requiredProvider?: string | null;
  requirements?: SkillRequirementsDto | null;
  ownerUserId?: string | null;
}

/** Skill parameter definition */
export interface SkillParameterDto {
  name: string;
  description?: string | null;
  defaultValue?: string | null;
  required: boolean;
  allowedValues?: string[] | null;
}

/** Skill dependency requirements */
export interface SkillRequirementsDto {
  bins: string[];
  envs: string[];
  configs: string[];
  os: string[];
}

/** Create skill request */
export interface CreateSkillDto {
  slug: string;
  scope?: SkillScope;
  name: string;
  description?: string | null;
  content: string;
  whenToUse?: string | null;
  parameters?: SkillParameterDto[] | null;
  allowedToolGroups?: string[] | null;
  allowedTools?: string[] | null;
  deniedTools?: string[] | null;
  requiredModel?: string | null;
  requiredProvider?: string | null;
  requirements?: SkillRequirementsDto | null;
  tags?: string[] | null;
  priority?: number;
  version?: string | null;
  author?: string | null;
  enabled?: boolean;
}

/** Update skill request */
export interface UpdateSkillDto {
  name?: string | null;
  description?: string | null;
  content?: string | null;
  whenToUse?: string | null;
  parameters?: SkillParameterDto[] | null;
  allowedToolGroups?: string[] | null;
  allowedTools?: string[] | null;
  deniedTools?: string[] | null;
  requiredModel?: string | null;
  requiredProvider?: string | null;
  requirements?: SkillRequirementsDto | null;
  tags?: string[] | null;
  priority?: number | null;
  version?: string | null;
  author?: string | null;
  enabled?: boolean | null;
}

/** Skill activate request */
export interface SkillActivateDto {
  parameters?: Record<string, string> | null;
}

/** Skill activation result */
export interface SkillActivationResultDto {
  slug: string;
  name: string;
  renderedContent: string;
  allowedToolGroups?: string[] | null;
  requiredModel?: string | null;
  requiredProvider?: string | null;
  warnings: string[];
}

/** Skill paged query */
export interface SkillQueryDto extends PagedQueryDto {
  keyword?: string | null;
  scope?: SkillScope | null;
  enabled?: boolean | null;
  tag?: string | null;
  sortBy?: string | null;
  sortDesc?: boolean;
  /**
   * When true, file-system / plugin / managed / project / embedded skills
   * are merged into the paged result. File-source rows carry `isReadOnly=true`.
   */
  includeFileSource?: boolean;
}

/** Skill usage statistics */
export interface SkillUsageStatsDto {
  totalSkills: number;
  enabledSkills: number;
  disabledSkills: number;
  tenantScopeSkills: number;
  userScopeSkills: number;
  totalActivations: number;
}

/** Popular skill */
export interface PopularSkillDto {
  slug: string;
  name: string;
  scope: SkillScope;
  source: SkillSource;
  activationCount: number;
  lastActivatedAt?: string | null;
}

/** Skill export format */
export interface SkillExportDto {
  slug: string;
  name: string;
  description?: string | null;
  content: string;
  whenToUse?: string | null;
  parameters?: SkillParameterDto[] | null;
  allowedToolGroups?: string[] | null;
  allowedTools?: string[] | null;
  deniedTools?: string[] | null;
  requiredModel?: string | null;
  requiredProvider?: string | null;
  requirements?: SkillRequirementsDto | null;
  tags?: string[] | null;
  priority: number;
  version?: string | null;
  author?: string | null;
  enabled: boolean;
}

/** Skill import request */
export interface SkillImportRequestDto {
  skills: SkillExportDto[];
  targetScope?: SkillScope;
}

/** Skill import result */
export interface SkillImportResultDto {
  created: number;
  updated: number;
  skipped: number;
  errors: string[];
}

// ============================================
// Workflow Types
// ============================================

/** Workflow definition */
export interface WorkflowDefinitionDto {
  id: string;
  name: string;
  description?: string | null;
  steps: WorkflowStepDto[];
  executionMode: WorkflowExecutionMode;
  isEnabled: boolean;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Create workflow request */
export interface CreateWorkflowDefinitionDto {
  name: string;
  description?: string | null;
  steps: WorkflowStepDto[];
  executionMode?: WorkflowExecutionMode;
  isEnabled?: boolean;
}

/** Update workflow request */
export interface UpdateWorkflowDefinitionDto {
  name?: string | null;
  description?: string | null;
  steps?: WorkflowStepDto[] | null;
  executionMode?: WorkflowExecutionMode | null;
  isEnabled?: boolean | null;
}

/** Workflow step */
export interface WorkflowStepDto {
  stepId?: string | null;
  agentId?: string | null;
  order: number;
  dependsOn?: string[] | null;
  condition?: string | null;
  provider?: string | null;
  model?: string | null;
  instructions?: string | null;
  maxRetries: number;
  retryDelaySeconds: number;
  timeoutSeconds?: number | null;
  requiresApproval: boolean;
  configuration?: Record<string, string> | null;
}

/** Workflow step result */
export interface WorkflowStepResultDto {
  stepId: string;
  output: string;
  skipped: boolean;
}

/** Run workflow request */
export interface RunWorkflowRequestDto {
  input: string;
  userId?: string | null;
}

/** Workflow execution result */
export interface WorkflowExecutionResultDto {
  executionId?: string | null;
  runId?: string | null;
  output: string;
  status: string;
  stepResults?: WorkflowStepResultDto[] | null;
}

/** Workflow execution status */
export interface WorkflowExecutionStatusDto {
  executionId: string;
  status: string;
  completedStepIds: string[];
  stepsAwaitingApproval: string[];
  createdAt: string;
  updatedAt: string;
}

/** Workflow definition query */
export interface WorkflowDefinitionQueryDto extends PagedQueryDto {
  keyword?: string | null;
  isEnabled?: boolean | null;
  executionMode?: WorkflowExecutionMode | null;
}

/** Workflow step approval request */
export interface WorkflowStepApprovalDto {
  feedback?: string | null;
}

/** Clone workflow request */
export interface CloneWorkflowRequestDto {
  newName?: string | null;
}

/** Workflow execution query (history) */
export interface WorkflowExecutionQueryDto extends PagedQueryDto {
  workflowDefinitionId?: string | null;
  status?: WorkflowExecutionStatus | null;
}

/** Workflow execution summary */
export interface WorkflowExecutionSummaryDto {
  id: string;
  executionId: string;
  workflowDefinitionId?: string | null;
  status: WorkflowExecutionStatus;
  completedStepCount: number;
  awaitingApprovalCount: number;
  creationTime: string;
  completedTime?: string | null;
  updatedTime: string;
}

/** Workflow execution detail */
export interface WorkflowExecutionDetailDto extends WorkflowExecutionSummaryDto {
  initialInput: string;
  completedStepIds: string[];
  stepsAwaitingApproval: string[];
  stepOutputs: Record<string, string>;
}

/** Workflow statistics */
export interface WorkflowStatsDto {
  totalWorkflows: number;
  enabledWorkflows: number;
  disabledWorkflows: number;
  byExecutionMode: Record<string, number>;
  totalExecutions: number;
  runningExecutions: number;
  completedExecutions: number;
  failedExecutions: number;
}

/** Workflow validation result */
export interface WorkflowValidationResultDto {
  isValid: boolean;
  errors: string[];
  warnings: string[];
}

/** Workflow stream event */
export interface WorkflowStreamEventDto {
  executionId?: string | null;
  eventType: string;
  stepId?: string | null;
  status: string;
  output?: string | null;
  stepResults?: WorkflowStepResultDto[] | null;
  isDone: boolean;
  errorMessage?: string | null;
}

// ============================================
// Persona Types
// ============================================

/** Agent persona (soul template) */
export interface AgentPersonaDto {
  id: string;
  name: string;
  slug: string;
  content: string;
  description?: string | null;
  /** Visibility scope — System (shared across tenants) or Tenant (tenant-private). */
  scope: ResourceScope;
  /** Owning tenant — populated when scope=Tenant. */
  tenantId?: string | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Create persona request */
export interface CreateAgentPersonaDto {
  name: string;
  slug: string;
  content: string;
  description?: string | null;
  /** Optional explicit scope; when omitted the server infers (tenant context -> Tenant, else System). */
  scope?: ResourceScope | null;
}

/** Update persona request */
export interface UpdateAgentPersonaDto {
  name?: string | null;
  slug?: string | null;
  content?: string | null;
  description?: string | null;
}

/** Persona query parameters */
export interface AgentPersonaQueryDto extends PagedQueryDto {
  keyword?: string | null;
  scope?: ResourceScope | null;
}

// ============================================
// Skill Category Types
// ============================================

/** Skill category (tree node) */
export interface SkillCategoryDto {
  id: string;
  name: string;
  slug: string;
  description?: string | null;
  parentId?: string | null;
  sortOrder: number;
  icon?: string | null;
  skillCount: number;
  children?: SkillCategoryDto[] | null;
}

/** Create skill category request */
export interface CreateSkillCategoryDto {
  name: string;
  slug?: string | null;
  description?: string | null;
  parentId?: string | null;
  sortOrder?: number | null;
  icon?: string | null;
}

/** Update skill category request */
export interface UpdateSkillCategoryDto {
  name?: string | null;
  description?: string | null;
  sortOrder?: number | null;
  icon?: string | null;
}

// ============================================
// Artifact Types
// ============================================

/** Semantic artifact type derived from MIME content type */
export type ArtifactType = 'image' | 'text' | 'code' | 'document' | 'data' | 'audio' | 'video' | 'file';

/** Agent artifact (run output) */
export interface AgentArtifactDto {
  id: string;
  runId: string;
  threadId: string;
  virtualPath: string;
  fileName: string;
  /** MIME type of the artifact content */
  contentType?: string | null;
  /** Semantic artifact category derived from contentType */
  type?: ArtifactType | null;
  size?: number | null;
  creationTime: string;
}

// ============================================
// User Profile Types
// ============================================

/** User AI profile */
export interface UserProfileDto {
  id: string;
  userId: string;
  displayName?: string | null;
  role?: string | null;
  preferredLanguage?: string | null;
  content: string;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Update user profile request */
export interface UpdateUserProfileDto {
  displayName?: string | null;
  role?: string | null;
  preferredLanguage?: string | null;
  content?: string | null;
}

// ============================================
// MCP Types
// ============================================

/** MCP server status */
export interface McpServerStatusDto {
  enabled: boolean;
  endpoint: string;
  requireAuthentication: boolean;
  /** Whether rate-limit keys are partitioned per tenant (not execution isolation) */
  rateLimitPerTenant: boolean;
  rateLimitPerMinute: number;
  exposedAgentCount: number;
  customToolCount: number;
  totalToolCount: number;
}

/** MCP tool info */
export interface McpToolInfoDto {
  name: string;
  description?: string | null;
}

/** MCP tool exposure options */
export interface McpToolExposureOptionsDto {
  toolName?: string | null;
  description?: string | null;
  enableStreaming?: boolean;
}

// --- MCP Server Registration ----------------------------------------------
// Entity-driven catalogue of EXTERNAL MCP servers Tnzi can connect to as a
// client. Distinct from McpServerStatusDto above (which describes Tnzi's own
// MCP-server-hosting status). Auth tokens are encrypted server-side via
// IDataProtectionProvider; DTOs only expose a `hasAuthToken` boolean — never
// plaintext or ciphertext. Enabled registrations are materialized into the
// MCP client runtime (merged with deployment-configured servers; same-name DB
// entries win). Only HTTP-family transports are allowed (sse / streamable-http
// / http) — stdio servers must be configured via deployment configuration.

/** MCP server registration entity DTO (read shape, no credential exposure) */
export interface McpServerRegistrationDto {
  id: string;
  name: string;
  serverUrl: string;
  transport: string;
  authType?: string | null;
  hasAuthToken: boolean;
  priority: number;
  isEnabled: boolean;
  description?: string | null;
  tags?: string | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Paged-query DTO for MCP server registrations */
export interface McpServerRegistrationQueryDto extends PagedQueryDto {
  transport?: string | null;
  isEnabled?: boolean | null;
  keyword?: string | null;
}

/** Create payload for MCP server registration */
export interface CreateMcpServerRegistrationDto {
  name: string;
  serverUrl: string;
  /** Transport mode — sse / streamable-http / http (stdio is rejected by the backend) */
  transport: string;
  /** Plaintext auth token — encrypted at rest by the backend */
  authToken?: string | null;
  authType?: string | null;
  priority?: number;
  isEnabled?: boolean;
  description?: string | null;
  tags?: string | null;
}

/**
 * Update payload for MCP server registration. Tri-state semantic for
 * authToken: `null`/omitted = keep current cipher; `""` = clear; non-empty
 * = encrypt and replace.
 */
export interface UpdateMcpServerRegistrationDto {
  name?: string | null;
  serverUrl?: string | null;
  /** Transport mode — sse / streamable-http / http (stdio is rejected by the backend) */
  transport?: string | null;
  authToken?: string | null;
  authType?: string | null;
  priority?: number | null;
  isEnabled?: boolean | null;
  description?: string | null;
  tags?: string | null;
}

/** MCP server registration test-connection result */
export interface McpServerTestResultDto {
  success: boolean;
  message?: string | null;
  latencyMs: number;
}

// ============================================
// MCP Tool Analytics Types
// ============================================

/** MCP tool statistics */
export interface McpToolStatsDto {
  toolName: string;
  totalCalls: number;
  avgDurationMs: number;
  p95DurationMs: number;
  errorRate: number;
  uniqueCallers: number;
  firstUsed?: string | null;
  lastUsed?: string | null;
}

/** MCP tool popularity ranking */
export interface McpToolPopularityDto {
  toolName: string;
  callCount: number;
  successRate: number;
  avgDurationMs: number;
}

/** MCP tool error info */
export interface McpToolErrorDto {
  errorMessage: string;
  count: number;
  lastOccurred: string;
}

// ============================================
// Workspace Agent / Persona (read-only, file-backed)
// ============================================

/** Workspace-discovered Agent definition (file-backed, read-only) */
export interface WorkspaceAgentDto {
  agentId: string;
  name: string;
  description?: string | null;
  provider?: string | null;
  model?: string | null;
  toolGroups?: string[] | null;
  executionMode?: string | null;
  domains?: string[] | null;
  roles?: string[] | null;
  qualityTier?: number | null;
  filePath: string;
  /** Where the file was found — "Global" or "Project". */
  workspaceScope: string;
  hasPersona: boolean;
  /** Markdown body of AGENT.md (system instructions, after frontmatter). */
  instructions?: string | null;
  /** Markdown body of the sibling PERSONA.md (if present). */
  personaContent?: string | null;
  /** Always "Workspace" — drives TSourceBadge. */
  source: string;
  /** Always true — workspace agents are file-backed and not editable. */
  isReadOnly: boolean;
}

/** Workspace-discovered Persona definition (sourced from PERSONA.md). */
export interface WorkspacePersonaDto {
  name: string;
  tone?: string | null;
  language?: string | null;
  agentId: string;
  filePath: string;
  workspaceScope: string;
  source: string;
  isReadOnly: boolean;
}
