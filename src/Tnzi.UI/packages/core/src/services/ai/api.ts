/**
 * AI Module API
 *
 * Covers: Chat, Threads, Agents (admin), Agent Runs (admin), Providers (admin),
 * Usage limits & model selection (user + admin), Usage Analytics (admin), Evaluations (admin),
 * Skills (user + admin), Workflows (admin), MCP (admin), MCP Tool Analytics (admin),
 * Skill Categories (admin), Artifacts (user), User Profile (user),
 * Workspace Agents (admin).
 *
 * RAG endpoints live in `./rag`. The Permission rule engine
 * (`usePermissionAdminApi` → admin/permissions), Agent Tasks
 * (`useAdminTaskApi` → admin/ai/tasks), and Sub-Agent Types
 * (`useAdminSubAgentTypeApi` → admin/ai/sub-agent-types) live in
 * `./permission`, `./task`, and `./sub-agent-type` respectively.
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type { ReorderRequest } from '../../types/api';
import type { CliPermissionAnswerDto } from './cli';
import type {
  // Chat
  ChatRequestDto,
  ChatResponseDto,
  ChatRunDto,
  ThreadMessagePageDto,
  ThreadMessagePageQuery,
  // Agents
  AgentDto,
  CreateAgentDto,
  UpdateAgentDto,
  AgentListQueryDto,
  RunAgentRequestDto,
  AgentResponseDto,
  AgentVersionDto,
  AgentVersionQueryDto,
  AgentValidationResultDto,
  AgentHealthSummaryDto,
  ConfigureAbTestDto,
  ToolGroupDto,
  AgentMemoryDto,
  AgentMemoryListQueryDto,
  CreateAgentMemoryDto,
  UpdateAgentMemoryDto,
  // Threads
  AgentThreadDto,
  CreateAgentThreadDto,
  AgentThreadDetailDto,
  ThreadListQueryDto,
  UpdateThreadTitleDto,
  ThreadExportDto,
  MessageFeedbackDto,
  // Agent Runs
  AgentRunDto,
  AgentRunNodeDto,
  AgentRunTraceDto,
  AgentRunQueryDto,
  AgentRunStatsDto,
  ApproveRunDto,
  RejectRunDto,
  // Usage limits & model selection
  MyUsageLimitsDto,
  ChatModelsDto,
  UsageLimitDefaultsDto,
  SaveUsagePolicyDto,
  UserUsageLimitsDto,
  UsageMeterQueryDto,
  UserUsageSummaryDto,
  BudgetSummaryDto,
  // Usage Analytics
  UsageSummaryDto,
  UsageSummaryQueryDto,
  UsageLogDto,
  UsageLogQueryDto,
  ProviderUsageDto,
  ModelUsageDto,
  UsageTrendPointDto,
  UsageTrendQueryDto,
  UsageBreakdownFilters,
  AgentUsageDto,
  AgentFeedbackStatsDto,
  CostSummaryDto,
  // Providers
  ProviderDefaultModelDto,
  ProviderOptionDto,
  ProviderModelsDto,
  ProviderDto,
  ProviderQueryDto,
  CreateProviderDto,
  UpdateProviderDto,
  ProviderTestResultDto,
  // Evaluations
  EvaluationRunDto,
  EvaluationRunDetailDto,
  EvaluationRunQueryDto,
  CreateEvaluationRunDto,
  BatchEvaluationDto,
  BatchEvaluationResultDto,
  EvaluationTrendDto,
  VersionComparisonDto,
  // Skills
  SkillSummaryDto,
  SkillDetailDto,
  CreateSkillDto,
  UpdateSkillDto,
  SkillActivateDto,
  SkillActivationResultDto,
  SkillQueryDto,
  SkillUsageStatsDto,
  PopularSkillDto,
  SkillExportDto,
  SkillImportRequestDto,
  SkillImportResultDto,
  // Workflows
  WorkflowDefinitionDto,
  CreateWorkflowDefinitionDto,
  UpdateWorkflowDefinitionDto,
  WorkflowDefinitionQueryDto,
  RunWorkflowRequestDto,
  WorkflowExecutionResultDto,
  CloneWorkflowRequestDto,
  WorkflowExecutionStatusDto,
  WorkflowExecutionQueryDto,
  WorkflowExecutionSummaryDto,
  WorkflowExecutionDetailDto,
  WorkflowStepApprovalDto,
  WorkflowStatsDto,
  WorkflowValidationResultDto,
  WorkflowDefinitionVersionDto,
  RestoreWorkflowVersionRequestDto,
  WorkflowExecutionStatsDto,
  WorkflowInterruptDto,
  ResumeWorkflowInputDto,
  WorkflowExecutionSignalDto,
  // MCP
  McpServerStatusDto,
  McpToolInfoDto,
  McpToolExposureOptionsDto,
  McpServerRegistrationDto,
  McpServerRegistrationQueryDto,
  CreateMcpServerRegistrationDto,
  UpdateMcpServerRegistrationDto,
  McpServerTestResultDto,
  // MCP Tool Analytics
  McpToolStatsDto,
  McpToolPopularityDto,
  McpToolErrorDto,
  // Skill Categories
  SkillCategoryDto,
  CreateSkillCategoryDto,
  UpdateSkillCategoryDto,
  // Artifacts
  AgentArtifactDto,
  // User Profile
  UserProfileDto,
  UpdateUserProfileDto,
  SkillScope,
} from './types';

// ============================================
// User: Chat API
// Route: /chat
// ============================================

/** User-facing chat API */
export function useChatApi(client: HttpClient) {
  const base = '/chat';
  return {
    /** Send a chat message (non-streaming) */
    chat: (data: ChatRequestDto) =>
      client.post<ChatResponseDto>(base, data),

    /** Build stream URL for SSE chat */
    getChatStreamUrl: () =>
      client.resolveUrl(`${base}/stream`),

    /**
     * The thread's runs that have not finished (external CLI agents). After a refresh, a
     * client reattaches to these with `getRunStreamUrl`.
     */
    getActiveRuns: (threadId: string) =>
      client.get<ChatRunDto[]>(`${base}/threads/${encodeURIComponent(threadId)}/runs`),

    /**
     * URL of a run's event stream (SSE, GET, same events as `chat/stream`): from the start,
     * or after `fromSequence`. Pass it to `streamChat({ url, method: 'GET' })`.
     */
    getRunStreamUrl: (runId: string, fromSequence = 0) =>
      client.resolveUrl(`${base}/runs/${encodeURIComponent(runId)}/stream?fromSequence=${fromSequence}`),

    /** Cancel a run on the server. What it had written stays in the thread as a stopped reply. */
    cancelRun: (runId: string) =>
      client.post<void>(`${base}/runs/${encodeURIComponent(runId)}/cancel`),

    /**
     * Answer a `permission_request` event of a run you started: `optionId` is one of the event's
     * `eventData.options[].id`. 409 when the request already has an outcome (answered elsewhere, timed out).
     */
    answerRunPermission: (runId: string, requestId: string, answer: CliPermissionAnswerDto) =>
      client.post<void>(
        `${base}/runs/${encodeURIComponent(runId)}/permissions/${encodeURIComponent(requestId)}`,
        answer,
      ),
  };
}

