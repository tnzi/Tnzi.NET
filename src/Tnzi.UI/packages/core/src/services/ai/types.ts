/**
 * AI Module Types - Aligned with Tnzi.NET backend AI module
 *
 * Covers: Agents, Chat, Threads, Agent Runs, Skills, Workflows,
 * Evaluations, Usage limits, Usage Analytics, MCP, MCP Tool Analytics,
 * Providers, Personas, Skill Categories, Artifacts, User Profile
 */

import type { PagedQueryDto } from '../../types/pagination';

// ============================================
// Enums
//
// The backend registers a global JsonStringEnumConverter, so every enum-typed
// field is serialized on the wire as its PascalCase member NAME (e.g. "Single",
// "Completed", "AwaitingInput") rather than a number. These are declared as
// string enums (member name === value) so `dto.field === EnumType.Member` and
// direct string comparisons both match the payload. Inputs accept either the
// name or the legacy number, so sending the string value back is always safe.
// ============================================

/** Agent execution mode */
export enum AgentExecutionMode {
  Single = 'Single',
  Handoff = 'Handoff',
  AgentAsTools = 'AgentAsTools',
  Router = 'Router',
}

/** Agent run status */
export enum AgentRunStatus {
  Pending = 'Pending',
  Running = 'Running',
  AwaitingApproval = 'AwaitingApproval',
  Completed = 'Completed',
  Failed = 'Failed',
  Cancelled = 'Cancelled',
  RequiresClarification = 'RequiresClarification',
}

/** Agent run node status */
export enum AgentRunNodeStatus {
  Pending = 'Pending',
  Running = 'Running',
  Completed = 'Completed',
  Failed = 'Failed',
  Skipped = 'Skipped',
  AwaitingApproval = 'AwaitingApproval',
  Approved = 'Approved',
  Rejected = 'Rejected',
}

/** Workflow execution mode */
export enum WorkflowExecutionMode {
  Sequential = 'Sequential',
  Parallel = 'Parallel',
  Dag = 'Dag',
}

/**
 * Workflow execution status.
 * Member order mirrors the backend enum
 * (`Tnzi.AI.Workflow.WorkflowExecutionStatus`) exactly - the previous numeric
 * declaration mis-mapped Paused/AwaitingApproval and omitted Cancelled /
 * AwaitingInput.
 */
export enum WorkflowExecutionStatus {
  Running = 'Running',
  Completed = 'Completed',
  Failed = 'Failed',
  Cancelled = 'Cancelled',
  Paused = 'Paused',
  AwaitingApproval = 'AwaitingApproval',
  AwaitingInput = 'AwaitingInput',
}

/** Evaluation run status */
export enum EvaluationRunStatus {
  Running = 'Running',
  Completed = 'Completed',
  Failed = 'Failed',
}

/** Usage analytics time granularity */
export enum UsageGranularity {
  Daily = 'Daily',
  Weekly = 'Weekly',
  Monthly = 'Monthly',
}

/**
 * The two usage-limit windows. `Session` is the 5-hour window, opened by the first use when none is running;
 * `Weekly` is a run of back-to-back 7-day periods, so it always resets on the same weekday and time.
 */
export enum UsageLimitWindow {
  Session = 'Session',
  Weekly = 'Weekly',
}

/** Thread list archive filter. `All` (the server's default) does not filter. */
export enum ThreadListStatus {
  All = 'All',
  Active = 'Active',
  Archived = 'Archived',
}

/** Thread list order: most recent activity first (default), newest first, or by title (A-Z, ignoring case). */
export enum ThreadListSort {
  LastActivity = 'LastActivity',
  CreationTime = 'CreationTime',
  Title = 'Title',
}

/** How one overridable limit is set: follow the level below, no limit, or an amount. */
export enum UsageLimitMode {
  Inherit = 'Inherit',
  Unlimited = 'Unlimited',
  Limited = 'Limited',
}

/** Skill scope */
export enum SkillScope {
  System = 'System',
  Tenant = 'Tenant',
  User = 'User',
}

/**
 * Shared-resource visibility scope (Provider).
 * System rows are shared across tenants; Tenant rows are tenant-private.
 * Mirrors the backend ResourceScope enum (serialized as PascalCase names).
 */
export enum ResourceScope {
  System = 'System',
  Tenant = 'Tenant',
  User = 'User',
}

