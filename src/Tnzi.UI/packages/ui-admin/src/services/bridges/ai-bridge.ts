/**
 * AI bridge — full fill (Phase 5 Task 5.1).
 *
 * Adapts the @tnzi/core AI admin API factories to the BridgeCrudContract shape
 * used by all TCrudPage-based management pages.
 *
 * IMPORTANT — plan-vs-reality drift (vs 2026-04-12 plan §5.1):
 *   The original plan assumed 13 service singletons (`AgentService.default`, etc.)
 *   each with a uniform `getPagedList/create/update/deleteMany` shape. The actual
 *   `@tnzi/core/services/ai` exports 19 factory functions (`useAdmin*Api(client)`)
 *   and the surface differs per resource. This bridge keeps the 13-sub-contract
 *   shape consumers expect, but resolves drift as follows:
 *
 *   1. agents          → useAdminAgentApi.getList/create/update/delete (loop for many)
 *   2. threads         → useAdminThreadApi.getList/create/updateTitle/delete (no plain update)
 *   3. agentRuns       → useAdminAgentRunApi.getList/cancel; tail() rejects (SSE belongs in
 *                        page-level streaming via getRunStreamUrl on the agent api, not here)
 *   4. workflows       → useAdminWorkflowApi.getList/create/update/delete + clone + validate
 *                        + publish (publish wraps batchEnable — IsEnabled IS the framework
 *                        publish semantic; see docs/modules/ai.md Workflow section)
 *   5. workflowRuns    → useAdminWorkflowApi.getExecutions/getExecutionDetail (NO separate
 *                        useAdminWorkflowRunApi exists in core); tail() rejects
 *   6. skills          → useAdminSkillApi.getPaged/create/update/delete + batchEnable/Disable
 *                        re-exposed as activate(id)/deactivate(id) for plan compatibility
 *   7. providers       → ENTITY-DRIVEN CRUD: useAdminProviderApi.{getList,create,update,delete,test}
 *                        wired against POST /admin/ai/providers/query and entities/* sub-routes.
 *                        Backend stores providers in the AI_Provider table with API keys encrypted
 *                        via IDataProtectionProvider. The legacy config-driven {getProviders,
 *                        getDefaultModel} endpoints are still exposed for backwards compatibility
 *                        but are NOT used by the bridge.
 *   8. usage           → useAdminUsageAnalyticsApi.{getSummary,getByAgent,getByModel,getTrend}
 *                        — query.filters must supply {startTime,endTime}
 *   9. knowledge       → useAdminKnowledgeBaseApi.{getList,create,update,delete,reindex}
 *                        reindex wired to POST /admin/knowledge-bases/{id}/reindex (Phase 5 prereq)
 *  10. mcpServers      → ENTITY-DRIVEN CRUD (Phase 5 backend prereq):
 *                        useAdminMcpApi.{getList,getById,create,update,delete,test} wired against
 *                        POST /admin/mcp/client/servers/query and servers/* sub-routes. Backend stores
 *                        external MCP server registrations in the AI_McpServerRegistration table
 *                        with auth tokens encrypted via IDataProtectionProvider. Legacy hosting
 *                        endpoints (status, tools, agent exposure) remain on the same factory but
 *                        are unrelated to this CRUD surface.
 *  11. quota           → useAdminQuotaApi.{getList,getQuota,setQuota,resetQuota}; fetch() pages
 *                        via POST /admin/quotas/query (Phase 5 prereq)
 *  12. personas        → useAdminPersonaApi.getList/create/update/delete
 *  13. evaluations     → useAdminEvaluationApi.getList/getById/delete + create (POST /run) +
 *                        runBatch (POST /batch); update rejects, run(id) rejects (backend
 *                        creates+runs in one call — use create() instead)
 *
 *   Bridges that "reject not-implemented" follow the chat-bridge / notification-bridge stub
 *   pattern from Phase 3. Pages downstream MUST avoid wiring to those operations until the
 *   matching backend lands.
 */
import {
  useAdminAgentApi,
  useAdminAgentRunApi,
  useAdminThreadApi,
  useAdminWorkflowApi,
  useAdminSkillApi,
  useAdminProviderApi,
  useAdminUsageAnalyticsApi,
  useAdminQuotaApi,
  useAdminPersonaApi,
  useAdminEvaluationApi,
  useAdminMcpApi,
  UsageGranularity,
  type AgentDto,
  type CreateAgentDto,
  type UpdateAgentDto,
  type AgentListQueryDto,
  type AgentRunDto,
  type AgentRunQueryDto,
  type AgentRunTraceDto,
  type AgentThreadDto,
  type CreateAgentThreadDto,
  type ThreadListQueryDto,
  type WorkflowDefinitionDto,
  type CreateWorkflowDefinitionDto,
  type UpdateWorkflowDefinitionDto,
  type WorkflowDefinitionQueryDto,
  type WorkflowExecutionSummaryDto,
  type WorkflowExecutionDetailDto,
  type WorkflowExecutionResultDto,
  type WorkflowExecutionStatusDto,
  type WorkflowValidationResultDto,
  type WorkflowStatsDto,
  type WorkflowStepApprovalDto,
  type RunWorkflowRequestDto,
  type SkillSummaryDto,
  type SkillDetailDto,
  type CreateSkillDto,
  type UpdateSkillDto,
  type SkillQueryDto,
  type UsageSummaryDto,
  type ProviderUsageDto,
  type ModelUsageDto,
  type AgentUsageDto,
  type UsageTrendPointDto,
  type UserQuotaDto,
  type UserQuotaQueryDto,
  type SetQuotaDto,
  type AgentPersonaDto,
  type CreateAgentPersonaDto,
  type UpdateAgentPersonaDto,
  type AgentPersonaQueryDto,
  type EvaluationRunDto,
  type EvaluationRunDetailDto,
  type EvaluationRunQueryDto,
  type CreateEvaluationRunDto,
  type BatchEvaluationDto,
  type BatchEvaluationResultDto,
  type McpServerRegistrationDto,
  type McpServerRegistrationQueryDto,
  type CreateMcpServerRegistrationDto,
  type UpdateMcpServerRegistrationDto,
  type McpServerTestResultDto,
  type ProviderDto,
  type ProviderQueryDto,
  type CreateProviderDto,
  type UpdateProviderDto,
  type ProviderTestResultDto,
} from '@tnzi/core/services/ai'
import { useAdminKnowledgeBaseApi, useAdminWorkspaceAgentApi } from '@tnzi/core/services/ai'
import type { WorkspaceAgentDto } from '@tnzi/core/services/ai'
// --- Production-grade widening (2026-06-07): surface deeper admin capabilities ---
import { useAdminSkillCategoryApi, useAdminMcpToolAnalyticsApi } from '@tnzi/core/services/ai'
import type {
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
  EvaluationTrendDto,
  VersionComparisonDto,
  BudgetSummaryDto,
  AgentThreadDetailDto,
  ThreadExportDto,
  UsageLogDto,
  UsageLogQueryDto,
  AgentFeedbackStatsDto,
  CostSummaryDto,
  SkillUsageStatsDto,
  PopularSkillDto,
  SkillExportDto,
  SkillImportRequestDto,
  SkillImportResultDto,
  SkillScope,
  SkillCategoryDto,
  CreateSkillCategoryDto,
  UpdateSkillCategoryDto,
  McpServerStatusDto,
  McpToolInfoDto,
  McpToolExposureOptionsDto,
  McpToolStatsDto,
  McpToolPopularityDto,
  McpToolErrorDto,
  ProviderDefaultModelDto,
  ProviderOptionDto,
  ProviderModelsDto,
  KnowledgeBaseDto,
  KnowledgeDocumentDto,
  DocumentUploadResultDto,
  SearchResultDto,
  ReindexResultDto,
  DocumentQueryParams,
  SearchTestParams,
  KnowledgeBaseCreateParams,
  KnowledgeBaseUpdateParams,
} from '@tnzi/core/services/ai'
import { createPagedList } from '@tnzi/core'
import type { PagedList } from '@tnzi/core'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { unwrapResult as unwrap, mapQueryToListRequest as mapQuery } from '../_mappers'