// ============================================
// User: Thread API
// Route: /threads
// ============================================

/** User-facing thread management API (scoped to current user) */
export function useThreadApi(client: HttpClient) {
  const base = '/threads';
  return {
    /** Get current user's threads (paged) */
    getList: (data: ThreadListQueryDto) =>
      client.post<PagedList<AgentThreadDto>>(`${base}/query`, data),

    /** Get thread detail with recent messages */
    getDetail: (id: string, messageLimit = 50) =>
      client.get<AgentThreadDetailDto>(`${base}/${id}`, { params: { messageLimit } }),

    /**
     * A page of the thread's messages, oldest first: the newest `limit` before `beforeOrder`
     * (all the newest when omitted). Page back with the first message's `order`.
     */
    getMessages: (id: string, query: ThreadMessagePageQuery = {}) =>
      client.get<ThreadMessagePageDto>(`${base}/${id}/messages`, {
        params: {
          limit: query.limit ?? 50,
          ...(query.beforeOrder != null ? { beforeOrder: query.beforeOrder } : {}),
        },
      }),

    /**
     * An attachment's content (Blob): an image or PDF the user sent, or an image a tool
     * returned. Fetched through the client because the endpoint needs the signed-in caller,
     * which a plain `<img src>` cannot carry.
     */
    getAttachment: (threadId: string, attachmentId: string) =>
      client.download(`${base}/${threadId}/attachments/${attachmentId}`),

    /** Update thread title */
    updateTitle: (id: string, data: UpdateThreadTitleDto) =>
      client.patch<AgentThreadDto>(`${base}/${id}/title`, data),

    /**
     * Archive a thread: it leaves lists filtered by `status: Active`, its content stays. A new user
     * message in it unarchives it. Archiving an archived thread keeps its archive time.
     */
    archive: (id: string) =>
      client.post<AgentThreadDto>(`${base}/${id}/archive`),

    /** Unarchive a thread. */
    unarchive: (id: string) =>
      client.post<AgentThreadDto>(`${base}/${id}/unarchive`),

    /** Delete thread */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Export thread as JSON */
    exportJson: (id: string) =>
      client.get<ThreadExportDto>(`${base}/${id}/export/json`),

    /**
     * Export thread as a Markdown file (Blob).
     *
     * Fetched through the client rather than handed to the browser as a URL:
     * the endpoint needs a signed-in caller, and a browser navigation carries
     * no Authorization header, so a link always downloaded a 401.
     */
    exportMarkdown: (id: string) =>
      client.download(`${base}/${id}/export/markdown`),

    /** Submit message feedback */
    submitFeedback: (threadId: string, messageId: string, data: MessageFeedbackDto) =>
      client.post<void>(`${base}/${threadId}/messages/${messageId}/feedback`, data),

    /** Revoke message feedback */
    revokeFeedback: (threadId: string, messageId: string) =>
      client.delete<void>(`${base}/${threadId}/messages/${messageId}/feedback`),
  };
}

// ============================================
// User: Usage limits & model selection
// Routes: /usage-limits, /chat/models
// ============================================

/** The current user's own usage limits (percentages and reset times, no amounts). */
export function useUsageLimitApi(client: HttpClient) {
  return {
    /** Each window's used share and reset time. */
    getMine: () =>
      client.get<MyUsageLimitsDto>('/usage-limits/me'),
  };
}