/** Skill source */
export enum SkillSource {
  FileSystem = 'FileSystem',
  Database = 'Database',
  Plugin = 'Plugin',
  Managed = 'Managed',
  Project = 'Project',
}

/** Reasoning effort */
export enum ReasoningEffort {
  None = 'None',
  Low = 'Low',
  Medium = 'Medium',
  High = 'High',
  /** Highest effort the provider offers (e.g. Anthropic Opus, OpenAI xhigh) */
  Max = 'Max',
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
  /** Persona (soul) content injected as a &lt;soul&gt; block. Inline on the agent. */
  persona?: string | null;
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
  /** Persona (soul) content injected as a &lt;soul&gt; block (optional). */
  persona?: string | null;
  /** Assigned knowledge base IDs (RAG). */
  knowledgeBaseIds?: string[] | null;
  /** Assigned skill slugs. */
  skillSlugs?: string[] | null;
}

/**
 * Update agent request. PATCH semantics: an omitted or `null` field is left as
 * it is.
 */
export interface UpdateAgentDto {
  name?: string | null;
  /** Pass an empty string to clear. */
  description?: string | null;
  /** System prompt. Pass an empty string to clear. */
  instructions?: string | null;
  provider?: string | null;
  /** Pass an empty string to clear; the provider's default model then applies. */
  model?: string | null;
  toolGroups?: string[] | null;
  /** A set value cannot be cleared through an update (`null` = no change). */
  temperature?: number | null;
  /** A set value cannot be cleared through an update (`null` = no change). */
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
   * Persona (soul) content. Omit to leave unchanged, pass an empty string
   * to clear, pass content to set.
   */
  persona?: string | null;
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
  /**
   * Replace this user message of the thread (and everything after it) with the request's
   * `message` / `content`. Needs `threadId`.
   */
  editMessageId?: string | null;
  /**
   * Send the thread's last user message again (with its attachments) for a new reply.
   * Needs `threadId`; leave `message` and `content` empty.
   */
  regenerate?: boolean;
  /**
   * Per-turn metadata: a small set of values the consumer defines (string keys, any JSON
   * values), such as the ids of the devices the message points at. The server bounds it
   * (default 16 keys, 4 KB; keys start with a letter and use letters, digits, `_ . : -`),
   * hands it to its per-turn handlers and stores it with the user message.
   */
  metadata?: Record<string, unknown> | null;
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

/**
 * Tool call detail. Mirrors backend `ToolCallDetail` (name, duration, outcome);
 * emitted on the stream once the tool has run, and persisted on the thread
 * message.
 *
 * For an agent bound to an external CLI runtime a call arrives twice with the
 * same `id`: when it starts (`status: 'running'`, with `input`) and when its
 * result is in (`completed` / `failed`, with `result`). `isSuccess` defaults to
 * true on the backend, so read `status` first when it is present.
 */
export interface ToolCallDetailDto {
  name: string;
  /** The call's id (external CLI agents). */
  id?: string | null;
  /** `running` / `completed` / `failed` (external CLI agents). */
  status?: string | null;
  durationMs?: number | null;
  /** Whether the call succeeded. */
  isSuccess?: boolean | null;
  /** Error message when `isSuccess` is false. */
  error?: string | null;
  /** The arguments as the agent sent them (external CLI agents: a JSON value, usually an object). */
  input?: unknown;
  output?: string | null;
  /** What the tool returned, block by block (external CLI agents). Images carry an attachment id, not bytes. */
  result?: ToolResultBlockDto[] | null;
  /** How long the reply text was when the call happened, to place the call within it (external CLI agents). */
  textOffset?: number | null;
}

/**
 * One block of a tool result (backend `ToolResultBlockDto`). An image is read
 * through `GET /api/threads/{threadId}/attachments/{attachmentId}`.
 */
export interface ToolResultBlockDto {
  /** `text`, `image` or `file` (a file the tool handed over, by name and URI). */
  type: string;
  text?: string | null;
  mediaType?: string | null;
  attachmentId?: string | null;
  /** Name of a `file` block, e.g. `report.pdf`. */
  name?: string | null;
  /** URI of a `file` block as the tool gave it; what it links to is the consumer's call. */
  uri?: string | null;
}

/**
 * Component reference for rich frontend rendering (maps to backend `ComponentRefDto`).
 * The framework never emits one itself: an application that renders interactive components
 * sets it on the stream events of its own streaming endpoint.
 */
export interface ComponentRefDto {
  /** Persistent component ID (for data fetching and session restore) */
  id: string;
  /** Component type, e.g. "DataTable", "Calendar" */
  type: string;
  /** "display" (read-only) or "action" (submittable) */
  category: string;
  /** Title shown above the component */
  title: string;
  /** Total record count (for pagination info) */
  totalCount: number;
  /** Column definitions JSON (ColumnDef[]) */
  columnsJson?: string | null;
  /** "active", "superseded", "submitted" or "expired" */
  status?: string | null;
}

/** Structured stream event types the framework emits in `ChatStreamEvent.eventType`. */
export type ChatStreamEventType =
  | 'clarification'
  | 'todos_updated'
  | 'sub_agent_started'
  | 'sub_agent_completed'
  | 'sub_agent_failed'
  | 'sub_agent_timed_out'
  /**
   * The first event of a turn run by an external CLI agent: carries `runId` (and
   * `userMessageId`), so the client can find the run again after a refresh and cancel it.
   */
  | 'run_started'
  /**
   * An external agent asks to be allowed an action ("ask me" permission mode). `eventData`: `requestId`,
   * `tool`, `toolCallId`, `input`, `description`, `title`, `reason`, `blockedPath`, `options`
   * (`id` / `name` / `kind`), `expiresAt`, `status` (`Pending`). Answer with `answerRunPermission`.
   */
  | 'permission_request'
  /** A permission request got its outcome. `eventData`: `requestId`, `status`, `optionId`, `answeredBy`. */
  | 'permission_resolved';

/**
 * Plan-mode todo item as carried by a stream event (maps to backend `TodoItemDto`).
 * The stream serializer writes enums as numbers: `status` is 0 Pending, 1 InProgress, 2 Completed, 3 Skipped.
 */
export interface StreamTodoItemDto {
  content: string;
  status: number;
  order: number;
}

/**
 * Chat stream event - SSE delta model (each event contains incremental content, not cumulative).
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
  /** Rich component reference (set by an application's own streaming endpoint; the framework never emits one) */
  componentRef?: ComponentRefDto | null;
  /**
   * Structured event type. `clarification` carries `eventData.type` and `eventData.options` (the question
   * text is also in `delta`); `todos_updated` carries `todos`; the `sub_agent_*` events carry
   * `eventData.agent` plus `task`, `result_length`, `timeout_seconds` or `error`.
   */
  eventType?: ChatStreamEventType | string | null;
  /** Payload of a structured event (shape depends on `eventType`) */
  eventData?: Record<string, unknown> | null;
  /** Todo list snapshot (`todos_updated` events) */
  todos?: StreamTodoItemDto[] | null;
  /** Files the run produced */
  artifacts?: AgentArtifactDto[] | null;
  /** Persisted user message ID (populated on the terminal event when the turn is persisted; null when persistence was skipped). */
  userMessageId?: string | null;
  /** Persisted assistant message ID (populated on the terminal event when the turn is persisted). Clients use this for message-scoped APIs such as feedback submission. */
  assistantMessageId?: string | null;
  /**
   * Position of the event in its run (external CLI agents). Reattach with
   * `getRunStreamUrl(runId, sequence)` to continue after the last event seen.
   */
  sequence?: number | null;
  /**
   * With `errorCode` `AI_USAGE_LIMIT_REACHED` (the limit this deployment set for the user) or
   * `AI_CLI_USAGE_LIMIT` (the external CLI's own account): when the limit resets (ISO, UTC), if known.
   */
  usageLimitResetsAt?: string | null;
  /** With `errorCode` `AI_USAGE_LIMIT_REACHED`: which window is at its limit. */
  usageLimitWindow?: UsageLimitWindow | null;
}

