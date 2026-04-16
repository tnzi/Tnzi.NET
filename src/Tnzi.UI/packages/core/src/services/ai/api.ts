/**
 * AI Module API - Complete backend endpoint coverage
 *
 * Covers: Chat, Threads, Agents (admin), Agent Runs (admin), Providers (admin),
 * Quotas (user + admin), Usage Analytics (admin), Evaluations (admin),
 * Skills (user + admin), Workflows (admin), MCP (admin), MCP Tool Analytics (admin),
 * Personas (admin), Skill Categories (admin), Artifacts (user), User Profile (user)
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  // Chat
  ChatRequestDto,
  ChatResponseDto,
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
  // Quotas
  UserQuotaDto,
  UserQuotaQueryDto,
  SetQuotaDto,
  ResetQuotaDto,
  // Usage Analytics
  UsageSummaryDto,
  UsageSummaryQueryDto,
  UsageLogDto,
  UsageLogQueryDto,
  ProviderUsageDto,
  ModelUsageDto,
  UsageTrendPointDto,
  UsageTrendQueryDto,
  AgentUsageDto,
  AgentFeedbackStatsDto,
  CostSummaryDto,
  // Providers
  ProviderDefaultModelDto,
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
  // Personas
  AgentPersonaDto,
  CreateAgentPersonaDto,
  UpdateAgentPersonaDto,
  AgentPersonaQueryDto,
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

    /** Update thread title */
    updateTitle: (id: string, data: UpdateThreadTitleDto) =>
      client.patch<AgentThreadDto>(`${base}/${id}/title`, data),

    /** Delete thread */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),

    /** Export thread as JSON */
    exportJson: (id: string) =>
      client.get<ThreadExportDto>(`${base}/${id}/export/json`),

    /** Get export markdown URL (file download) */
    getExportMarkdownUrl: (id: string) =>
      client.resolveUrl(`${base}/${id}/export/markdown`),

    /** Submit message feedback */
    submitFeedback: (threadId: string, messageId: string, data: MessageFeedbackDto) =>
      client.post<void>(`${base}/${threadId}/messages/${messageId}/feedback`, data),

    /** Revoke message feedback */
    revokeFeedback: (threadId: string, messageId: string) =>
      client.delete<void>(`${base}/${threadId}/messages/${messageId}/feedback`),
  };
}

// ============================================
// User: Quota API
// Route: /quotas
// ============================================

/** User-facing quota API (current user) */
export function useQuotaApi(client: HttpClient) {
  return {
    /** Get current user's quota info */
    getMyQuota: () =>
      client.get<UserQuotaDto>('/quotas/me'),
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

    /** Get export markdown URL (file download) */
    getExportMarkdownUrl: (id: string) =>
      client.resolveUrl(`${base}/${id}/export/markdown`),
  };
}

// ============================================
// Admin: Quota API
// Route: /admin/quotas
// ============================================