/** The models the current user may pick in the chat. */
export function useChatModelApi(client: HttpClient) {
  return {
    /** Models for an agent (omit `agentId` for agentless chat, filtered by `provider` or the default provider). */
    getModels: (agentId?: string | null, provider?: string | null) =>
      client.get<ChatModelsDto>('/chat/models', {
        params: {
          ...(agentId ? { agentId } : {}),
          ...(provider ? { provider } : {}),
        },
      }),
  };
}

// ============================================
// User: Skill API
// Route: /skills
// ============================================

/** User-facing skill API */
export function useSkillApi(client: HttpClient) {
  const base = '/skills';
  return {
    /** Get all available skills for current user/tenant */
    getAvailable: () =>
      client.get<SkillSummaryDto[]>(base),

    /** Get skill detail by slug */
    getBySlug: (slug: string) =>
      client.get<SkillDetailDto>(`${base}/${slug}`),

    /** Search skills */
    search: (query: string, maxResults = 10) =>
      client.get<SkillSummaryDto[]>(`${base}/search`, { params: { query, maxResults } }),

    /** Activate skill with parameters */
    activate: (slug: string, data?: SkillActivateDto) =>
      client.post<SkillActivationResultDto>(`${base}/${slug}/activate`, data ?? {}),

    /** Create user skill */
    create: (data: CreateSkillDto) =>
      client.post<SkillDetailDto>(base, data),

    /** Update skill */
    update: (id: string, data: UpdateSkillDto) =>
      client.put<SkillDetailDto>(`${base}/${id}`, data),

    /** Delete skill */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),
  };
}

// ============================================
// Admin: Agent API
// Route: /admin/agents
// ============================================

/** Admin agent management API */
export function useAdminAgentApi(client: HttpClient) {
  const base = '/admin/agents';
  return {
    /** Get agent list (paged) */
    getList: (data?: AgentListQueryDto) =>
      client.post<PagedList<AgentDto>>(`${base}/query`, data ?? {}),

    /** Get agent by ID */
    getById: (id: string) =>
      client.get<AgentDto>(`${base}/${id}`),

    /** Create agent */
    create: (data: CreateAgentDto) =>
      client.post<AgentDto>(base, data),

    /** Update agent */
    update: (id: string, data: UpdateAgentDto) =>
      client.put<AgentDto>(`${base}/${id}`, data),

    /** Delete agent */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Clone agent */
    clone: (id: string, name?: string) =>
      client.post<AgentDto>(`${base}/${id}/clone`, undefined, { params: name ? { name } : undefined }),

    /** Run agent (non-streaming) */
    run: (id: string, data: RunAgentRequestDto) =>
      client.post<AgentResponseDto>(`${base}/${id}/run`, data),

    /** Build stream URL for agent run (SSE) */
    getRunStreamUrl: (id: string) =>
      client.resolveUrl(`${base}/${id}/run/stream`),

    /** Get agent version list (paged) */
    getVersions: (id: string, data?: AgentVersionQueryDto) =>
      client.post<PagedList<AgentVersionDto>>(`${base}/${id}/versions/query`, data ?? {}),

    /** Get specific version detail */
    getVersion: (id: string, version: number) =>
      client.get<AgentVersionDto>(`${base}/${id}/versions/${version}`),

    /** Rollback to specific version */
    rollbackToVersion: (id: string, version: number) =>
      client.post<AgentDto>(`${base}/${id}/versions/${version}/rollback`),

    /** Validate agent configuration */
    validate: (id: string) =>
      client.post<AgentValidationResultDto>(`${base}/${id}/validate`),

    /** Get agent health summary */
    getHealth: () =>
      client.get<AgentHealthSummaryDto>(`${base}/health`),

    /** Configure A/B test (route a traffic slice to version B). Returns the updated agent. */
    configureAbTest: (id: string, data: ConfigureAbTestDto) =>
      client.post<AgentDto>(`${base}/${id}/ab-test`, data),

    /** Stop the A/B test on an agent. Returns the updated agent. */
    stopAbTest: (id: string) =>
      client.delete<AgentDto>(`${base}/${id}/ab-test`),

    /** List assignable tool groups (catalog from the in-process tool registry) */
    getToolGroups: () =>
      client.get<ToolGroupDto[]>(`${base}/tool-groups`),

    /** List an agent's (admin-curated) memory entries, paged */
    getMemory: (id: string, data?: AgentMemoryListQueryDto) =>
      client.post<PagedList<AgentMemoryDto>>(`${base}/${id}/memory/query`, data ?? {}),

    /** Create a memory entry for an agent */
    createMemory: (id: string, data: CreateAgentMemoryDto) =>
      client.post<AgentMemoryDto>(`${base}/${id}/memory`, data),

    /** Update an agent memory entry */
    updateMemory: (id: string, memoryId: string, data: UpdateAgentMemoryDto) =>
      client.put<AgentMemoryDto>(`${base}/${id}/memory/${memoryId}`, data),

    /** Delete an agent memory entry */
    deleteMemory: (id: string, memoryId: string) =>
      client.delete<void>(`${base}/${id}/memory/${memoryId}`),
  };
}