// HttpClient type derived from a factory signature.
type HttpClient = Parameters<typeof useAdminAgentApi>[0]

export interface AiBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly; any not provided fall back to client (or stay null). */
  agentApi?: ReturnType<typeof useAdminAgentApi>
  agentRunApi?: ReturnType<typeof useAdminAgentRunApi>
  threadApi?: ReturnType<typeof useAdminThreadApi>
  workflowApi?: ReturnType<typeof useAdminWorkflowApi>
  skillApi?: ReturnType<typeof useAdminSkillApi>
  providerApi?: ReturnType<typeof useAdminProviderApi>
  usageApi?: ReturnType<typeof useAdminUsageAnalyticsApi>
  knowledgeBaseApi?: ReturnType<typeof useAdminKnowledgeBaseApi>
  mcpApi?: ReturnType<typeof useAdminMcpApi>
  quotaApi?: ReturnType<typeof useAdminQuotaApi>
  personaApi?: ReturnType<typeof useAdminPersonaApi>
  evaluationApi?: ReturnType<typeof useAdminEvaluationApi>
}

// ---- Bridge contract shapes ------------------------------------------------

export interface UsageSummary {
  totalTokens: number
  totalCostUsd: number
  requestCount: number
}

/**
 * Row exposed to UI for the mcpServers sub-contract.
 * Backed by the AI_McpServerRegistration entity table; the DTO never carries
 * plaintext or ciphertext auth tokens — only a `hasAuthToken` boolean flag.
 */
export type McpServerRow = McpServerRegistrationDto

/**
 * Row exposed to UI for the providers sub-contract.
 * Backed by the AI_Provider entity table; ProviderDto from @tnzi/core never carries
 * plaintext or ciphertext API key, only a `hasApiKey` boolean indicator.
 */
export type ProviderRow = ProviderDto

