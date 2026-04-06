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
  ExternalCli = 4,
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
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Agent execution configuration */
export interface AgentExecutionConfigDto {
  handoff?: HandoffExecutionConfigDto | null;
  router?: RouterExecutionConfigDto | null;
  agentAsTools?: AgentAsToolsExecutionConfigDto | null;
  externalCli?: ExternalCliExecutionConfigDto | null;
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

export interface ExternalCliExecutionConfigDto {
  provider: string;
  workingDirectory?: string | null;
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
  changeNote?: string | null;
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

/** Evaluation run query */
export interface EvaluationRunQueryDto extends PagedQueryDto {
  agentId?: string | null;
  status?: EvaluationRunStatus | null;
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
  isSystem: boolean;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Create persona request */
export interface CreateAgentPersonaDto {
  name: string;
  slug: string;
  content: string;
  description?: string | null;
  isSystem?: boolean;
}

/** Update persona request */
export interface UpdateAgentPersonaDto {
  name?: string | null;
  slug?: string | null;
  content?: string | null;
  description?: string | null;
  isSystem?: boolean | null;
}

/** Persona query parameters */
export interface AgentPersonaQueryDto extends PagedQueryDto {
  keyword?: string | null;
  isSystem?: boolean | null;
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
  transport: string;
  endpoint: string;
  requireAuthentication: boolean;
  tenantIsolation: boolean;
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