// ============================================
// Admin: Agent Run API
// Route: /admin/agent-runs
// ============================================

/** Admin agent run management API */
export function useAdminAgentRunApi(client: HttpClient) {
  const base = '/admin/agent-runs';
  return {
    /** Get run statistics */
    getStats: () =>
      client.get<AgentRunStatsDto>(`${base}/stats`),

    /** Get run list (paged) */
    getList: (data: AgentRunQueryDto) =>
      client.post<PagedList<AgentRunDto>>(`${base}/query`, data),

    /** Get run by ID */
    getById: (runId: string) =>
      client.get<AgentRunDto>(`${base}/${runId}`),

    /** Get all nodes of a run */
    getNodes: (runId: string) =>
      client.get<AgentRunNodeDto[]>(`${base}/${runId}/nodes`),

    /** Get specific node detail */
    getNode: (runId: string, nodeId: string) =>
      client.get<AgentRunNodeDto>(`${base}/${runId}/nodes/${nodeId}`),

    /** Get traces for a node */
    getNodeTraces: (runId: string, nodeId: string) =>
      client.get<AgentRunTraceDto[]>(`${base}/${runId}/nodes/${nodeId}/traces`),

    /** Get all traces for a run */
    getRunTraces: (runId: string) =>
      client.get<AgentRunTraceDto[]>(`${base}/${runId}/traces`),

    /** Cancel a run */
    cancel: (runId: string) =>
      client.post<void>(`${base}/${runId}/cancel`),

    /** Approve a run (HITL) */
    approve: (runId: string, data?: ApproveRunDto) =>
      client.post<void>(`${base}/${runId}/approve`, data),

    /** Reject a run (HITL) */
    reject: (runId: string, data?: RejectRunDto) =>
      client.post<void>(`${base}/${runId}/reject`, data),

    /** Retry a failed node */
    retryNode: (runId: string, nodeId: string) =>
      client.post<void>(`${base}/${runId}/nodes/${nodeId}/retry`),
  };
}

// ============================================
// Admin: Thread API
// Route: /admin/threads
// ============================================

/** Admin thread management API */
export function useAdminThreadApi(client: HttpClient) {
  const base = '/admin/threads';
  return {
    /** Create thread */
    create: (data: CreateAgentThreadDto) =>
      client.post<AgentThreadDto>(base, data),

    /** Get thread list (paged) */
    getList: (data: ThreadListQueryDto) =>
      client.post<PagedList<AgentThreadDto>>(`${base}/query`, data),

    /** Get thread by ID */
    getById: (id: string) =>
      client.get<AgentThreadDto>(`${base}/${id}`),

    /** Get thread detail with messages */
    getDetail: (id: string, messageLimit = 50) =>
      client.get<AgentThreadDetailDto>(`${base}/${id}/detail`, { params: { messageLimit } }),

    /** Update thread title */
    updateTitle: (id: string, data: UpdateThreadTitleDto) =>
      client.patch<AgentThreadDto>(`${base}/${id}/title`, data),

    /** Delete thread */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Export thread as JSON */
    exportJson: (id: string) =>
      client.get<ThreadExportDto>(`${base}/${id}/export/json`),

    /**
     * Export thread as a Markdown file (Blob).
     *
     * Fetched through the client rather than handed to the browser as a URL:
     * the endpoint needs a signed-in caller, and a browser navigation carries
     * no Authorization header, so a link always downloaded a 401.
     */
    exportMarkdown: (id: string) =>
      client.download(`${base}/${id}/export/markdown`),
  };
}

// ============================================
// Admin: Usage limits API
// Route: /admin/ai/usage-limits
// ============================================

/** Admin usage limits & model policy API */
export function useAdminUsageLimitApi(client: HttpClient) {
  const base = '/admin/ai/usage-limits';
  return {
    /** Everyone's defaults: saved, configuration level, effective, model list. */
    getDefaults: () =>
      client.get<UsageLimitDefaultsDto>(`${base}/defaults`),

    /** Save everyone's defaults. */
    saveDefaults: (data: SaveUsagePolicyDto) =>
      client.put<UsageLimitDefaultsDto>(`${base}/defaults`, data),

    /** One user's own settings, what they resolve to, and current usage. */
    getUser: (userId: string) =>
      client.get<UserUsageLimitsDto>(`${base}/users/${userId}`),

    /** Save one user's own settings. */
    saveUser: (userId: string, data: SaveUsagePolicyDto) =>
      client.put<UserUsageLimitsDto>(`${base}/users/${userId}`, data),

    /** Remove one user's own settings: everything follows the defaults again. */
    clearUser: (userId: string) =>
      client.delete<void>(`${base}/users/${userId}`),

    /** Clear one user's current windows. */
    resetUser: (userId: string) =>
      client.post<void>(`${base}/users/${userId}/reset`),

    /** Users who used AI, most recent first, with their windows. */
    queryUsers: (data: UsageMeterQueryDto) =>
      client.post<PagedList<UserUsageSummaryDto>>(`${base}/users/query`, data),

    /**
     * Get the USD cost budget summary for a tenant/time-range.
     * All params optional - omit `tenantId` for the global view; start/end
     * default to the current calendar month on the backend.
     */
    getBudgetSummary: (params?: { tenantId?: string; startTime?: string; endTime?: string }) =>
      client.get<BudgetSummaryDto>(`${base}/budget/summary`, params ? { params } : undefined),
  };
}