/** Content part (polymorphic: text | image | document | file) */
export interface ContentPartDto {
  type: 'text' | 'image' | 'document' | 'file';
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
  fileName?: string | null;
}

/** A PDF sent inline (base64 or a `data:` URI). External CLI agents receive it as a document block. */
export interface DocumentContentPartDto extends ContentPartDto {
  type: 'document';
  base64Data: string;
  mediaType?: string | null;
  fileName?: string | null;
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
  /** When the thread was archived; null when it is not. */
  archivedTime?: string | null;
}

/** Create thread request */
export interface CreateAgentThreadDto {
  agentId?: string | null;
  title?: string | null;
}

/** Thread list query */
export interface ThreadListQueryDto extends PagedQueryDto {
  agentId?: string | null;
  /** Only threads of these agents (agent-less threads never match); omitted or empty = no filter. ANDed with `agentId`. */
  agentIds?: string[] | null;
  keyword?: string | null;
  /** Last activity at or after (ISO). */
  startTime?: string | null;
  /** Last activity at or before (ISO). */
  endTime?: string | null;
  /** Archive filter; omitted = `All`. */
  status?: ThreadListStatus;
  /** Order; omitted = `LastActivity`. */
  sort?: ThreadListSort;
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
  /** When the thread was archived; null when it is not. */
  archivedTime?: string | null;
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
  /** How the reply ended when it did not simply complete: `stopped`, `failed` or `usage_limit`. */
  status?: string | null;
  /** With status `usage_limit`: when the external CLI's account limit resets (ISO, UTC), if known. */
  usageLimitResetsAt?: string | null;
  /** On a user message: the per-turn metadata the client sent with it. */
  metadata?: Record<string, unknown> | null;
  /** Files shown on the message (uploads on a user message, tool images on a reply). No bytes. */
  attachments?: AttachmentDto[] | null;
}