export interface AiBridge {
  agents: BridgeCrudContract<AgentDto, CreateAgentDto, UpdateAgentDto> & {
    /** Fetch a single agent by id. */
    getById(id: string): Promise<AgentDto>
    /** Deep-copy an agent (optionally with a new name). */
    clone(id: string, name?: string): Promise<AgentDto>
    /** Paged version-snapshot history. */
    getVersions(id: string, query?: AgentVersionQueryDto): Promise<PagedList<AgentVersionDto>>
    /** Fetch a single version snapshot. */
    getVersion(id: string, version: number): Promise<AgentVersionDto>
    /** Roll the agent back to a prior version snapshot. */
    rollbackToVersion(id: string, version: number): Promise<AgentDto>
    /** Validate the agent's configuration (provider/model/tools/handoff/skills). */
    validate(id: string): Promise<AgentValidationResultDto>
    /** Cross-agent health summary (healthy/unhealthy counts + details). */
    getHealth(): Promise<AgentHealthSummaryDto>
    /** Configure an A/B test (route a traffic slice to version B). */
    configureAbTest(id: string, data: ConfigureAbTestDto): Promise<AgentDto>
    /** Stop the A/B test on an agent. */
    stopAbTest(id: string): Promise<AgentDto>
    /** Assignable tool-group catalog (for the per-agent Tools picker). */
    getToolGroups(): Promise<ToolGroupDto[]>
    /** Paged list of an agent's admin-curated memory entries. */
    getMemory(id: string, query?: AgentMemoryListQueryDto): Promise<PagedList<AgentMemoryDto>>
    /** Create a memory entry for an agent. */
    createMemory(id: string, data: CreateAgentMemoryDto): Promise<AgentMemoryDto>
    /** Update an agent memory entry. */
    updateMemory(id: string, memoryId: string, data: UpdateAgentMemoryDto): Promise<AgentMemoryDto>
    /** Delete an agent memory entry. */
    deleteMemory(id: string, memoryId: string): Promise<void>
  }
  threads: BridgeCrudContract<AgentThreadDto, CreateAgentThreadDto, Partial<AgentThreadDto>> & {
    /** Thread detail incl. recent messages. */
    getDetail(id: string, messageLimit?: number): Promise<AgentThreadDetailDto>
    /** Export a thread as a JSON payload. */
    exportJson(id: string): Promise<ThreadExportDto>
    /** Resolve the markdown export URL (file download — caller opens it). */
    getExportMarkdownUrl(id: string): string
  }
  agentRuns: Pick<BridgeCrudContract<AgentRunDto>, 'fetch'> & {
    cancel(id: string): Promise<void>
    /** All trace events recorded for a run (drives the run monitor's trace panel via polling). */
    getTraces(id: string): Promise<AgentRunTraceDto[]>
    tail(id: string): Promise<ReadableStream<Uint8Array>>
  }
  workflows: BridgeCrudContract<
    WorkflowDefinitionDto,
    CreateWorkflowDefinitionDto,
    UpdateWorkflowDefinitionDto
  > & {
    /** Fetch a single workflow definition by id. */
    getById(id: string): Promise<WorkflowDefinitionDto>
    /** Deep-copy a workflow definition. */
    clone(id: string): Promise<WorkflowDefinitionDto>
    /** Toggle isEnabled=true. Workflow has no separate Draft/Published — IsEnabled IS the publish flag. */
    publish(id: string): Promise<WorkflowDefinitionDto>
    /** Toggle isEnabled=false. */
    unpublish(id: string): Promise<WorkflowDefinitionDto>
    /** Synchronously execute the workflow once. */
    run(id: string, input: string, userId?: string): Promise<WorkflowExecutionResultDto>
    /** Resolve the SSE stream URL for `run/stream`. Caller opens EventSource. */
    getRunStreamUrl(id: string): string
    /** Pre-run validation (step refs, cycles, agent refs). */
    validate(id: string): Promise<WorkflowValidationResultDto>
    /** Workflow-wide statistics (definition counts + execution counts). */
    getStats(): Promise<WorkflowStatsDto>
    /** Batch enable workflows. */
    batchEnable(ids: string[]): Promise<number>
    /** Batch disable workflows. */
    batchDisable(ids: string[]): Promise<number>
  }
  workflowRuns: Pick<BridgeCrudContract<WorkflowExecutionSummaryDto>, 'fetch'> & {
    tail(id: string): Promise<ReadableStream<Uint8Array>>
    /** Fetch the full execution detail (steps + outputs + pending approvals). */
    getDetail(executionId: string): Promise<WorkflowExecutionDetailDto>
    /** Get current execution status (lighter than getDetail). */
    getStatus(executionId: string): Promise<WorkflowExecutionStatusDto>
    /** Resume a paused execution. */
    resume(executionId: string): Promise<WorkflowExecutionResultDto>
    /** Approve a step waiting on human review. */
    approveStep(executionId: string, stepId: string, comment?: string): Promise<void>
    /** Reject a step (`reason` required by backend). */
    rejectStep(executionId: string, stepId: string, reason: string): Promise<void>
  }
  skills: BridgeCrudContract<SkillSummaryDto, CreateSkillDto, UpdateSkillDto> & {
    activate(id: string): Promise<void>
    deactivate(id: string): Promise<void>
    /** Fetch full skill detail (content + constraints) by slug. */
    getBySlug(slug: string): Promise<SkillDetailDto>
    /** Keyword + semantic-fallback search. */
    search(query: string, maxResults?: number): Promise<SkillSummaryDto[]>
    /** Catalog usage statistics (counts by scope + activations). */
    getUsageStats(): Promise<SkillUsageStatsDto>
    /** Most-activated skills ranking. */
    getPopular(topN?: number): Promise<PopularSkillDto[]>
    /** Export skills (optionally scoped) as a portable JSON array.
     *  Named `exportSkills` (not `export`) to avoid the reserved CSV/Blob
     *  `export?` member on BridgeCrudContract. */
    exportSkills(scope?: SkillScope): Promise<SkillExportDto[]>
    /** Import skills from a portable JSON array.
     *  Named `importSkills` (not `import`) to avoid the reserved file-`import?`
     *  member on BridgeCrudContract. */
    importSkills(data: SkillImportRequestDto): Promise<SkillImportResultDto>
  }
  /** Skill category tree (admin/ai/skill-categories). */
  skillCategories: {
    getTree(): Promise<SkillCategoryDto[]>
    create(data: CreateSkillCategoryDto): Promise<SkillCategoryDto>
    update(id: string, data: UpdateSkillCategoryDto): Promise<SkillCategoryDto>
    delete(id: string): Promise<void>
    getSkills(categoryId: string): Promise<SkillSummaryDto[]>
  }
  providers: BridgeCrudContract<ProviderRow, CreateProviderDto, UpdateProviderDto> & {
    test(id: string): Promise<{ ok: boolean; latency: number; error?: string }>
    /** Names of all enabled providers (config-driven). */
    getProviders(): Promise<string[]>
    /** Default model resolved for a provider name. */
    getDefaultModel(providerName: string): Promise<ProviderDefaultModelDto>
    /** Enabled provider dropdown options (id + name + type + default model). */
    getOptions(): Promise<ProviderOptionDto[]>
    /** Models for a provider entity (live /v1/models + static fallback). */
    listModels(id: string): Promise<ProviderModelsDto>
  }
  usage: {
    summary(query: CrudPageQuery): Promise<UsageSummary>
    byAgent(query: CrudPageQuery): Promise<AgentUsageDto[]>
    byModel(query: CrudPageQuery): Promise<ModelUsageDto[]>
    byDay(query: CrudPageQuery): Promise<UsageTrendPointDto[]>
    /** Paged usage log query (OperationType/provider/model/agent/success filters). */
    getLogs(query: CrudPageQuery): Promise<PagedList<UsageLogDto>>
    /** Usage aggregated by provider. */
    byProvider(query: CrudPageQuery): Promise<ProviderUsageDto[]>
    /** Per-agent feedback (good-rate + tag distribution). */
    feedbackStats(topN?: number): Promise<AgentFeedbackStatsDto[]>
    /** USD cost summary grouped by provider/model. */
    costSummary(query: CrudPageQuery): Promise<CostSummaryDto>
  }
  knowledge: BridgeCrudContract<
    KnowledgeBaseDto,
    KnowledgeBaseCreateParams,
    KnowledgeBaseUpdateParams
  > & {
    /** Fetch a single knowledge base by id. */
    getById(id: string): Promise<KnowledgeBaseDto>
    /** Trigger full re-vectorization; returns chunk/document counts + duration. */
    reindex(id: string): Promise<ReindexResultDto>
    /** Paged document list for a knowledge base. */
    getDocuments(kbId: string, query?: DocumentQueryParams): Promise<PagedList<KnowledgeDocumentDto>>
    /** Poll a single document's ingestion status. */
    getDocumentStatus(kbId: string, docId: string): Promise<KnowledgeDocumentDto>
    /** Upload a document (multipart); ingestion is async — poll status by docId. */
    uploadDocument(kbId: string, file: File): Promise<DocumentUploadResultDto>
    /** Delete a document from a knowledge base. */
    deleteDocument(kbId: string, docId: string): Promise<void>
    /** Search-test a knowledge base (returns ranked chunk hits). */
    searchTest(kbId: string, data: SearchTestParams): Promise<SearchResultDto[]>
  }
  mcpServers: BridgeCrudContract<
    McpServerRow,
    CreateMcpServerRegistrationDto,
    UpdateMcpServerRegistrationDto
  > & {
    test(id: string): Promise<{ ok: boolean; latency: number; error?: string }>
  }
  /** Tnzi's own MCP server (status + tool list + agent exposure). */
  mcp: {
    getStatus(): Promise<McpServerStatusDto>
    getTools(): Promise<McpToolInfoDto[]>
    getExposedAgents(): Promise<string[]>
    exposeAgent(agentId: string, options?: McpToolExposureOptionsDto): Promise<void>
    removeAgent(agentId: string): Promise<void>
  }
  /** MCP tool-call analytics (admin/ai/mcp/tool-analytics). */
  mcpToolAnalytics: {
    getToolStats(toolName: string, from?: string, to?: string): Promise<McpToolStatsDto>
    getMostUsedTools(top?: number, from?: string, to?: string): Promise<McpToolPopularityDto[]>
    getToolErrors(toolName: string, top?: number): Promise<McpToolErrorDto[]>
    cleanup(retentionDays?: number): Promise<number>
  }
  quota: BridgeCrudContract<UserQuotaDto, SetQuotaDto, SetQuotaDto> & {
    /** USD cost budget summary for a tenant/time-range. */
    getBudgetSummary(params?: { tenantId?: string; startTime?: string; endTime?: string }): Promise<BudgetSummaryDto>
  }
  personas: BridgeCrudContract<AgentPersonaDto, CreateAgentPersonaDto, UpdateAgentPersonaDto>
  evaluations: BridgeCrudContract<EvaluationRunDto, CreateEvaluationRunDto, Partial<EvaluationRunDto>> & {
    run(id: string): Promise<{ runId: string }>
    runBatch(data: BatchEvaluationDto): Promise<BatchEvaluationResultDto>
    /** Fetch an EvaluationRun with its full resultsJson (drives the diff drawer). */
    getDetail(id: string): Promise<EvaluationRunDetailDto>
    /** An agent's evaluation score trend over its last N runs. */
    getTrend(agentId: string, lastNRuns?: number): Promise<EvaluationTrendDto>
    /** Side-by-side comparison of two agent versions' evaluation stats. */
    compareVersions(agentId: string, versionA: number, versionB: number): Promise<VersionComparisonDto>
  }
}