// ============================================
// Admin: Provider API
// Route: /admin/ai/providers
// ============================================

/** Admin AI provider API */
export function useAdminProviderApi(client: HttpClient) {
  const base = '/admin/ai/providers';
  return {
    /** Get all enabled providers */
    getProviders: () =>
      client.get<string[]>(base),

    /** Get default model for a provider */
    getDefaultModel: (providerName: string) =>
      client.get<ProviderDefaultModelDto>(`${base}/${providerName}/default-model`),

    /** Provider dropdown options (enabled only) - for the Agent config Provider select */
    getOptions: () =>
      client.get<ProviderOptionDto[]>(`${base}/options`),

    /** List models for a provider entity (live /v1/models + static fallback) */
    listModels: (id: string) =>
      client.get<ProviderModelsDto>(`${base}/entities/${id}/models`),

    // ---- Entity-driven CRUD (Phase 5 backend prereq) ----------------------

    /** Paged list of Provider entities */
    getList: (query: ProviderQueryDto) =>
      client.post<PagedList<ProviderDto>>(`${base}/query`, query),

    /** Get Provider entity by id */
    getById: (id: string) =>
      client.get<ProviderDto>(`${base}/entities/${id}`),

    /** Create Provider entity */
    create: (data: CreateProviderDto) =>
      client.post<ProviderDto>(`${base}/entities`, data),

    /** Update Provider entity (apiKey null = keep, '' = clear, non-empty = rotate) */
    update: (id: string, data: UpdateProviderDto) =>
      client.put<ProviderDto>(`${base}/entities/${id}`, data),

    /** Soft-delete Provider entity */
    delete: (id: string) =>
      client.delete(`${base}/entities/${id}`),

    /**
     * Reorder the provider list (drag-and-drop).
     *
     * Writes display order only. `Priority` is untouched: that one decides which
     * row wins at runtime when several share a name, and rearranging a list
     * should not change which provider actually gets called. Configuration-source
     * entries have no row and cannot take part.
     */
    reorder: (data: ReorderRequest) =>
      client.post<void>(`${base}/reorder`, data),

    /** Test Provider connection (shallow probe) */
    test: (id: string) =>
      client.post<ProviderTestResultDto>(`${base}/entities/${id}/test`, {}),
  };
}

// ============================================
// Admin: Usage Analytics API
// Route: /admin/ai/usage
// ============================================

/** Admin usage analytics API */
export function useAdminUsageAnalyticsApi(client: HttpClient) {
  const base = '/admin/ai/usage';
  return {
    /** Get usage summary */
    getSummary: (data: UsageSummaryQueryDto) =>
      client.post<UsageSummaryDto>(`${base}/summary`, data),

    /** Get usage logs (paged) */
    getLogs: (data: UsageLogQueryDto) =>
      client.post<PagedList<UsageLogDto>>(`${base}/logs`, data),

    /** Get usage by provider */
    getByProvider: (startTime: string, endTime: string) =>
      client.get<ProviderUsageDto[]>(`${base}/by-provider`, { params: { startTime, endTime } }),

    /** Get usage by model (optionally narrowed to one provider and/or one agent) */
    getByModel: (startTime: string, endTime: string, provider?: string, agentId?: string) =>
      client.get<ModelUsageDto[]>(`${base}/by-model`, { params: { startTime, endTime, provider, agentId } }),

    /** Get usage trend (time series) */
    getTrend: (data: UsageTrendQueryDto) =>
      client.post<UsageTrendPointDto[]>(`${base}/trend`, data),

    /** Get usage and estimated cost by agent (top N; optionally narrowed by provider, model and/or agent) */
    getByAgent: (startTime: string, endTime: string, topN = 20, filters: UsageBreakdownFilters = {}) =>
      client.get<AgentUsageDto[]>(`${base}/by-agent`, { params: { startTime, endTime, topN, ...filters } }),

    /** Get agent feedback statistics */
    getAgentFeedbackStats: (topN = 20) =>
      client.get<AgentFeedbackStatsDto[]>(`${base}/agent-feedback-stats`, { params: { topN } }),

    /** Get cost summary (optionally narrowed by provider, model and/or agent) */
    getCostSummary: (startTime: string, endTime: string, provider?: string, model?: string, agentId?: string) =>
      client.get<CostSummaryDto>(`${base}/cost-summary`, { params: { startTime, endTime, provider, model, agentId } }),
  };
}

// ============================================
// Admin: Evaluation API
// Route: /admin/ai/evaluations
// ============================================