/** Admin quota management API */
export function useAdminQuotaApi(client: HttpClient) {
  const base = '/admin/quotas';
  return {
    /** Get quota for a user */
    getQuota: (userId: string) =>
      client.get<UserQuotaDto>(`${base}/${userId}`),

    /** Get user quotas (paged) */
    getList: (data: UserQuotaQueryDto) =>
      client.post<PagedList<UserQuotaDto>>(`${base}/query`, data),

    /** Set user quota */
    setQuota: (data: SetQuotaDto) =>
      client.post<UserQuotaDto>(base, data),

    /** Reset user quota */
    resetQuota: (data: ResetQuotaDto) =>
      client.post<void>(`${base}/reset`, data),
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

    /** Get usage by model */
    getByModel: (startTime: string, endTime: string, provider?: string) =>
      client.get<ModelUsageDto[]>(`${base}/by-model`, { params: { startTime, endTime, provider } }),

    /** Get usage trend (time series) */
    getTrend: (data: UsageTrendQueryDto) =>
      client.post<UsageTrendPointDto[]>(`${base}/trend`, data),

    /** Get usage by agent (top N) */
    getByAgent: (startTime: string, endTime: string, topN = 20) =>
      client.get<AgentUsageDto[]>(`${base}/by-agent`, { params: { startTime, endTime, topN } }),

    /** Get agent feedback statistics */
    getAgentFeedbackStats: (topN = 20) =>
      client.get<AgentFeedbackStatsDto[]>(`${base}/agent-feedback-stats`, { params: { topN } }),

    /** Get cost summary */
    getCostSummary: (startTime: string, endTime: string, provider?: string, model?: string) =>
      client.get<CostSummaryDto>(`${base}/cost-summary`, { params: { startTime, endTime, provider, model } }),
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

    /** Get execution history (paged) */
    getExecutions: (queryParams?: WorkflowExecutionQueryDto) =>
      client.get<PagedList<WorkflowExecutionSummaryDto>>(`${base}/executions`, queryParams ? { params: queryParams } : undefined),

    // --- Batch & Analytics ---

    /** Batch delete workflows */
    batchDelete: (ids: string[]) =>
      client.post<number>(`${base}/batch-delete`, ids),

    /**
     * Batch enable workflows. Workflow definitions have a single `IsEnabled`
     * flag — there is no separate Draft/Published state machine, so enabling
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
  };
}

// ============================================
// Admin: MCP API
// Route: /admin/mcp
// ============================================

/** Admin MCP server management API */
export function useAdminMcpApi(client: HttpClient) {
  const base = '/admin/mcp';
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

    // ---- Entity-driven MCP Server Registration CRUD (Phase 5 backend prereq) ----
    // Catalog of EXTERNAL MCP servers Tnzi can connect to as a client. Auth tokens
    // are encrypted server-side; DTOs only expose `hasAuthToken` boolean. Routes
    // live under /admin/mcp/entities/* alongside the legacy hosting endpoints.

    /** Page MCP server registrations */
    getList: (query: McpServerRegistrationQueryDto) =>
      client.post<PagedList<McpServerRegistrationDto>>(`${base}/entities/query`, query),

    /** Get a single MCP server registration by id */
    getById: (id: string) =>
      client.get<McpServerRegistrationDto>(`${base}/entities/${id}`),

    /** Create a new MCP server registration */
    create: (data: CreateMcpServerRegistrationDto) =>
      client.post<McpServerRegistrationDto>(`${base}/entities`, data),

    /** Update an existing MCP server registration */
    update: (id: string, data: UpdateMcpServerRegistrationDto) =>
      client.put<McpServerRegistrationDto>(`${base}/entities/${id}`, data),

    /** Soft-delete an MCP server registration */
    delete: (id: string) =>
      client.delete<void>(`${base}/entities/${id}`),

    /** Shallow probe — verifies entity is enabled, URI parses, credentials decrypt */
    test: (id: string) =>
      client.post<McpServerTestResultDto>(`${base}/entities/${id}/test`, {}),
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
      client.get<McpToolStatsDto>(`${base}/stats/${toolName}`, { params: { from, to } }),

    /** Get most used tools ranking */
    getMostUsedTools: (top = 10, from?: string, to?: string) =>
      client.get<McpToolPopularityDto[]>(`${base}/most-used`, { params: { top, from, to } }),

    /** Get errors for a specific tool */
    getToolErrors: (toolName: string, top = 20) =>
      client.get<McpToolErrorDto[]>(`${base}/errors/${toolName}`, { params: { top } }),

    /** Cleanup old analytics records */
    cleanup: (retentionDays = 90) =>
      client.delete<number>(`${base}/cleanup`, { params: { retentionDays } }),
  };
}

// ============================================
// Admin: Persona API
// Route: /admin/ai/personas
// ============================================

/** Admin persona management API */
export function useAdminPersonaApi(client: HttpClient) {
  const base = '/admin/ai/personas';
  return {
    /** Get persona list (paged) */
    getList: (data: AgentPersonaQueryDto) =>
      client.post<PagedList<AgentPersonaDto>>(`${base}/query`, data),

    /** Get persona by ID */
    getById: (id: string) =>
      client.get<AgentPersonaDto>(`${base}/${id}`),

    /** Get persona by slug */
    getBySlug: (slug: string) =>
      client.get<AgentPersonaDto>(`${base}/by-slug/${slug}`),

    /** Create persona */
    create: (data: CreateAgentPersonaDto) =>
      client.post<AgentPersonaDto>(base, data),

    /** Update persona */
    update: (id: string, data: UpdateAgentPersonaDto) =>
      client.put<AgentPersonaDto>(`${base}/${id}`, data),

    /** Delete persona */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),
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
    /** Get artifact list (paged query) */
    getList: (params?: Record<string, unknown>) =>
      client.get<AgentArtifactDto[]>(base, { params }),

    /** Get artifacts by thread */
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