// 0.2.72+ (C4): `CrudPageResult<T>` is now an alias for `@tnzi/core`'s
// `PagedList<T>`, so the legacy 4-field projection collapses to identity.
// Kept as an inline function for the existing call sites + so future
// `PagedList`-shape drift can be caught here.
function toCrudResult<T>(p: PagedList<T>): CrudPageResult<T> {
  return p
}

const notImplemented = (op: string) => () =>
  Promise.reject(new Error(`ai-bridge: ${op} is not implemented — backend endpoint not available`))

// ---------------------------------------------------------------------------

export function createAiBridge(deps: AiBridgeDeps = {}): AiBridge {
  const agentApi = deps.agentApi ?? (deps.client ? useAdminAgentApi(deps.client) : null)
  const agentRunApi = deps.agentRunApi ?? (deps.client ? useAdminAgentRunApi(deps.client) : null)
  const threadApi = deps.threadApi ?? (deps.client ? useAdminThreadApi(deps.client) : null)
  const workflowApi = deps.workflowApi ?? (deps.client ? useAdminWorkflowApi(deps.client) : null)
  const skillApi = deps.skillApi ?? (deps.client ? useAdminSkillApi(deps.client) : null)
  const providerApi = deps.providerApi ?? (deps.client ? useAdminProviderApi(deps.client) : null)
  const usageApi = deps.usageApi ?? (deps.client ? useAdminUsageAnalyticsApi(deps.client) : null)
  const kbApi =
    deps.knowledgeBaseApi ?? (deps.client ? useAdminKnowledgeBaseApi(deps.client) : null)
  const mcpApi = deps.mcpApi ?? (deps.client ? useAdminMcpApi(deps.client) : null)
  const quotaApi = deps.quotaApi ?? (deps.client ? useAdminQuotaApi(deps.client) : null)
  const personaApi = deps.personaApi ?? (deps.client ? useAdminPersonaApi(deps.client) : null)
  const evaluationApi =
    deps.evaluationApi ?? (deps.client ? useAdminEvaluationApi(deps.client) : null)
  // New capability factories (client-derived; production always passes `client`).
  const skillCategoryApi = deps.client ? useAdminSkillCategoryApi(deps.client) : null
  const mcpAnalyticsApi = deps.client ? useAdminMcpToolAnalyticsApi(deps.client) : null

  // Construction-time guard — fail fast instead of deferring errors to first async call.
  // Matches identity-bridge pattern. Production callers pass `client`; tests may inject
  // individual api mocks, but must cover every sub-contract they intend to exercise.
  if (
    !agentApi ||
    !agentRunApi ||
    !threadApi ||
    !workflowApi ||
    !skillApi ||
    !providerApi ||
    !usageApi ||
    !kbApi ||
    !mcpApi ||
    !quotaApi ||
    !personaApi ||
    !evaluationApi
  ) {
    throw new Error(
      'createAiBridge: provide either `client` (HttpClient) or all 12 api deps ' +
        '(agentApi/agentRunApi/threadApi/workflowApi/skillApi/providerApi/usageApi/' +
        'knowledgeBaseApi/mcpApi/quotaApi/personaApi/evaluationApi)',
    )
  }

  // ---- agents -------------------------------------------------------------
  const agents: AiBridge['agents'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<AgentDto>>(await agentApi.getList(mapQuery(q) as unknown as AgentListQueryDto)),
      ),
    create: async (data) => unwrap<AgentDto>(await agentApi.create(data)),
    update: async (id, data) => unwrap<AgentDto>(await agentApi.update(String(id), data)),
    delete: async (ids) => {
      for (const id of ids) await agentApi.delete(String(id))
    },
    getById: async (id) => unwrap<AgentDto>(await agentApi.getById(String(id))),
    clone: async (id, name) => unwrap<AgentDto>(await agentApi.clone(String(id), name)),
    getVersions: async (id, query) =>
      unwrap<PagedList<AgentVersionDto>>(await agentApi.getVersions(String(id), query)),
    getVersion: async (id, version) =>
      unwrap<AgentVersionDto>(await agentApi.getVersion(String(id), version)),
    rollbackToVersion: async (id, version) =>
      unwrap<AgentDto>(await agentApi.rollbackToVersion(String(id), version)),
    validate: async (id) => unwrap<AgentValidationResultDto>(await agentApi.validate(String(id))),
    getHealth: async () => unwrap<AgentHealthSummaryDto>(await agentApi.getHealth()),
    configureAbTest: async (id, data) =>
      unwrap<AgentDto>(await agentApi.configureAbTest(String(id), data)),
    stopAbTest: async (id) => unwrap<AgentDto>(await agentApi.stopAbTest(String(id))),
    getToolGroups: async () => unwrap<ToolGroupDto[]>(await agentApi.getToolGroups()),
    getMemory: async (id, query) =>
      unwrap<PagedList<AgentMemoryDto>>(await agentApi.getMemory(String(id), query)),
    createMemory: async (id, data) =>
      unwrap<AgentMemoryDto>(await agentApi.createMemory(String(id), data)),
    updateMemory: async (id, memoryId, data) =>
      unwrap<AgentMemoryDto>(await agentApi.updateMemory(String(id), String(memoryId), data)),
    deleteMemory: async (id, memoryId) => {
      await agentApi.deleteMemory(String(id), String(memoryId))
    },
  }

  // ---- threads ------------------------------------------------------------
  const threads: AiBridge['threads'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<AgentThreadDto>>(await threadApi.getList(
          mapQuery(q) as unknown as ThreadListQueryDto,
        )),
      ),
    create: async (data) => unwrap<AgentThreadDto>(await threadApi.create(data)),
    update: async (id, data) =>
      unwrap<AgentThreadDto>(await threadApi.updateTitle(String(id), {
        title: (data as { title?: string }).title ?? '',
      })),
    delete: async (ids) => {
      for (const id of ids) await threadApi.delete(String(id))
    },
    getDetail: async (id, messageLimit) =>
      unwrap<AgentThreadDetailDto>(await threadApi.getDetail(String(id), messageLimit)),
    exportJson: async (id) => unwrap<ThreadExportDto>(await threadApi.exportJson(String(id))),
    getExportMarkdownUrl: (id) => threadApi.getExportMarkdownUrl(String(id)),
  }

  // ---- agentRuns ----------------------------------------------------------
  const agentRuns = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<AgentRunDto>> =>
      toCrudResult(
        unwrap<PagedList<AgentRunDto>>(await agentRunApi.getList(
          mapQuery(q) as unknown as AgentRunQueryDto,
        )),
      ),
    cancel: async (id: string) => {
      await agentRunApi.cancel(id)
    },
    getTraces: async (id: string) => {
      const items = unwrap<AgentRunTraceDto[] | undefined>(await agentRunApi.getRunTraces(id))
      return Array.isArray(items) ? items : []
    },
    tail: notImplemented('agentRuns.tail') as () => Promise<ReadableStream<Uint8Array>>,
  }

  // ---- workflows ----------------------------------------------------------
  const workflows: AiBridge['workflows'] = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<WorkflowDefinitionDto>> =>
      toCrudResult(
        unwrap<PagedList<WorkflowDefinitionDto>>(await workflowApi.getList(
          mapQuery(q) as unknown as WorkflowDefinitionQueryDto,
        )),
      ),
    create: async (data: CreateWorkflowDefinitionDto) =>
      unwrap<WorkflowDefinitionDto>(await workflowApi.create(data)),
    update: async (id: string, data: UpdateWorkflowDefinitionDto) =>
      unwrap<WorkflowDefinitionDto>(await workflowApi.update(String(id), data)),
    delete: async (ids: string[]) => {
      await workflowApi.batchDelete(ids.map(String))
    },
    getById: async (id: string) =>
      unwrap<WorkflowDefinitionDto>(await workflowApi.getById(String(id))),
    clone: async (id: string) =>
      unwrap<WorkflowDefinitionDto>(await workflowApi.clone(String(id))),
    // batchEnable is the current publish semantic — see docs/modules/ai.md Workflow section.
    // batchEnable returns affected-row count; we re-fetch the definition so the contract
    // (Promise<WorkflowDefinitionDto>) is honoured for callers that want the updated entity.
    publish: async (id: string): Promise<WorkflowDefinitionDto> => {
      await workflowApi.batchEnable([String(id)])
      return unwrap<WorkflowDefinitionDto>(await workflowApi.getById(String(id)))
    },
    unpublish: async (id: string): Promise<WorkflowDefinitionDto> => {
      await workflowApi.batchDisable([String(id)])
      return unwrap<WorkflowDefinitionDto>(await workflowApi.getById(String(id)))
    },
    run: async (id: string, input: string, userId?: string) => {
      const body: RunWorkflowRequestDto = { input, userId: userId ?? null }
      return unwrap<WorkflowExecutionResultDto>(await workflowApi.run(String(id), body))
    },
    getRunStreamUrl: (id: string) => workflowApi.getRunStreamUrl(String(id)),
    validate: async (id: string) =>
      unwrap<WorkflowValidationResultDto>(await workflowApi.validate(String(id))),
    getStats: async () =>
      unwrap<WorkflowStatsDto>(await workflowApi.getStats()),
    batchEnable: async (ids: string[]) =>
      unwrap<number>(await workflowApi.batchEnable(ids.map(String))),
    batchDisable: async (ids: string[]) =>
      unwrap<number>(await workflowApi.batchDisable(ids.map(String))),
  }

  // ---- workflowRuns -------------------------------------------------------
  const workflowRuns: AiBridge['workflowRuns'] = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<WorkflowExecutionSummaryDto>> => {
      const params = {
        pageIndex: q.pageIndex,
        pageSize: q.pageSize,
        ...(q.filters ?? {}),
      }
      return toCrudResult(
        unwrap<PagedList<WorkflowExecutionSummaryDto>>(await workflowApi.getExecutions(
          params as unknown as never,
        )),
      )
    },
    tail: notImplemented('workflowRuns.tail') as () => Promise<ReadableStream<Uint8Array>>,
    getDetail: async (executionId: string): Promise<WorkflowExecutionDetailDto> =>
      unwrap<WorkflowExecutionDetailDto>(await workflowApi.getExecutionDetail(executionId)),
    getStatus: async (executionId: string) =>
      unwrap<WorkflowExecutionStatusDto>(await workflowApi.getExecutionStatus(executionId)),
    resume: async (executionId: string) =>
      unwrap<WorkflowExecutionResultDto>(await workflowApi.resumeExecution(executionId)),
    approveStep: async (executionId: string, stepId: string, comment?: string) => {
      const body: WorkflowStepApprovalDto | undefined = comment ? { feedback: comment } : undefined
      await workflowApi.approveStep(executionId, stepId, body)
    },
    rejectStep: async (executionId: string, stepId: string, reason: string) => {
      const body: WorkflowStepApprovalDto = { feedback: reason }
      await workflowApi.rejectStep(executionId, stepId, body)
    },
  }

  // ---- skills -------------------------------------------------------------
  const skills = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<SkillSummaryDto>> =>
      toCrudResult(
        unwrap<PagedList<SkillSummaryDto>>(await skillApi.getPaged(
          // Admin list always merges DB + file-source skills. Backend hides
          // file-source rows behind this opt-in flag because they're not
          // editable; the admin page distinguishes them via `isReadOnly` +
          // `source` columns instead.
          { ...mapQuery(q), includeFileSource: true } as unknown as SkillQueryDto,
        )),
      ),
    create: async (data: CreateSkillDto) =>
      unwrap<SkillSummaryDto>(await skillApi.create(data)),
    update: async (id: string, data: UpdateSkillDto) =>
      unwrap<SkillSummaryDto>(await skillApi.update(String(id), data)),
    delete: async (ids: string[]) => {
      await skillApi.batchDelete(ids.map(String))
    },
    activate: async (id: string) => {
      // Admin "activate" maps to batchEnable on a single id.
      await skillApi.batchEnable([String(id)])
    },
    deactivate: async (id: string) => {
      await skillApi.batchDisable([String(id)])
    },
    getBySlug: async (slug: string) => unwrap<SkillDetailDto>(await skillApi.getBySlug(slug)),
    search: async (query: string, maxResults?: number) => {
      const items = unwrap<SkillSummaryDto[] | undefined>(await skillApi.search(query, maxResults))
      return Array.isArray(items) ? items : []
    },
    getUsageStats: async () => unwrap<SkillUsageStatsDto>(await skillApi.getUsageStats()),
    getPopular: async (topN?: number) => {
      const items = unwrap<PopularSkillDto[] | undefined>(await skillApi.getPopular(topN))
      return Array.isArray(items) ? items : []
    },
    exportSkills: async (scope?: SkillScope) => {
      const items = unwrap<SkillExportDto[] | undefined>(await skillApi.export(scope))
      return Array.isArray(items) ? items : []
    },
    importSkills: async (data: SkillImportRequestDto) =>
      unwrap<SkillImportResultDto>(await skillApi.import(data)),
  }

  // ---- skill categories ---------------------------------------------------
  const skillCategories: AiBridge['skillCategories'] = {
    getTree: async () => {
      const items = unwrap<SkillCategoryDto[] | undefined>(await skillCategoryApi!.getTree())
      return Array.isArray(items) ? items : []
    },
    create: async (data) => unwrap<SkillCategoryDto>(await skillCategoryApi!.create(data)),
    update: async (id, data) =>
      unwrap<SkillCategoryDto>(await skillCategoryApi!.update(String(id), data)),
    delete: async (id) => {
      await skillCategoryApi!.delete(String(id))
    },
    getSkills: async (categoryId) => {
      const items = unwrap<SkillSummaryDto[] | undefined>(
        await skillCategoryApi!.getSkillsByCategory(String(categoryId)),
      )
      return Array.isArray(items) ? items : []
    },
  }

  // ---- providers ----------------------------------------------------------
  // Entity-driven CRUD against POST /admin/ai/providers/query and entities/* sub-routes.
  // The bridge maps the backend ProviderTestResultDto ({success,message,latencyMs}) to the
  // UI-friendly { ok, latency, error? } shape that page consumers expect.
  const providers = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<ProviderRow>> =>
      toCrudResult(
        unwrap<PagedList<ProviderDto>>(
          await providerApi.getList(mapQuery(q) as unknown as ProviderQueryDto),
        ),
      ),
    create: async (data: CreateProviderDto): Promise<ProviderDto> =>
      unwrap<ProviderDto>(await providerApi.create(data)),
    update: async (id: string, data: UpdateProviderDto): Promise<ProviderDto> =>
      unwrap<ProviderDto>(await providerApi.update(String(id), data)),
    delete: async (ids: string[]): Promise<void> => {
      for (const id of ids) await providerApi.delete(String(id))
    },
    test: async (id: string): Promise<{ ok: boolean; latency: number; error?: string }> => {
      const result = unwrap<ProviderTestResultDto>(await providerApi.test(String(id)))
      return {
        ok: result.success,
        latency: result.latencyMs,
        error: result.success ? undefined : result.message ?? undefined,
      }
    },
    getProviders: async () => {
      const items = unwrap<string[] | undefined>(await providerApi.getProviders())
      return Array.isArray(items) ? items : []
    },
    getDefaultModel: async (providerName: string) =>
      unwrap<ProviderDefaultModelDto>(await providerApi.getDefaultModel(providerName)),
    getOptions: async () => unwrap<ProviderOptionDto[]>(await providerApi.getOptions()),
    listModels: async (id: string) => unwrap<ProviderModelsDto>(await providerApi.listModels(String(id))),
  }

  // ---- usage --------------------------------------------------------------
  const usage = {
    summary: async (q: CrudPageQuery): Promise<UsageSummary> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const summary = unwrap<UsageSummaryDto | undefined>(await usageApi.getSummary({
        startTime: String(filters.startTime ?? ''),
        endTime: String(filters.endTime ?? ''),
        provider: filters.provider as string | undefined,
        model: filters.model as string | undefined,
      } as unknown as never))
      return {
        totalTokens: summary?.totalTokens ?? 0,
        totalCostUsd: summary?.totalEstimatedCostUsd ?? 0,
        requestCount: summary?.totalRequests ?? 0,
      }
    },
    byAgent: async (q: CrudPageQuery): Promise<AgentUsageDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const items = unwrap<AgentUsageDto[] | undefined>(await usageApi.getByAgent(
        String(filters.startTime ?? ''),
        String(filters.endTime ?? ''),
        (filters.topN as number | undefined) ?? 20,
      ))
      return Array.isArray(items) ? items : []
    },
    byModel: async (q: CrudPageQuery): Promise<ModelUsageDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const items = unwrap<ModelUsageDto[] | undefined>(await usageApi.getByModel(
        String(filters.startTime ?? ''),
        String(filters.endTime ?? ''),
        filters.provider as string | undefined,
      ))
      return Array.isArray(items) ? items : []
    },
    byDay: async (q: CrudPageQuery): Promise<UsageTrendPointDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      // Backend enum is UsageGranularity { Daily=0, Weekly=1, Monthly=2 } — use
      // the canonical enum reference rather than a string literal so refactors
      // on the enum are caught at type-check time. Also coalesce empty
      // start/end times so an unconfigured dashboard probe doesn't bounce on
      // DateTime binding before the user has picked a range.
      const start = String(filters.startTime ?? '')
      const end = String(filters.endTime ?? '')
      if (!start || !end) return []
      const items = unwrap<UsageTrendPointDto[] | undefined>(await usageApi.getTrend({
        startTime: start,
        endTime: end,
        granularity: UsageGranularity.Daily,
      }))
      return Array.isArray(items) ? items : []
    },
    getLogs: async (q: CrudPageQuery): Promise<PagedList<UsageLogDto>> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      return unwrap<PagedList<UsageLogDto>>(await usageApi.getLogs({
        pageIndex: q.pageIndex ?? 1,
        pageSize: q.pageSize ?? 20,
        startTime: (filters.startTime as string | undefined) ?? undefined,
        endTime: (filters.endTime as string | undefined) ?? undefined,
        provider: filters.provider as string | undefined,
        model: filters.model as string | undefined,
        operationType: filters.operationType as string | undefined,
        isSuccess: filters.isSuccess as boolean | undefined,
        agentId: filters.agentId as string | undefined,
      } as unknown as UsageLogQueryDto))
    },
    byProvider: async (q: CrudPageQuery): Promise<ProviderUsageDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const items = unwrap<ProviderUsageDto[] | undefined>(await usageApi.getByProvider(
        String(filters.startTime ?? ''),
        String(filters.endTime ?? ''),
      ))
      return Array.isArray(items) ? items : []
    },
    feedbackStats: async (topN?: number): Promise<AgentFeedbackStatsDto[]> => {
      const items = unwrap<AgentFeedbackStatsDto[] | undefined>(await usageApi.getAgentFeedbackStats(topN))
      return Array.isArray(items) ? items : []
    },
    costSummary: async (q: CrudPageQuery): Promise<CostSummaryDto> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      return unwrap<CostSummaryDto>(await usageApi.getCostSummary(
        String(filters.startTime ?? ''),
        String(filters.endTime ?? ''),
        filters.provider as string | undefined,
        filters.model as string | undefined,
      ))
    },
  }

  // ---- knowledge ----------------------------------------------------------
  const knowledge: AiBridge['knowledge'] = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<KnowledgeBaseDto>> => {
      const result = unwrap<unknown>(await kbApi.getList({
        pageIndex: q.pageIndex ?? 1,
        pageSize: q.pageSize ?? 20,
        keyword: q.searchText || undefined,
      }))
      // Backend may return either a plain array or PagedList<KbDto> — handle both.
      if (Array.isArray(result)) {
        return createPagedList(
          result as KnowledgeBaseDto[],
          result.length,
          q.pageIndex ?? 1,
          q.pageSize ?? 20,
        )
      }
      const paged = result as PagedList<KnowledgeBaseDto>
      return toCrudResult(
        paged ?? {
          items: [],
          totalCount: 0,
          pageIndex: q.pageIndex ?? 1,
          pageSize: q.pageSize ?? 20,
          totalPages: 0,
          hasNextPage: false,
          hasPreviousPage: false,
        },
      )
    },
    create: async (data) => unwrap<KnowledgeBaseDto>(await kbApi.create(data)),
    update: async (id, data) => unwrap<KnowledgeBaseDto>(await kbApi.update(String(id), data)),
    delete: async (ids) => {
      for (const id of ids) await kbApi.delete(String(id))
    },
    getById: async (id) => unwrap<KnowledgeBaseDto>(await kbApi.getById(String(id))),
    reindex: async (id) => unwrap<ReindexResultDto>(await kbApi.reindex(String(id))),
    getDocuments: async (kbId, query) =>
      unwrap<PagedList<KnowledgeDocumentDto>>(await kbApi.getDocuments(String(kbId), query)),
    getDocumentStatus: async (kbId, docId) =>
      unwrap<KnowledgeDocumentDto>(await kbApi.getDocumentStatus(String(kbId), String(docId))),
    uploadDocument: async (kbId, file) =>
      unwrap<DocumentUploadResultDto>(await kbApi.uploadDocument(String(kbId), file)),
    deleteDocument: async (kbId, docId) => {
      await kbApi.deleteDocument(String(kbId), String(docId))
    },
    searchTest: async (kbId, data) => {
      const items = unwrap<SearchResultDto[] | undefined>(await kbApi.searchTest(String(kbId), data))
      return Array.isArray(items) ? items : []
    },
  }

  // ---- mcpServers ---------------------------------------------------------
  // Phase 5 backend prereq: entity-driven CRUD for external MCP server client
  // registrations. fetch/create/update/delete/test all wire to real backend
  // endpoints under /admin/mcp/client/servers/*.
  const mcpServers = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<McpServerRow>> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const query: McpServerRegistrationQueryDto = {
        pageIndex: q.pageIndex ?? 1,
        pageSize: q.pageSize ?? 20,
        skip: ((q.pageIndex ?? 1) - 1) * (q.pageSize ?? 20),
        take: q.pageSize ?? 20,
        keyword: q.searchText || undefined,
        transport: (filters.transport as string | undefined) ?? null,
        isEnabled: (filters.isEnabled as boolean | undefined) ?? null,
      }
      return toCrudResult(unwrap<PagedList<McpServerRegistrationDto>>(await mcpApi.getList(query)))
    },
    create: async (data: CreateMcpServerRegistrationDto): Promise<McpServerRow> =>
      unwrap<McpServerRegistrationDto>(await mcpApi.create(data)),
    update: async (id: string, data: UpdateMcpServerRegistrationDto): Promise<McpServerRow> =>
      unwrap<McpServerRegistrationDto>(await mcpApi.update(String(id), data)),
    delete: async (ids: string[]): Promise<void> => {
      for (const id of ids) await mcpApi.delete(String(id))
    },
    test: async (id: string): Promise<{ ok: boolean; latency: number; error?: string }> => {
      const result = unwrap<McpServerTestResultDto>(await mcpApi.test(String(id)))
      return {
        ok: result.success,
        latency: result.latencyMs,
        error: result.success ? undefined : result.message ?? undefined,
      }
    },
  }

  // ---- mcp (Tnzi's own MCP server) ---------------------------------------
  const mcp: AiBridge['mcp'] = {
    getStatus: async () => unwrap<McpServerStatusDto>(await mcpApi.getStatus()),
    getTools: async () => {
      const items = unwrap<McpToolInfoDto[] | undefined>(await mcpApi.getTools())
      return Array.isArray(items) ? items : []
    },
    getExposedAgents: async () => {
      const items = unwrap<string[] | undefined>(await mcpApi.getExposedAgents())
      return Array.isArray(items) ? items : []
    },
    exposeAgent: async (agentId, options) => {
      await mcpApi.exposeAgent(String(agentId), options)
    },
    removeAgent: async (agentId) => {
      await mcpApi.removeAgent(String(agentId))
    },
  }

  // ---- mcp tool analytics -------------------------------------------------
  const mcpToolAnalytics: AiBridge['mcpToolAnalytics'] = {
    getToolStats: async (toolName, from, to) =>
      unwrap<McpToolStatsDto>(await mcpAnalyticsApi!.getToolStats(toolName, from, to)),
    getMostUsedTools: async (top, from, to) => {
      const items = unwrap<McpToolPopularityDto[] | undefined>(
        await mcpAnalyticsApi!.getMostUsedTools(top, from, to),
      )
      return Array.isArray(items) ? items : []
    },
    getToolErrors: async (toolName, top) => {
      const items = unwrap<McpToolErrorDto[] | undefined>(
        await mcpAnalyticsApi!.getToolErrors(toolName, top),
      )
      return Array.isArray(items) ? items : []
    },
    cleanup: async (retentionDays) => unwrap<number>(await mcpAnalyticsApi!.cleanup(retentionDays)),
  }

  // ---- quota --------------------------------------------------------------
  const quota: AiBridge['quota'] = {
    // Backend exposes a paged query at POST /admin/quotas/query (Phase 5 prereq).
    fetch: async (q): Promise<CrudPageResult<UserQuotaDto>> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const pageIndex = q.pageIndex ?? 1
      const pageSize = q.pageSize ?? 20
      const query: UserQuotaQueryDto = {
        pageIndex,
        pageSize,
        skip: (pageIndex - 1) * pageSize,
        take: pageSize,
        userId: (filters.userId as string | undefined) ?? null,
        isEnabled: (filters.isEnabled as boolean | undefined) ?? null,
      }
      return toCrudResult(unwrap<PagedList<UserQuotaDto>>(await quotaApi.getList(query)))
    },
    create: async (data) => unwrap<UserQuotaDto>(await quotaApi.setQuota(data)),
    update: async (_id, data) => unwrap<UserQuotaDto>(await quotaApi.setQuota(data)),
    delete: notImplemented('quota.delete') as (ids: string[]) => Promise<void>,
    getBudgetSummary: async (params) =>
      unwrap<BudgetSummaryDto>(await quotaApi.getBudgetSummary(params)),
  }

  // ---- personas -----------------------------------------------------------
  const personas: BridgeCrudContract<
    AgentPersonaDto,
    CreateAgentPersonaDto,
    UpdateAgentPersonaDto
  > = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<AgentPersonaDto>>(await personaApi.getList(
          mapQuery(q) as unknown as AgentPersonaQueryDto,
        )),
      ),
    create: async (data) => unwrap<AgentPersonaDto>(await personaApi.create(data)),
    update: async (id, data) => unwrap<AgentPersonaDto>(await personaApi.update(String(id), data)),
    delete: async (ids) => {
      for (const id of ids) await personaApi.delete(String(id))
    },
  }

  // ---- evaluations --------------------------------------------------------
  const evaluations = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<EvaluationRunDto>> =>
      toCrudResult(
        unwrap<PagedList<EvaluationRunDto>>(await evaluationApi.getList(
          mapQuery(q) as unknown as EvaluationRunQueryDto,
        )),
      ),
    // Backend's "create" is create-and-run in one shot (POST /admin/ai/evaluations/run).
    create: async (data: CreateEvaluationRunDto): Promise<EvaluationRunDto> =>
      unwrap<EvaluationRunDetailDto>(await evaluationApi.create(data)),
    update: notImplemented('evaluations.update') as (
      id: string,
      data: Partial<EvaluationRunDto>,
    ) => Promise<EvaluationRunDto>,
    delete: async (ids: string[]) => {
      for (const id of ids) await evaluationApi.delete(String(id))
    },
    // run(id) of an existing evaluation is not supported — backend creates+runs in one
    // call. Use create() to spawn a fresh run, or runBatch() for multi-target runs.
    run: (_id: string): Promise<{ runId: string }> =>
      Promise.reject(
        new Error(
          'evaluations.run(id) is not supported — backend creates and runs in one call. ' +
            'Use evaluations.create(dto) instead.',
        ),
      ),
    runBatch: async (data: BatchEvaluationDto): Promise<BatchEvaluationResultDto> =>
      unwrap<BatchEvaluationResultDto>(await evaluationApi.runBatch(data)),
    getDetail: async (id: string): Promise<EvaluationRunDetailDto> =>
      unwrap<EvaluationRunDetailDto>(await evaluationApi.getById(id)),
    getTrend: async (agentId: string, lastNRuns?: number) =>
      unwrap<EvaluationTrendDto>(await evaluationApi.getTrend(agentId, lastNRuns)),
    compareVersions: async (agentId: string, versionA: number, versionB: number) =>
      unwrap<VersionComparisonDto>(await evaluationApi.compareVersions(agentId, versionA, versionB)),
  }

  return {
    agents,
    threads,
    agentRuns,
    workflows,
    workflowRuns,
    skills,
    skillCategories,
    providers,
    usage,
    knowledge,
    mcpServers,
    mcp,
    mcpToolAnalytics,
    quota,
    personas,
    evaluations,
  }
}

// 0.2.72+ (B4): re-export AI enums + types so pages can consume runtime
// values via the bridge surface and stay clean under the
// `no-restricted-imports` guard against `@tnzi/core/services/*` value
// imports from `pages/**`.
export { WorkflowExecutionMode } from '@tnzi/core/services/ai'

/**
 * Workspace-agent helper — used by the read-only `WorkspaceAgents`
 * page. We expose a minimal `list()` so the page no longer needs to
 * import `useAdminWorkspaceAgentApi` directly. Lives outside the main
 * bridge contract because workspace agents are discovered from YAML
 * files on disk, not the DB, so the full CRUD shape doesn't apply.
 */
export interface WorkspaceAgentsBridge {
  list(): Promise<WorkspaceAgentDto[]>
}

export interface WorkspaceAgentsBridgeDeps {
  client?: HttpClient
}

export function createWorkspaceAgentsBridge(
  deps: WorkspaceAgentsBridgeDeps,
): WorkspaceAgentsBridge {
  const api = deps.client ? useAdminWorkspaceAgentApi(deps.client) : null
  return {
    async list() {
      if (!api) return []
      const resp = await api.listAgents()
      return resp?.data ?? []
    },
  }
}