/** Admin evaluation management API */
export function useAdminEvaluationApi(client: HttpClient) {
  const base = '/admin/ai/evaluations';
  return {
    /** Get evaluation run by ID */
    getById: (id: string) =>
      client.get<EvaluationRunDetailDto>(`${base}/${id}`),

    /** Get evaluation run list (paged) */
    getList: (data: EvaluationRunQueryDto) =>
      client.post<PagedList<EvaluationRunDto>>(`${base}/query`, data),

    /** Delete evaluation run */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Create and run an evaluation in one shot */
    create: (data: CreateEvaluationRunDto) =>
      client.post<EvaluationRunDetailDto>(`${base}/run`, data),

    /** Run a batch evaluation across multiple agents/versions */
    runBatch: (data: BatchEvaluationDto) =>
      client.post<BatchEvaluationResultDto>(`${base}/batch`, data),

    /** Get an agent's evaluation score trend over its last N runs. */
    getTrend: (agentId: string, lastNRuns = 10) =>
      client.get<EvaluationTrendDto>(`${base}/agents/${agentId}/trend`, { params: { lastNRuns } }),

    /** Compare two agent versions' aggregate evaluation stats. */
    compareVersions: (agentId: string, versionA: number, versionB: number) =>
      client.get<VersionComparisonDto>(`${base}/agents/${agentId}/compare`, {
        params: { versionA, versionB },
      }),
  };
}

// ============================================
// Admin: Skill API
// Route: /admin/skills
// ============================================

/** Admin skill management API */
export function useAdminSkillApi(client: HttpClient) {
  const base = '/admin/skills';
  return {
    /** Get skills (paged with filters) */
    getPaged: (queryParams?: SkillQueryDto) =>
      client.get<PagedList<SkillSummaryDto>>(base, queryParams ? { params: queryParams } : undefined),

    /** Get skill detail by slug */
    getBySlug: (slug: string) =>
      client.get<SkillDetailDto>(`${base}/${slug}`),

    /** Search skills */
    search: (query: string, maxResults = 10) =>
      client.get<SkillSummaryDto[]>(`${base}/search`, { params: { query, maxResults } }),

    /** Create tenant skill */
    create: (data: CreateSkillDto) =>
      client.post<SkillDetailDto>(base, data),

    /** Update skill */
    update: (id: string, data: UpdateSkillDto) =>
      client.put<SkillDetailDto>(`${base}/${id}`, data),

    /** Delete skill */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Batch delete skills */
    batchDelete: (ids: string[]) =>
      client.post<number>(`${base}/batch-delete`, ids),

    /** Batch enable skills */
    batchEnable: (ids: string[]) =>
      client.post<number>(`${base}/batch-enable`, ids),

    /** Batch disable skills */
    batchDisable: (ids: string[]) =>
      client.post<number>(`${base}/batch-disable`, ids),

    /** Get usage statistics */
    getUsageStats: () =>
      client.get<SkillUsageStatsDto>(`${base}/stats`),

    /** Get popular skills */
    getPopular: (topN = 10) =>
      client.get<PopularSkillDto[]>(`${base}/popular`, { params: { topN } }),

    /** Export skills */
    export: (scope?: SkillScope) =>
      client.get<SkillExportDto[]>(`${base}/export`, scope !== undefined ? { params: { scope } } : undefined),

    /** Import skills */
    import: (data: SkillImportRequestDto) =>
      client.post<SkillImportResultDto>(`${base}/import`, data),
  };
}

// ============================================
// Admin: Workflow API
// Route: /admin/workflows
// ============================================