/** Where a stored attachment came from. */
export type AttachmentOrigin = 'Upload' | 'ToolResult';

/**
 * A stored chat attachment (no bytes). Read its content with
 * `useThreadApi(http).getAttachment(threadId, id)`; only the thread's owner can.
 */
export interface AttachmentDto {
  id: string;
  origin: AttachmentOrigin;
  mediaType: string;
  fileName?: string | null;
  size: number;
  /** The tool call a tool image came from. */
  toolCallId?: string | null;
}

/** One page of a thread's messages, oldest first (`GET threads/{id}/messages`). */
export interface ThreadMessagePageDto {
  messages: ThreadMessageDto[];
  /** Older messages remain before this page. */
  hasMore: boolean;
}

/** Query for a page of thread messages: the `limit` newest before `beforeOrder` (exclusive). */
export interface ThreadMessagePageQuery {
  beforeOrder?: number | null;
  /** 1-200, default 50. */
  limit?: number;
}

/** A run of an external CLI agent on a thread, as the chat endpoints report it. */
export interface ChatRunDto {
  runId: string;
  threadId?: string | null;
  /** The user message the run answers. */
  userMessageId?: string | null;
  /** `Queued`, `Dispatched`, `Running`, or a terminal status. */
  status: string;
  creationTime: string;
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
// Usage Limits & Model Selection Types
// ============================================

/** One overridable limit: inherit, unlimited, or an amount in USD. */
export interface UsageLimitSettingDto {
  mode: UsageLimitMode;
  /** USD; only meaningful with `Limited`. */
  amountUsd?: number | null;
}

/** A saved policy (the tenant's defaults, or one user's own settings). Unset items follow the level below. */
export interface UsagePolicyDto {
  sessionLimit: UsageLimitSettingDto;
  weeklyLimit: UsageLimitSettingDto;
  /** Allowed model ids; null = follow the level below. */
  allowedModels?: string[] | null;
  /** Default model id; null = follow the level below. */
  defaultModel?: string | null;
  note?: string | null;
  lastModificationTime?: string | null;
}

/** Save a policy. `allowedModels` may not be an empty list (leave it null to follow the level below). */
export interface SaveUsagePolicyDto {
  sessionLimit: UsageLimitSettingDto;
  weeklyLimit: UsageLimitSettingDto;
  allowedModels?: string[] | null;
  defaultModel?: string | null;
  note?: string | null;
}

/** A model of the model list (`AI:Chat:Models`). */
export interface ChatModelDto {
  id: string;
  displayName: string;
  /** Provider the model runs on; null = any agent. */
  provider?: string | null;
  description?: string | null;
}

/** The models the current user may pick for an agent. More than one: the chat shows a picker. */
export interface ChatModelsDto {
  models: ChatModelDto[];
  defaultModel?: string | null;
}

/** The policy in force after every level was applied. */
export interface EffectiveUsagePolicyDto {
  /** USD; null = no limit. */
  sessionLimitUsd?: number | null;
  /** USD; null = no limit. */
  weeklyLimitUsd?: number | null;
  allowedModels: ChatModelDto[];
  defaultModel?: string | null;
}

/** The configuration level (`AI:UsageLimits`): what an unset default follows. */
export interface UsageLimitConfigurationDto {
  sessionLimitUsd?: number | null;
  weeklyLimitUsd?: number | null;
  /** Empty = every model of the model list. */
  allowedModels: string[];
  defaultModel?: string | null;
  sessionWindowMinutes: number;
  weeklyWindowMinutes: number;
  /** 0-1 */
  warningThreshold: number;
}

/** Everyone's defaults (admin). */
export interface UsageLimitDefaultsDto {
  policy: UsagePolicyDto;
  configuration: UsageLimitConfigurationDto;
  effective: EffectiveUsagePolicyDto;
  catalog: ChatModelDto[];
}

/** One window's usage as an administrator sees it (amounts included). */
export interface UsageWindowStatusDto {
  window: UsageLimitWindow;
  durationMinutes: number;
  /** USD; null = no limit. */
  limitUsd?: number | null;
  usedUsd: number;
  /** 0-100 (above 100 when a turn overshot); null = no limit. */
  usedPercent?: number | null;
  startedAt?: string | null;
  /** UTC; null = the window has not started. */
  resetsAt?: string | null;
  exceeded: boolean;
  warningReached: boolean;
}

/** One user's usage, all windows (admin). */
export interface UsageLimitStatusDto {
  windows: UsageWindowStatusDto[];
  /** 0-1 */
  warningThreshold: number;
  checkedAt: string;
  lastUsedAt?: string | null;
}

/** One user's settings, what they resolve to, and current usage (admin). */
export interface UserUsageLimitsDto {
  userId: string;
  hasCustomPolicy: boolean;
  policy: UsagePolicyDto;
  /** Everyone's defaults: what this user's unset items resolve to. */
  defaults: EffectiveUsagePolicyDto;
  effective: EffectiveUsagePolicyDto;
  catalog: ChatModelDto[];
  usage: UsageLimitStatusDto;
}

/** One window of the current user's own usage: percentages and reset times, no amounts. */
export interface MyUsageWindowDto {
  window: UsageLimitWindow;
  /** False = no limit on this window (and no percentage). */
  limited: boolean;
  /** Length of the window in minutes (`AI:UsageLimits`), for wording such as "5 hours". */
  durationMinutes: number;
  usedPercent?: number | null;
  startedAt?: string | null;
  /** UTC; null = not started (the 5-hour window starts with the next message). */
  resetsAt?: string | null;
  /** Sending is refused until `resetsAt`. */
  exceeded: boolean;
  warningReached: boolean;
}

/** The current user's own usage (`GET usage-limits/me`). */
export interface MyUsageLimitsDto {
  windows: MyUsageWindowDto[];
  /** 0-1 */
  warningThreshold: number;
  /** When it was read (UTC): the "last updated" line. */
  checkedAt: string;
}

/** The `errorDetails` of an `AI_USAGE_LIMIT_REACHED` refusal. */
export interface UsageLimitDenialDto {
  window: UsageLimitWindow;
  /** UTC; null when the limit is zero (it does not reset on its own). */
  resetsAt?: string | null;
}

/** Admin list of metered users. */
export interface UsageMeterQueryDto extends PagedQueryDto {
  userId?: string | null;
}

/** One row of the admin list: a user who used AI and their windows. */
export interface UserUsageSummaryDto {
  userId: string;
  hasCustomPolicy: boolean;
  windows: UsageWindowStatusDto[];
  lastUsedAt?: string | null;
}

/**
 * Budget check status (USD cost budget). Serialized as the PascalCase member
 * name by the backend's global JsonStringEnumConverter.
 *
 * `Indeterminate`: the budget is enabled but nothing in the period recorded a
 * cost (cost tracking off, or no model rate configured), so the aggregated
 * 0 USD means "cannot be measured", not "nothing was spent". Render it as a
 * warning, never as a green "within budget".
 */
export type BudgetStatus = 'WithinBudget' | 'WarningThreshold' | 'BudgetExceeded' | 'Indeterminate';

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

/**
 * Optional narrowing for the usage breakdown endpoints (`by-agent`). Matches the
 * filters the summary and trend queries carry, so a dashboard that filters by
 * provider, model or agent draws every panel from the same rows.
 */
export interface UsageBreakdownFilters {
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
 * Provider source - 'Database' (admin-entered entity, writable) or
 * 'Configuration' (appsettings AI:Providers, read-only: update/delete/testConnection return 400).
 */
export type ProviderSource = 'Database' | 'Configuration';

/** Provider dropdown option (enabled providers only) - for the Agent config Provider select. */
export interface ProviderOptionDto {
  id: string;
  name: string;
  providerType: string;
  defaultModel?: string | null;
  /** Source - 'Database' (entity) or 'Configuration' (appsettings AI:Providers). */
  source: ProviderSource;
}

/** Provider model list - live from /v1/models when available, else a static fallback. */
export interface ProviderModelsDto {
  models: string[];
  /** "live" = fetched from the provider's /v1/models; "fallback" = static curated list. */
  source: string;
}

/** Provider entity DTO - never exposes plaintext or ciphertext API key */
export interface ProviderDto {
  id: string;
  name: string;
  providerType: string;
  endpoint?: string | null;
  defaultModel?: string | null;
  /** Runtime tie-break when several rows share a name - NOT the list order. */
  priority: number;
  /** Display order (lower first), written by drag-to-reorder. */
  sortOrder: number;
  isEnabled: boolean;
  description?: string | null;
  hasApiKey: boolean;
  /** Visibility scope - System (shared across tenants) or Tenant (tenant-private). */
  scope: ResourceScope;
  /** Owning tenant - populated when scope=Tenant. */
  tenantId?: string | null;
  /**
   * Source - 'Database' (entity, writable) or 'Configuration' (appsettings
   * AI:Providers; synthetic stable id, pinned to the top of the list,
   * update/delete/testConnection rejected with 400, listModels works).
   */
  source: ProviderSource;
  /** Creation time - null for Configuration-sourced entries. */
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

/** Provider update request - apiKey: null = keep, '' = clear, non-empty = rotate */
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

/** A batch evaluation target that failed to evaluate */
export interface BatchEvaluationFailureDto {
  agentId: string;
  versionNumber?: number | null;
  /** Why the target failed */
  message: string;
}

/** Batch evaluation result */
export interface BatchEvaluationResultDto {
  /** Targets that were evaluated */
  results: EvaluationRunDetailDto[];
  /** Targets that failed; together with `results` they cover every requested target */
  failures: BatchEvaluationFailureDto[];
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
  /** Category the skill is filed under; null when uncategorised. */
  categoryId?: string | null;
  /** That category's display name, resolved server-side. */
  categoryName?: string | null;
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
  /** Graph configuration (conditional edges + loops); null when the workflow has none */
  configuration?: WorkflowGraphConfigurationDto | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/**
 * Workflow graph configuration: conditional edges and loops, persisted alongside the steps.
 * A router / conditional / review node's route only takes effect through a conditional edge
 * whose `fromNodeId` is that step; without one every dependent branch runs.
 */
export interface WorkflowGraphConfigurationDto {
  conditionalEdges?: WorkflowConditionalEdgeDto[] | null;
  /** Loops keyed by loop id */
  loops?: Record<string, WorkflowLoopDto> | null;
}

/** Conditional edge: route key (or matched output) -> target step id */
export interface WorkflowConditionalEdgeDto {
  fromNodeId: string;
  routes: Record<string, string>;
  defaultTarget?: string | null;
  /** OutputContains (default) | OutputEquals | JsonPath */
  conditionType?: string | null;
}

/** Loop: the listed steps re-run until the loop signals done, at most maxIterations times */
export interface WorkflowLoopDto {
  nodeIds: string[];
  maxIterations?: number;
}

/** Create workflow request */
export interface CreateWorkflowDefinitionDto {
  name: string;
  description?: string | null;
  steps: WorkflowStepDto[];
  executionMode?: WorkflowExecutionMode;
  isEnabled?: boolean;
  configuration?: WorkflowGraphConfigurationDto | null;
}

/** Update workflow request */
export interface UpdateWorkflowDefinitionDto {
  name?: string | null;
  description?: string | null;
  steps?: WorkflowStepDto[] | null;
  executionMode?: WorkflowExecutionMode | null;
  isEnabled?: boolean | null;
  /** Omit / null = leave untouched; an object with no edges and no loops clears the stored configuration */
  configuration?: WorkflowGraphConfigurationDto | null;
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
  startedAt?: string | null;
  durationMs?: number | null;
  creationTime: string;
  completedTime?: string | null;
  updatedTime: string;
  pendingSignalCount?: number;
  /** Why the execution is waiting; `cancel_requested` after a cancel was queued for a running execution */
  currentWaitReason?: string | null;
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

/**
 * Workflow definition version: a snapshot of the definition taken before each
 * update (and before each restore). `definition` is only populated by the
 * single-version read; the history list leaves it out.
 */
export interface WorkflowDefinitionVersionDto {
  id: string;
  workflowDefinitionId: string;
  versionNumber: number;
  changeDescription?: string | null;
  /** Full definition snapshot as a JSON string (name, description, steps, executionMode, isEnabled, configuration) */
  definition?: string | null;
  creationTime: string;
}

/** Restore a workflow to an earlier version */
export interface RestoreWorkflowVersionRequestDto {
  /** Note recorded on the snapshot of the current definition taken before the restore */
  changeDescription?: string | null;
}

/** Per-workflow execution statistics (duration analysis) */
export interface WorkflowExecutionStatsDto {
  workflowId: string;
  totalExecutions: number;
  avgDurationMs?: number | null;
  minDurationMs?: number | null;
  maxDurationMs?: number | null;
  p95DurationMs?: number | null;
  /** 0.0 to 1.0 */
  successRate: number;
}

/** Pending human-in-the-loop interrupt of a workflow execution */
export interface WorkflowInterruptDto {
  stepId: string;
  reason: string;
  /** Approval | HumanInput | ExternalEvent */
  type: string;
  /** Requested input fields: key to a free-form description of the expected value */
  requestedInput?: Record<string, unknown> | null;
  timeoutSeconds?: number | null;
  executionId: string;
}

/** Resume an interrupted workflow execution with external input */
export interface ResumeWorkflowInputDto {
  /** Must match the pending interrupt's step id */
  stepId: string;
  input: Record<string, unknown>;
}

/** Input payload carried by a workflow execution signal */
export interface WorkflowExecutionInputDto {
  message?: string | null;
  data?: Record<string, unknown> | null;
  submittedAt: string;
}

/** Node-level input carried by a workflow execution signal */
export interface AgentRunNodeInputDto {
  nodeKey?: string | null;
  nodeName?: string | null;
  inputKind?: string | null;
  input: WorkflowExecutionInputDto;
}

/** Pending signal in a workflow execution's mailbox (e.g. a requested cancel) */
export interface WorkflowExecutionSignalDto {
  signalId: string;
  /** `cancel` | `resume_input` */
  type: string;
  stepId?: string | null;
  nodeInput?: AgentRunNodeInputDto | null;
  reason?: string | null;
  createdAt: string;
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
}

// --- MCP Server Registration ----------------------------------------------
// Entity-driven catalogue of EXTERNAL MCP servers Tnzi can connect to as a
// client. Distinct from McpServerStatusDto above (which describes Tnzi's own
// MCP-server-hosting status). Auth tokens are encrypted server-side via
// IDataProtectionProvider; DTOs only expose a `hasAuthToken` boolean - never
// plaintext or ciphertext. Enabled registrations are materialized into the
// MCP client runtime (merged with deployment-configured servers; same-name DB
// entries win). Only HTTP-family transports are allowed (sse / streamable-http
// / http) - stdio servers must be configured via deployment configuration.

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
  /** Transport mode - sse / streamable-http / http (stdio is rejected by the backend) */
  transport: string;
  /** Plaintext auth token - encrypted at rest by the backend */
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
  /** Transport mode - sse / streamable-http / http (stdio is rejected by the backend) */
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
  /** Where the file was found - "Global" or "Project". */
  workspaceScope: string;
  hasPersona: boolean;
  /** Markdown body of AGENT.md (system instructions, after frontmatter). */
  instructions?: string | null;
  /** Markdown body of the sibling PERSONA.md (if present). */
  personaContent?: string | null;
  /** Always "Workspace" - drives TSourceBadge. */
  source: string;
  /** Always true - workspace agents are file-backed and not editable. */
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