/** Admin workflow management API */
export function useAdminWorkflowApi(client: HttpClient) {
  const base = '/admin/workflows';
  return {
    // --- Definition CRUD ---

    /** Create workflow */
    create: (data: CreateWorkflowDefinitionDto) =>
      client.post<WorkflowDefinitionDto>(base, data),

    /** Update workflow */
    update: (id: string, data: UpdateWorkflowDefinitionDto) =>
      client.put<WorkflowDefinitionDto>(`${base}/${id}`, data),

    /** Delete workflow */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Get workflow by ID */
    getById: (id: string) =>
      client.get<WorkflowDefinitionDto>(`${base}/${id}`),

    /** Get workflow list (paged) */
    getList: (data?: WorkflowDefinitionQueryDto) =>
      client.post<PagedList<WorkflowDefinitionDto>>(`${base}/query`, data ?? {}),

    // --- Execution ---

    /** Run workflow (non-streaming) */
    run: (id: string, data: RunWorkflowRequestDto) =>
      client.post<WorkflowExecutionResultDto>(`${base}/${id}/run`, data),

    /** Build stream URL for workflow run (SSE) */
    getRunStreamUrl: (id: string) =>
      client.resolveUrl(`${base}/${id}/run/stream`),

    /** Clone workflow */
    clone: (id: string, data?: CloneWorkflowRequestDto) =>
      client.post<WorkflowDefinitionDto>(`${base}/${id}/clone`, data),

    // --- Execution Management ---

    /** Get execution status */
    getExecutionStatus: (executionId: string) =>
      client.get<WorkflowExecutionStatusDto>(`${base}/executions/${executionId}/status`),

    /** Get execution detail */
    getExecutionDetail: (executionId: string) =>
      client.get<WorkflowExecutionDetailDto>(`${base}/executions/${executionId}/detail`),

    /** Resume paused execution */
    resumeExecution: (executionId: string) =>
      client.post<WorkflowExecutionResultDto>(`${base}/executions/${executionId}/resume`),

    /** Approve workflow step */
    approveStep: (executionId: string, stepId: string, data?: WorkflowStepApprovalDto) =>
      client.post<void>(`${base}/executions/${executionId}/steps/${stepId}/approve`, data),

    /** Reject workflow step */
    rejectStep: (executionId: string, stepId: string, data: WorkflowStepApprovalDto) =>
      client.post<void>(`${base}/executions/${executionId}/steps/${stepId}/reject`, data),

    /**
     * Cancel an execution. A paused / awaiting execution is cancelled at once;
     * a running one gets a cancel signal queued in its mailbox (the engine
     * stops at its next checkpoint). `feedback` becomes the signal's reason.
     */
    cancelExecution: (executionId: string, data?: WorkflowStepApprovalDto) =>
      client.post<void>(`${base}/executions/${executionId}/cancel`, data),

    /** Pending human-in-the-loop interrupt (404 when the execution is not awaiting input) */
    getPendingInterrupt: (executionId: string) =>
      client.get<WorkflowInterruptDto>(`${base}/executions/${executionId}/interrupt`),

    /** Resume an execution awaiting input with the operator's answer */
    resumeWithInput: (executionId: string, data: ResumeWorkflowInputDto) =>
      client.post<WorkflowExecutionResultDto>(`${base}/executions/${executionId}/resume-with-input`, data),

    /** Pending signals in the execution's mailbox (501 when no mailbox is registered) */
    getPendingSignals: (executionId: string) =>
      client.get<WorkflowExecutionSignalDto[]>(`${base}/executions/${executionId}/signals`),

    /** Get execution history (paged) */
    getExecutions: (queryParams?: WorkflowExecutionQueryDto) =>
      client.get<PagedList<WorkflowExecutionSummaryDto>>(`${base}/executions`, queryParams ? { params: queryParams } : undefined),

    // --- Batch & Analytics ---

    /** Batch delete workflows */
    batchDelete: (ids: string[]) =>
      client.post<number>(`${base}/batch-delete`, ids),

    /**
     * Batch enable workflows. Workflow definitions have a single `IsEnabled`
     * flag - there is no separate Draft/Published state machine, so enabling
     * a workflow IS the framework's "publish" semantic.
     * See docs/modules/ai.md → Workflow → Publish semantics.
     */
    batchEnable: (ids: string[]) =>
      client.post<number>(`${base}/batch-enable`, ids),

    /** Batch disable workflows */
    batchDisable: (ids: string[]) =>
      client.post<number>(`${base}/batch-disable`, ids),

    /** Get workflow statistics */
    getStats: () =>
      client.get<WorkflowStatsDto>(`${base}/stats`),

    /** Validate workflow definition */
    validate: (id: string) =>
      client.post<WorkflowValidationResultDto>(`${base}/${id}/validate`),

    /** Per-workflow execution statistics (count, success rate, duration percentiles) */
    getExecutionStats: (id: string) =>
      client.get<WorkflowExecutionStatsDto>(`${base}/${id}/execution-stats`),

    // --- Versions ---

    /** Version history, newest first (snapshots without the definition body) */
    getVersions: (id: string) =>
      client.get<WorkflowDefinitionVersionDto[]>(`${base}/${id}/versions`),

    /** One version including its full definition snapshot */
    getVersion: (id: string, versionNumber: number) =>
      client.get<WorkflowDefinitionVersionDto>(`${base}/${id}/versions/${versionNumber}`),

    /** Restore a version; the current definition is snapshotted first */
    restoreVersion: (id: string, versionNumber: number, data?: RestoreWorkflowVersionRequestDto) =>
      client.post<void>(`${base}/${id}/versions/${versionNumber}/restore`, data),
  };
}

// ============================================
// Admin: MCP API
// Route: /admin/mcp
// ============================================

/** Admin MCP server management API */
export function useAdminMcpApi(client: HttpClient) {
  const base = '/admin/mcp';
  // External MCP server registry CRUD moved to AIMcpClientModule (Tnzi as MCP client)
  const clientBase = '/admin/mcp/client/servers';
  return {
    /** Get MCP server status */
    getStatus: () =>
      client.get<McpServerStatusDto>(`${base}/status`),

    /** Get registered MCP tools */
    getTools: () =>
      client.get<McpToolInfoDto[]>(`${base}/tools`),

    /** Get exposed agent IDs */
    getExposedAgents: () =>
      client.get<string[]>(`${base}/agents`),

    /** Expose an agent as MCP tool */
    exposeAgent: (agentId: string, options?: McpToolExposureOptionsDto) =>
      client.post<void>(`${base}/agents/${agentId}/expose`, options),

    /** Remove an exposed agent */
    removeAgent: (agentId: string) =>
      client.delete<void>(`${base}/agents/${agentId}`),

    // ---- External MCP Server Registration CRUD ----
    // Catalog of EXTERNAL MCP servers Tnzi can connect to as a client. Auth tokens
    // are encrypted server-side; DTOs only expose `hasAuthToken` boolean. Backed by
    // AIMcpClientModule's DefaultMcpClientAdminController at /admin/mcp/client/servers/*.

    /** Page MCP server registrations */
    getList: (query: McpServerRegistrationQueryDto) =>
      client.post<PagedList<McpServerRegistrationDto>>(`${clientBase}/query`, query),

    /** Get a single MCP server registration by id */
    getById: (id: string) =>
      client.get<McpServerRegistrationDto>(`${clientBase}/${id}`),

    /** Create a new MCP server registration */
    create: (data: CreateMcpServerRegistrationDto) =>
      client.post<McpServerRegistrationDto>(`${clientBase}`, data),

    /** Update an existing MCP server registration */
    update: (id: string, data: UpdateMcpServerRegistrationDto) =>
      client.put<McpServerRegistrationDto>(`${clientBase}/${id}`, data),

    /** Soft-delete an MCP server registration */
    delete: (id: string) =>
      client.delete<void>(`${clientBase}/${id}`),

    /** Shallow probe - verifies entity is enabled, URI parses, credentials decrypt */
    test: (id: string) =>
      client.post<McpServerTestResultDto>(`${clientBase}/${id}/test`, {}),
  };
}

// ============================================
// Admin: MCP Tool Analytics API
// Route: /admin/ai/mcp/tool-analytics
// ============================================

/** Admin MCP tool analytics API */
export function useAdminMcpToolAnalyticsApi(client: HttpClient) {
  const base = '/admin/ai/mcp/tool-analytics';
  return {
    /** Get statistics for a specific tool */
    getToolStats: (toolName: string, from?: string, to?: string) =>
      client.get<McpToolStatsDto>(`${base}/stats/${encodeURIComponent(toolName)}`, { params: { from, to } }),

    /** Get most used tools ranking */
    getMostUsedTools: (top = 10, from?: string, to?: string) =>
      client.get<McpToolPopularityDto[]>(`${base}/most-used`, { params: { top, from, to } }),

    /** Get errors for a specific tool */
    getToolErrors: (toolName: string, top = 20) =>
      client.get<McpToolErrorDto[]>(`${base}/errors/${encodeURIComponent(toolName)}`, { params: { top } }),

    /** Cleanup old analytics records */
    cleanup: (retentionDays = 90) =>
      client.delete<number>(`${base}/cleanup`, { params: { retentionDays } }),
  };
}

// ============================================
// Admin: Skill Category API
// Route: /admin/ai/skill-categories
// ============================================

/** Admin skill category management API */
export function useAdminSkillCategoryApi(client: HttpClient) {
  const base = '/admin/ai/skill-categories';
  return {
    /** Get category tree */
    getTree: () =>
      client.get<SkillCategoryDto[]>(`${base}/tree`),

    /** Create category */
    create: (data: CreateSkillCategoryDto) =>
      client.post<SkillCategoryDto>(base, data),

    /** Update category */
    update: (id: string, data: UpdateSkillCategoryDto) =>
      client.put<SkillCategoryDto>(`${base}/${id}`, data),

    /** Delete category */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Get skills in a category */
    getSkillsByCategory: (categoryId: string) =>
      client.get<SkillSummaryDto[]>(`${base}/${categoryId}/skills`),

    /**
     * Reorder categories under one parent (drag-and-drop).
     * `ids` is the visible order; categories outside those slots keep their positions.
     */
    reorder: (data: ReorderRequest, parentId?: string | null) =>
      client.post<void>(`${base}/reorder`, data, {
        params: parentId ? { parentId } : undefined,
      }),
  };
}

// ============================================
// User: Artifact API
// Route: /artifacts
// ============================================

/** User-facing artifact API */
export function useArtifactApi(client: HttpClient) {
  const base = '/artifacts';
  return {
    /** Get artifacts by thread (the endpoint has no unscoped listing) */
    getByThread: (threadId: string) =>
      client.get<AgentArtifactDto[]>(base, { params: { threadId } }),

    /** Get artifacts by run */
    getByRun: (runId: string) =>
      client.get<AgentArtifactDto[]>(`${base}/by-run`, { params: { runId } }),

    /** Get artifact by ID */
    getById: (id: string) =>
      client.get<AgentArtifactDto>(`${base}/${id}`),
  };
}

// ============================================
// User: AI Profile API
// Route: /user-profile
// ============================================

/** User-facing AI profile API */
export function useUserProfileApi(client: HttpClient) {
  const base = '/user-profile';
  return {
    /** Get current user's AI profile */
    get: () =>
      client.get<UserProfileDto>(base),

    /** Update current user's AI profile */
    update: (data: UpdateUserProfileDto) =>
      client.put<UserProfileDto>(base, data),
  };
}

// ============================================
// Admin: Workspace Agent / Persona API (read-only)
// Route: /admin/ai/workspace
// ============================================

import type { WorkspaceAgentDto, WorkspacePersonaDto } from './types';

/**
 * Read-only admin access to workspace-discovered Agent + Persona files.
 * Mirrors `Tnzi.AI/Controllers/Admin/DefaultWorkspaceAgentAdminController`.
 *
 * Workspace agents/personas come from AGENT.md / PERSONA.md files on disk
 * (under `~/.tnzi/agents/` or the project's `.agents/` directory). They
 * are surfaced read-only because editing should happen on disk; files are
 * re-read on every request, so an edit shows up on the next listing.
 */
export function useAdminWorkspaceAgentApi(client: HttpClient) {
  const base = '/admin/ai/workspace';
  return {
    listAgents: () =>
      client.get<WorkspaceAgentDto[]>(`${base}/agents`),
    listPersonas: () =>
      client.get<WorkspacePersonaDto[]>(`${base}/personas`),
  };
}
