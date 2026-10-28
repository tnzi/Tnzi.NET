/**
 * AI bridge - full fill (Phase 5 Task 5.1).
 *
 * Adapts the @tnzi/core AI admin API factories to the BridgeCrudContract shape
 * used by all TCrudPage-based management pages.
 *
 * IMPORTANT - plan-vs-reality drift (vs 2026-04-12 plan §5.1):
 *   The original plan assumed 13 service singletons (`AgentService.default`, etc.)
 *   each with a uniform `getPagedList/create/update/deleteMany` shape. The actual
 *   `@tnzi/core/services/ai` exports 19 factory functions (`useAdmin*Api(client)`)
 *   and the surface differs per resource. This bridge keeps the 13-sub-contract
 *   shape consumers expect, but resolves drift as follows:
 *
 *   1. agents          → useAdminAgentApi.getList/create/update/delete (loop for many)
 *   2. threads         → useAdminThreadApi.getList/create/updateTitle/delete (no plain update)
 *   3. agentRuns       → useAdminAgentRunApi.getList/getById/getRunTraces/cancel
 *   4. workflows       → useAdminWorkflowApi.getList/create/update/delete + clone + validate
 *                        + publish (publish wraps batchEnable - IsEnabled IS the framework
 *                        publish semantic; see docs/modules/ai.md Workflow section)
 *                        + versions (getVersions/getVersion/restoreVersion) + getExecutionStats
 *   5. workflowRuns    → useAdminWorkflowApi.getExecutions/getExecutionDetail (NO separate
 *                        useAdminWorkflowRunApi exists in core) + cancel / getInterrupt /
 *                        resumeWithInput / getSignals, all keyed by the execution's business
 *                        `executionId` (never the row `id`)
 *   6. skills          → useAdminSkillApi.getPaged/create/update/delete + batchEnable/Disable
 *                        re-exposed as activate(id)/deactivate(id) for plan compatibility
 *   7. providers       → ENTITY-DRIVEN CRUD: useAdminProviderApi.{getList,create,update,delete,test}
 *                        wired against POST /admin/ai/providers/query and entities/* sub-routes.
 *                        Backend stores providers in the AI_Provider table with API keys encrypted
 *                        via IDataProtectionProvider. The legacy config-driven {getProviders,
 *                        getDefaultModel} endpoints are still exposed for backwards compatibility
 *                        but are NOT used by the bridge.
 *   8. usage           → useAdminUsageAnalyticsApi.{getSummary,getByAgent,getByModel,getTrend}
 * - query.filters must supply {startTime,endTime}
 *   9. knowledge       → useAdminKnowledgeBaseApi.{getList,create,update,delete,reindex}
 *                        reindex wired to POST /admin/knowledge-bases/{id}/reindex (Phase 5 prereq)
 *  10. mcpServers      → ENTITY-DRIVEN CRUD (Phase 5 backend prereq):
 *                        useAdminMcpApi.{getList,getById,create,update,delete,test} wired against
 *                        POST /admin/mcp/client/servers/query and servers/* sub-routes. Backend stores
 *                        external MCP server registrations in the AI_McpServerRegistration table
 *                        with auth tokens encrypted via IDataProtectionProvider. Legacy hosting
 *                        endpoints (status, tools, agent exposure) remain on the same factory but
 *                        are unrelated to this CRUD surface.
 *  11. usageLimits     → useAdminUsageLimitApi: everyone's defaults (get / save), one user's
 *                        own settings (get / save / clear / reset windows), the metered-user
 *                        list (POST /admin/ai/usage-limits/users/query) and the budget summary
 *  12. evaluations     → useAdminEvaluationApi.getList/getById/delete + create (POST /run) +
 *                        runBatch (POST /batch); no update (the backend creates and runs
 *                        in one call)
 *
 *   An operation the backend does not serve is left out of the contract (evaluations
 *   has no update) rather than shipped as a member that always rejects.
 */
import {
  useAdminAgentApi,
  useAdminAgentRunApi,
  useAdminThreadApi,
  useAdminWorkflowApi,
  useAdminSkillApi,
  useAdminProviderApi,
  useAdminUsageAnalyticsApi,
  useAdminUsageLimitApi,
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
  type WorkflowValidationResultDto,
  type WorkflowStepApprovalDto,
  type RunWorkflowRequestDto,
  type WorkflowDefinitionVersionDto,
  type WorkflowExecutionStatsDto,
  type WorkflowInterruptDto,
  type WorkflowExecutionSignalDto,
  type SkillSummaryDto,
  type SkillDetailDto,
  type CreateSkillDto,
  type UpdateSkillDto,
  type SkillQueryDto,
  type UsageSummaryDto,
  type UsageSummaryQueryDto,
  type ModelUsageDto,
  type AgentUsageDto,
  type UsageTrendPointDto,
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
  McpServerStatusDto,
  McpToolInfoDto,
  McpToolExposureOptionsDto,
  McpToolStatsDto,
  McpToolPopularityDto,
  McpToolErrorDto,
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
import { ensureOk, unwrapResult as unwrap, mapQueryToListRequest as mapQuery, unwrapOk } from '../_mappers'
import { createUsageLimitBridge, type UsageLimitBridge } from './usage-limit-bridge'

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
  usageLimitApi?: ReturnType<typeof useAdminUsageLimitApi>
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
 * plaintext or ciphertext auth tokens - only a `hasAuthToken` boolean flag.
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
    /**
     * Download a thread as a Markdown file (the caller saves the Blob). Throws with
     * the server's reason when the export is refused.
     */
    exportMarkdown(id: string): Promise<Blob>
  }
  agentRuns: Pick<BridgeCrudContract<AgentRunDto>, 'fetch'> & {
    /** One run by id (a deep-linked run need not be on the list's first page). */
    getById(id: string): Promise<AgentRunDto>
    cancel(id: string): Promise<void>
    /** All trace events recorded for a run (drives the run monitor's trace panel via polling). */
    getTraces(id: string): Promise<AgentRunTraceDto[]>
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
    /** Toggle isEnabled=true. Workflow has no separate Draft/Published - IsEnabled IS the publish flag. */
    publish(id: string): Promise<WorkflowDefinitionDto>
    /** Toggle isEnabled=false. */
    unpublish(id: string): Promise<WorkflowDefinitionDto>
    /** Synchronously execute the workflow once. */
    run(id: string, input: string, userId?: string): Promise<WorkflowExecutionResultDto>
    /** Pre-run validation (step refs, cycles, agent refs). */
    validate(id: string): Promise<WorkflowValidationResultDto>
    /** Batch enable workflows. */
    batchEnable(ids: string[]): Promise<number>
    /** Batch disable workflows. */
    batchDisable(ids: string[]): Promise<number>
    /** Per-workflow execution statistics; throws when the backend refuses. */
    getExecutionStats(id: string): Promise<WorkflowExecutionStatsDto>
    /** Version history, newest first (no definition body). */
    getVersions(id: string): Promise<WorkflowDefinitionVersionDto[]>
    /** One version with its full definition snapshot. */
    getVersion(id: string, versionNumber: number): Promise<WorkflowDefinitionVersionDto>
    /** Restore a version (the current definition is snapshotted first). */
    restoreVersion(id: string, versionNumber: number, changeDescription?: string): Promise<void>
  }
  workflowRuns: Pick<BridgeCrudContract<WorkflowExecutionSummaryDto>, 'fetch'> & {
    /** Fetch the full execution detail (steps + outputs + pending approvals). */
    getDetail(executionId: string): Promise<WorkflowExecutionDetailDto>
    /** Resume a paused execution. */
    resume(executionId: string): Promise<WorkflowExecutionResultDto>
    /** Approve a step waiting on human review. */
    approveStep(executionId: string, stepId: string, comment?: string): Promise<void>
    /** Reject a step (`reason` required by backend). */
    rejectStep(executionId: string, stepId: string, reason: string): Promise<void>
    /**
     * Cancel an execution. Paused / awaiting executions are cancelled at once;
     * a running one gets a cancel signal queued (see `currentWaitReason`).
     */
    cancel(executionId: string, reason?: string): Promise<void>
    /** Pending human-in-the-loop interrupt; throws when there is none or the read is refused. */
    getInterrupt(executionId: string): Promise<WorkflowInterruptDto>
    /** Answer the pending interrupt and resume the execution. */
    resumeWithInput(executionId: string, stepId: string, input: Record<string, unknown>): Promise<WorkflowExecutionResultDto>
    /** Pending mailbox signals; throws when the backend refuses (e.g. 501 without a mailbox). */
    getSignals(executionId: string): Promise<WorkflowExecutionSignalDto[]>
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
    getSkills(categoryId: string): Promise<SkillSummaryDto[]>
  }
  providers: BridgeCrudContract<ProviderRow, CreateProviderDto, UpdateProviderDto> & {
    test(id: string): Promise<{ ok: boolean; latency: number; error?: string }>
    /** Enabled provider dropdown options (id + name + type + default model). */
    getOptions(): Promise<ProviderOptionDto[]>
    /** Models for a provider entity (live /v1/models + static fallback). */
    listModels(id: string): Promise<ProviderModelsDto>
    /**
     * Persist a dragged order. Display order only - `priority` (which row wins at
     * runtime when names collide) is untouched. Configuration-source entries have
     * no row and must not be submitted.
     */
    reorder(ids: string[]): Promise<void>
  }
  usage: {
    summary(query: CrudPageQuery): Promise<UsageSummary>
    byAgent(query: CrudPageQuery): Promise<AgentUsageDto[]>
    byModel(query: CrudPageQuery): Promise<ModelUsageDto[]>
    byDay(query: CrudPageQuery): Promise<UsageTrendPointDto[]>
    /** Paged usage log query (OperationType/provider/model/agent/success filters). */
    getLogs(query: CrudPageQuery): Promise<PagedList<UsageLogDto>>
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
    /** Trigger full re-vectorization; returns chunk/document counts + duration. */
    reindex(id: string): Promise<ReindexResultDto>
    /** Paged document list for a knowledge base. */
    getDocuments(kbId: string, query?: DocumentQueryParams): Promise<PagedList<KnowledgeDocumentDto>>
    /** Poll a single document's ingestion status. */
    getDocumentStatus(kbId: string, docId: string): Promise<KnowledgeDocumentDto>
    /** Upload a document (multipart); ingestion is async - poll status by docId. */
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
  /**
   * Usage limits (the 5-hour session window and the weekly window, in API-equivalent USD)
   * and the models each user may pick: everyone's defaults, one user's own settings, the
   * users who used AI, and the tenant's cost budget (see `usage-limit-bridge.ts`).
   */
  usageLimits: UsageLimitBridge
  /** No update: a run is created and executed in one call and never edited. */
  evaluations: Omit<BridgeCrudContract<EvaluationRunDto, CreateEvaluationRunDto>, 'update'> & {
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

/**
 * The usage dashboard's date range as the analytics endpoints bind it. They take
 * non-nullable `DateTime`s, so a cleared picker went out as `''` and every call
 * answered 400. A cleared start reads from the beginning, a cleared end up to now.
 */
const USAGE_OPEN_START = new Date(0).toISOString()
function usageRange(filters: Record<string, unknown>): { startTime: string; endTime: string } {
  const start = typeof filters.startTime === 'string' ? filters.startTime.trim() : ''
  const end = typeof filters.endTime === 'string' ? filters.endTime.trim() : ''
  return { startTime: start || USAGE_OPEN_START, endTime: end || new Date().toISOString() }
}

/**
 * The dashboard's provider / model / agent narrowing, forwarded to every
 * analytics call that takes it. Each panel used to receive a different subset
 * (the summary dropped the agent, the trend and the by-agent breakdown dropped
 * both), so the KPI cards, the trend and the breakdowns described different rows.
 * Blank inputs are omitted: an empty agent id is not a Guid.
 */
function usageNarrowing(filters: Record<string, unknown>): { provider?: string; model?: string; agentId?: string } {
  const pick = (value: unknown): string | undefined =>
    typeof value === 'string' && value.trim() ? value.trim() : undefined
  return { provider: pick(filters.provider), model: pick(filters.model), agentId: pick(filters.agentId) }
}

/** A single-skill switch that changed no row was refused (System scope, file source), not applied. */
function requireSkillToggled(changed: number): void {
  if (changed < 1) {
    throw new Error('The skill was not changed. System-scope and file-source skills cannot be switched on or off here.')
  }
}

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
  const usageLimitApi = deps.usageLimitApi ?? (deps.client ? useAdminUsageLimitApi(deps.client) : null)
  const evaluationApi =
    deps.evaluationApi ?? (deps.client ? useAdminEvaluationApi(deps.client) : null)
  // New capability factories (client-derived; production always passes `client`).
  const skillCategoryApi = deps.client ? useAdminSkillCategoryApi(deps.client) : null
  const mcpAnalyticsApi = deps.client ? useAdminMcpToolAnalyticsApi(deps.client) : null

  // Construction-time guard - fail fast instead of deferring errors to first async call.
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
    !usageLimitApi ||
    !evaluationApi
  ) {
    throw new Error(
      'createAiBridge: provide either `client` (HttpClient) or all 11 api deps ' +
        '(agentApi/agentRunApi/threadApi/workflowApi/skillApi/providerApi/usageApi/' +
        'knowledgeBaseApi/mcpApi/usageLimitApi/evaluationApi)',
    )
  }

  // ---- agents -------------------------------------------------------------
  const agents: AiBridge['agents'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrapOk<PagedList<AgentDto>>(await agentApi.getList(mapQuery(q) as unknown as AgentListQueryDto)),
      ),
    create: async (data) => unwrapOk<AgentDto>(await agentApi.create(data)),
    update: async (id, data) => unwrapOk<AgentDto>(await agentApi.update(String(id), data)),
    delete: async (ids) => {
      // `ensureOk`, not a bare await: HttpClient never rejects on a business
      // failure, it RESOLVES `{ succeeded: false }`. Without this a 409 (still
      // referenced) or 403 came back as success, and the page reported the rows
      // deleted while they were still there.
      for (const id of ids) ensureOk(await agentApi.delete(String(id)))
    },
    // Single-record reads reject a refusal (404 / 403) with the server's reason;
    // unchecked they resolved null and the page failed on a TypeError instead.
    getById: async (id) => unwrapOk<AgentDto>(await agentApi.getById(String(id))),
    clone: async (id, name) => unwrapOk<AgentDto>(await agentApi.clone(String(id), name)),
    getVersions: async (id, query) =>
      unwrapOk<PagedList<AgentVersionDto>>(await agentApi.getVersions(String(id), query)),
    rollbackToVersion: async (id, version) =>
      unwrapOk<AgentDto>(await agentApi.rollbackToVersion(String(id), version)),
    validate: async (id) => unwrapOk<AgentValidationResultDto>(await agentApi.validate(String(id))),
    getHealth: async () => unwrap<AgentHealthSummaryDto>(await agentApi.getHealth()),
    configureAbTest: async (id, data) =>
      unwrapOk<AgentDto>(await agentApi.configureAbTest(String(id), data)),
    stopAbTest: async (id) => unwrapOk<AgentDto>(await agentApi.stopAbTest(String(id))),
    getToolGroups: async () => unwrapOk<ToolGroupDto[]>(await agentApi.getToolGroups()) ?? [],
    getMemory: async (id, query) =>
      unwrapOk<PagedList<AgentMemoryDto>>(await agentApi.getMemory(String(id), query)),
    createMemory: async (id, data) =>
      unwrapOk<AgentMemoryDto>(await agentApi.createMemory(String(id), data)),
    updateMemory: async (id, memoryId, data) =>
      unwrapOk<AgentMemoryDto>(await agentApi.updateMemory(String(id), String(memoryId), data)),
    deleteMemory: async (id, memoryId) => {
      ensureOk(await agentApi.deleteMemory(String(id), String(memoryId)))
    },
  }

  // ---- threads ------------------------------------------------------------
  const threads: AiBridge['threads'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrapOk<PagedList<AgentThreadDto>>(await threadApi.getList(
          mapQuery(q) as unknown as ThreadListQueryDto,
        )),
      ),
    create: async (data) => unwrapOk<AgentThreadDto>(await threadApi.create(data)),
    update: async (id, data) =>
      unwrapOk<AgentThreadDto>(await threadApi.updateTitle(String(id), {
        title: (data as { title?: string }).title ?? '',
      })),
    delete: async (ids) => {
      for (const id of ids) ensureOk(await threadApi.delete(String(id)))
    },
    getDetail: async (id, messageLimit) =>
      unwrapOk<AgentThreadDetailDto>(await threadApi.getDetail(String(id), messageLimit)),
    exportJson: async (id) => unwrapOk<ThreadExportDto>(await threadApi.exportJson(String(id))),
    exportMarkdown: async (id) => unwrapOk<Blob>(await threadApi.exportMarkdown(String(id))),
  }

  // ---- agentRuns ----------------------------------------------------------
  const agentRuns = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<AgentRunDto>> =>
      toCrudResult(
        unwrapOk<PagedList<AgentRunDto>>(await agentRunApi.getList(
          mapQuery(q) as unknown as AgentRunQueryDto,
        )),
      ),
    getById: async (id: string) => unwrapOk<AgentRunDto>(await agentRunApi.getById(id)),
    cancel: async (id: string) => {
      ensureOk(await agentRunApi.cancel(id))
    },
    // A refused read rejects: "no trace events" is not what a 403 means.
    getTraces: async (id: string) => {
      const items = unwrapOk<AgentRunTraceDto[] | undefined>(await agentRunApi.getRunTraces(id))
      return Array.isArray(items) ? items : []
    },
  }

  // ---- workflows ----------------------------------------------------------
  const workflows: AiBridge['workflows'] = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<WorkflowDefinitionDto>> =>
      toCrudResult(
        unwrapOk<PagedList<WorkflowDefinitionDto>>(await workflowApi.getList(
          mapQuery(q) as unknown as WorkflowDefinitionQueryDto,
        )),
      ),
    create: async (data: CreateWorkflowDefinitionDto) =>
      unwrapOk<WorkflowDefinitionDto>(await workflowApi.create(data)),
    update: async (id: string, data: UpdateWorkflowDefinitionDto) =>
      unwrapOk<WorkflowDefinitionDto>(await workflowApi.update(String(id), data)),
    delete: async (ids: string[]) => {
      ensureOk(await workflowApi.batchDelete(ids.map(String)))
    },
    getById: async (id: string) =>
      unwrapOk<WorkflowDefinitionDto>(await workflowApi.getById(String(id))),
    clone: async (id: string) =>
      unwrapOk<WorkflowDefinitionDto>(await workflowApi.clone(String(id))),
    // batchEnable is the current publish semantic - see docs/modules/ai.md Workflow section.
    // batchEnable returns affected-row count; we re-fetch the definition so the contract
    // (Promise<WorkflowDefinitionDto>) is honoured for callers that want the updated entity.
    publish: async (id: string): Promise<WorkflowDefinitionDto> => {
      ensureOk(await workflowApi.batchEnable([String(id)]))
      return unwrapOk<WorkflowDefinitionDto>(await workflowApi.getById(String(id)))
    },
    unpublish: async (id: string): Promise<WorkflowDefinitionDto> => {
      ensureOk(await workflowApi.batchDisable([String(id)]))
      return unwrapOk<WorkflowDefinitionDto>(await workflowApi.getById(String(id)))
    },
    run: async (id: string, input: string, userId?: string) => {
      const body: RunWorkflowRequestDto = { input, userId: userId ?? null }
      return unwrapOk<WorkflowExecutionResultDto>(await workflowApi.run(String(id), body))
    },
    validate: async (id: string) =>
      unwrapOk<WorkflowValidationResultDto>(await workflowApi.validate(String(id))),
    batchEnable: async (ids: string[]) =>
      unwrapOk<number>(await workflowApi.batchEnable(ids.map(String))),
    batchDisable: async (ids: string[]) =>
      unwrapOk<number>(await workflowApi.batchDisable(ids.map(String))),
    // The reads below go through unwrapOk too: a refused read (404 / 403 / 501)
    // must surface as an error, not as an empty history or all-zero stats.
    getExecutionStats: async (id: string) =>
      unwrapOk<WorkflowExecutionStatsDto>(await workflowApi.getExecutionStats(String(id))),
    getVersions: async (id: string) => {
      const items = unwrapOk<WorkflowDefinitionVersionDto[] | null>(await workflowApi.getVersions(String(id)))
      return Array.isArray(items) ? items : []
    },
    getVersion: async (id: string, versionNumber: number) =>
      unwrapOk<WorkflowDefinitionVersionDto>(await workflowApi.getVersion(String(id), versionNumber)),
    restoreVersion: async (id: string, versionNumber: number, changeDescription?: string) => {
      const body = changeDescription?.trim() ? { changeDescription: changeDescription.trim() } : undefined
      unwrapOk<unknown>(await workflowApi.restoreVersion(String(id), versionNumber, body))
    },
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
        unwrapOk<PagedList<WorkflowExecutionSummaryDto>>(await workflowApi.getExecutions(
          params as unknown as never,
        )),
      )
    },
    getDetail: async (executionId: string): Promise<WorkflowExecutionDetailDto> =>
      unwrapOk<WorkflowExecutionDetailDto>(await workflowApi.getExecutionDetail(executionId)),
    resume: async (executionId: string) =>
      unwrapOk<WorkflowExecutionResultDto>(await workflowApi.resumeExecution(executionId)),
    approveStep: async (executionId: string, stepId: string, comment?: string) => {
      const body: WorkflowStepApprovalDto | undefined = comment ? { feedback: comment } : undefined
      ensureOk(await workflowApi.approveStep(executionId, stepId, body))
    },
    rejectStep: async (executionId: string, stepId: string, reason: string) => {
      const body: WorkflowStepApprovalDto = { feedback: reason }
      ensureOk(await workflowApi.rejectStep(executionId, stepId, body))
    },
    cancel: async (executionId: string, reason?: string) => {
      const body: WorkflowStepApprovalDto | undefined = reason?.trim() ? { feedback: reason.trim() } : undefined
      unwrapOk<unknown>(await workflowApi.cancelExecution(executionId, body))
    },
    getInterrupt: async (executionId: string) =>
      unwrapOk<WorkflowInterruptDto>(await workflowApi.getPendingInterrupt(executionId)),
    resumeWithInput: async (executionId: string, stepId: string, input: Record<string, unknown>) =>
      unwrapOk<WorkflowExecutionResultDto>(await workflowApi.resumeWithInput(executionId, { stepId, input })),
    getSignals: async (executionId: string) => {
      const items = unwrapOk<WorkflowExecutionSignalDto[] | null>(await workflowApi.getPendingSignals(executionId))
      return Array.isArray(items) ? items : []
    },
  }

  // ---- skills -------------------------------------------------------------
  const skills = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<SkillSummaryDto>> =>
      toCrudResult(
        unwrapOk<PagedList<SkillSummaryDto>>(await skillApi.getPaged(
          // Admin list always merges DB + file-source skills. Backend hides
          // file-source rows behind this opt-in flag because they're not
          // editable; the admin page distinguishes them via `isReadOnly` +
          // `source` columns instead.
          { ...mapQuery(q), includeFileSource: true } as unknown as SkillQueryDto,
        )),
      ),
    create: async (data: CreateSkillDto) =>
      unwrapOk<SkillSummaryDto>(await skillApi.create(data)),
    update: async (id: string, data: UpdateSkillDto) =>
      unwrapOk<SkillSummaryDto>(await skillApi.update(String(id), data)),
    // The batch endpoints answer 200 with the number of rows they changed and
    // silently skip rows they may not touch (System scope, a file-source row's
    // empty id, another tenant's row). The count is the only signal, so a
    // shortfall is a failure here, not a success.
    delete: async (ids: string[]) => {
      const deleted = unwrapOk<number>(await skillApi.batchDelete(ids.map(String)))
      if (deleted < ids.length) {
        throw new Error(`Only ${deleted} of ${ids.length} skills were deleted. System-scope and file-source skills cannot be deleted here.`)
      }
    },
    // Admin "activate" / "deactivate" map to the batch endpoints on a single id. Zero rows
    // changed means the card was not a row this admin can switch; that is a failure, not a
    // success toast.
    activate: async (id: string) => {
      requireSkillToggled(unwrapOk<number>(await skillApi.batchEnable([String(id)])))
    },
    deactivate: async (id: string) => {
      requireSkillToggled(unwrapOk<number>(await skillApi.batchDisable([String(id)])))
    },
    getBySlug: async (slug: string) => unwrapOk<SkillDetailDto>(await skillApi.getBySlug(slug)),
    search: async (query: string, maxResults?: number) => {
      const items = unwrapOk<SkillSummaryDto[] | undefined>(await skillApi.search(query, maxResults))
      return Array.isArray(items) ? items : []
    },
    getUsageStats: async () => unwrap<SkillUsageStatsDto>(await skillApi.getUsageStats()),
    getPopular: async (topN?: number) => {
      const items = unwrapOk<PopularSkillDto[] | undefined>(await skillApi.getPopular(topN))
      return Array.isArray(items) ? items : []
    },
    exportSkills: async (scope?: SkillScope) => {
      const items = unwrapOk<SkillExportDto[] | undefined>(await skillApi.export(scope))
      return Array.isArray(items) ? items : []
    },
    importSkills: async (data: SkillImportRequestDto) =>
      unwrapOk<SkillImportResultDto>(await skillApi.import(data)),
  }

  // ---- skill categories ---------------------------------------------------
  const skillCategories: AiBridge['skillCategories'] = {
    getTree: async () => {
      const items = unwrapOk<SkillCategoryDto[] | undefined>(await skillCategoryApi!.getTree())
      return Array.isArray(items) ? items : []
    },
    getSkills: async (categoryId) => {
      const items = unwrapOk<SkillSummaryDto[] | undefined>(
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
        unwrapOk<PagedList<ProviderDto>>(
          await providerApi.getList(mapQuery(q) as unknown as ProviderQueryDto),
        ),
      ),
    create: async (data: CreateProviderDto): Promise<ProviderDto> =>
      unwrapOk<ProviderDto>(await providerApi.create(data)),
    update: async (id: string, data: UpdateProviderDto): Promise<ProviderDto> =>
      unwrapOk<ProviderDto>(await providerApi.update(String(id), data)),
    delete: async (ids: string[]): Promise<void> => {
      for (const id of ids) ensureOk(await providerApi.delete(String(id)))
    },
    test: async (id: string): Promise<{ ok: boolean; latency: number; error?: string }> => {
      // Read-only probe, but the result type is non-optional: on a refused envelope
      // bare unwrap handed back undefined and the next line read `.success` off it.
      const result = unwrapOk<ProviderTestResultDto>(await providerApi.test(String(id)))
      return {
        ok: result.success,
        latency: result.latencyMs,
        error: result.success ? undefined : result.message ?? undefined,
      }
    },
    // A refused read rejects (it needs ai.provider.view): unchecked it resolved
    // null and the pickers reading `.length` / `.map` off it threw.
    getOptions: async () => unwrapOk<ProviderOptionDto[] | null>(await providerApi.getOptions()) ?? [],
    listModels: async (id: string) => unwrap<ProviderModelsDto>(await providerApi.listModels(String(id))),
    reorder: async (ids: string[]): Promise<void> => {
      ensureOk(await providerApi.reorder({ ids }))
    },
  }

  // ---- usage --------------------------------------------------------------
  // Every analytics read goes through unwrapOk: the dashboard fans these out with
  // Promise.allSettled and reports a rejected source in its error banner. A bare
  // unwrap turned a refused read (403, 500, a timeout) into null, which the
  // fallbacks below then rendered as zero usage and zero cost.
  const usage = {
    summary: async (q: CrudPageQuery): Promise<UsageSummary> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const query: UsageSummaryQueryDto = { ...usageRange(filters), ...usageNarrowing(filters) }
      const summary = unwrapOk<UsageSummaryDto | undefined>(await usageApi.getSummary(query))
      return {
        totalTokens: summary?.totalTokens ?? 0,
        totalCostUsd: summary?.totalEstimatedCostUsd ?? 0,
        requestCount: summary?.totalRequests ?? 0,
      }
    },
    byAgent: async (q: CrudPageQuery): Promise<AgentUsageDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const { startTime, endTime } = usageRange(filters)
      const items = unwrapOk<AgentUsageDto[] | undefined>(await usageApi.getByAgent(
        startTime,
        endTime,
        (filters.topN as number | undefined) ?? 20,
        usageNarrowing(filters),
      ))
      return Array.isArray(items) ? items : []
    },
    byModel: async (q: CrudPageQuery): Promise<ModelUsageDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const { startTime, endTime } = usageRange(filters)
      const { provider, agentId } = usageNarrowing(filters)
      const items = unwrapOk<ModelUsageDto[] | undefined>(await usageApi.getByModel(startTime, endTime, provider, agentId))
      return Array.isArray(items) ? items : []
    },
    byDay: async (q: CrudPageQuery): Promise<UsageTrendPointDto[]> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      // Backend enum is UsageGranularity { Daily=0, Weekly=1, Monthly=2 } - use
      // the canonical enum reference rather than a string literal so refactors
      // on the enum are caught at type-check time.
      const items = unwrapOk<UsageTrendPointDto[] | undefined>(await usageApi.getTrend({
        ...usageRange(filters),
        ...usageNarrowing(filters),
        granularity: UsageGranularity.Daily,
      }))
      return Array.isArray(items) ? items : []
    },
    getLogs: async (q: CrudPageQuery): Promise<PagedList<UsageLogDto>> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      return unwrapOk<PagedList<UsageLogDto>>(await usageApi.getLogs({
        pageIndex: q.pageIndex ?? 1,
        pageSize: q.pageSize ?? 20,
        startTime: (filters.startTime as string | undefined) || undefined,
        endTime: (filters.endTime as string | undefined) || undefined,
        operationType: filters.operationType as string | undefined,
        isSuccess: filters.isSuccess as boolean | undefined,
        ...usageNarrowing(filters),
      } as unknown as UsageLogQueryDto))
    },
    feedbackStats: async (topN?: number): Promise<AgentFeedbackStatsDto[]> => {
      const items = unwrapOk<AgentFeedbackStatsDto[] | undefined>(await usageApi.getAgentFeedbackStats(topN))
      return Array.isArray(items) ? items : []
    },
    costSummary: async (q: CrudPageQuery): Promise<CostSummaryDto> => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const { startTime, endTime } = usageRange(filters)
      const { provider, model, agentId } = usageNarrowing(filters)
      return unwrapOk<CostSummaryDto>(await usageApi.getCostSummary(startTime, endTime, provider, model, agentId))
    },
  }

  // ---- knowledge ----------------------------------------------------------
  const knowledge: AiBridge['knowledge'] = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<KnowledgeBaseDto>> => {
      const result = unwrapOk<unknown>(await kbApi.getList({
        pageIndex: q.pageIndex ?? 1,
        pageSize: q.pageSize ?? 20,
        keyword: q.searchText || undefined,
      }))
      // Backend may return either a plain array or PagedList<KbDto> - handle both.
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
    create: async (data) => unwrapOk<KnowledgeBaseDto>(await kbApi.create(data)),
    update: async (id, data) => unwrapOk<KnowledgeBaseDto>(await kbApi.update(String(id), data)),
    delete: async (ids) => {
      for (const id of ids) ensureOk(await kbApi.delete(String(id)))
    },
    reindex: async (id) => unwrapOk<ReindexResultDto>(await kbApi.reindex(String(id))),
    getDocuments: async (kbId, query) =>
      unwrapOk<PagedList<KnowledgeDocumentDto>>(await kbApi.getDocuments(String(kbId), query)),
    getDocumentStatus: async (kbId, docId) =>
      unwrap<KnowledgeDocumentDto>(await kbApi.getDocumentStatus(String(kbId), String(docId))),
    uploadDocument: async (kbId, file) =>
      unwrapOk<DocumentUploadResultDto>(await kbApi.uploadDocument(String(kbId), file)),
    deleteDocument: async (kbId, docId) => {
      ensureOk(await kbApi.deleteDocument(String(kbId), String(docId)))
    },
    searchTest: async (kbId, data) => {
      const items = unwrapOk<SearchResultDto[] | undefined>(await kbApi.searchTest(String(kbId), data))
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
      return toCrudResult(unwrapOk<PagedList<McpServerRegistrationDto>>(await mcpApi.getList(query)))
    },
    create: async (data: CreateMcpServerRegistrationDto): Promise<McpServerRow> =>
      unwrapOk<McpServerRegistrationDto>(await mcpApi.create(data)),
    update: async (id: string, data: UpdateMcpServerRegistrationDto): Promise<McpServerRow> =>
      unwrapOk<McpServerRegistrationDto>(await mcpApi.update(String(id), data)),
    delete: async (ids: string[]): Promise<void> => {
      for (const id of ids) ensureOk(await mcpApi.delete(String(id)))
    },
    test: async (id: string): Promise<{ ok: boolean; latency: number; error?: string }> => {
      const result = unwrapOk<McpServerTestResultDto>(await mcpApi.test(String(id)))
      return {
        ok: result.success,
        latency: result.latencyMs,
        error: result.success ? undefined : result.message ?? undefined,
      }
    },
  }

  // ---- mcp (Tnzi's own MCP server) ---------------------------------------
  // The server reads reject a failed envelope: a 403 / 5xx read as an empty
  // status or tool list described a disabled server with nothing exposed, and a
  // 404 / 501 (sub-module not loaded) must reach the page so it can say so.
  const mcp: AiBridge['mcp'] = {
    getStatus: async () => unwrapOk<McpServerStatusDto>(await mcpApi.getStatus()),
    getTools: async () => {
      const items = unwrapOk<McpToolInfoDto[] | undefined>(await mcpApi.getTools())
      return Array.isArray(items) ? items : []
    },
    getExposedAgents: async () => {
      const items = unwrapOk<string[] | undefined>(await mcpApi.getExposedAgents())
      return Array.isArray(items) ? items : []
    },
    exposeAgent: async (agentId, options) => {
      ensureOk(await mcpApi.exposeAgent(String(agentId), options))
    },
    removeAgent: async (agentId) => {
      ensureOk(await mcpApi.removeAgent(String(agentId)))
    },
  }

  // ---- mcp tool analytics -------------------------------------------------
  const mcpToolAnalytics: AiBridge['mcpToolAnalytics'] = {
    getToolStats: async (toolName, from, to) =>
      unwrapOk<McpToolStatsDto>(await mcpAnalyticsApi!.getToolStats(toolName, from, to)),
    getMostUsedTools: async (top, from, to) => {
      const items = unwrapOk<McpToolPopularityDto[] | undefined>(
        await mcpAnalyticsApi!.getMostUsedTools(top, from, to),
      )
      return Array.isArray(items) ? items : []
    },
    getToolErrors: async (toolName, top) => {
      const items = unwrapOk<McpToolErrorDto[] | undefined>(
        await mcpAnalyticsApi!.getToolErrors(toolName, top),
      )
      return Array.isArray(items) ? items : []
    },
    cleanup: async (retentionDays) => unwrapOk<number>(await mcpAnalyticsApi!.cleanup(retentionDays)),
  }

  // ---- usage limits -------------------------------------------------------
  const usageLimits = createUsageLimitBridge({ usageLimitApi })

  // ---- evaluations --------------------------------------------------------
  const evaluations: AiBridge['evaluations'] = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<EvaluationRunDto>> =>
      toCrudResult(
        unwrapOk<PagedList<EvaluationRunDto>>(await evaluationApi.getList(
          mapQuery(q) as unknown as EvaluationRunQueryDto,
        )),
      ),
    // Backend's "create" is create-and-run in one shot (POST /admin/ai/evaluations/run).
    create: async (data: CreateEvaluationRunDto): Promise<EvaluationRunDto> =>
      unwrapOk<EvaluationRunDetailDto>(await evaluationApi.create(data)),
    delete: async (ids: string[]) => {
      // See agents.delete: a failed delete resolves rather than rejects, so an
      // unchecked await reports success on a row that never went away.
      for (const id of ids) ensureOk(await evaluationApi.delete(String(id)))
    },
    runBatch: async (data: BatchEvaluationDto): Promise<BatchEvaluationResultDto> =>
      unwrapOk<BatchEvaluationResultDto>(await evaluationApi.runBatch(data)),
    getDetail: async (id: string): Promise<EvaluationRunDetailDto> =>
      unwrapOk<EvaluationRunDetailDto>(await evaluationApi.getById(id)),
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
    usageLimits,
    evaluations,
  }
}

// 0.2.72+ (B4): re-export AI enums + types so pages can consume runtime
// values via the bridge surface and stay clean under the
// `no-restricted-imports` guard against `@tnzi/core/services/*` value
// imports from `pages/**`.
export { WorkflowExecutionMode, UsageLimitMode, UsageLimitWindow } from '@tnzi/core/services/ai'
export type {
  UsageLimitDefaultsDto,
  UserUsageLimitsDto,
  UserUsageSummaryDto,
  SaveUsagePolicyDto,
  UsagePolicyDto,
  UsageLimitSettingDto,
  UsageWindowStatusDto,
  EffectiveUsagePolicyDto,
  ChatModelDto,
} from '@tnzi/core/services/ai'

/**
 * Workspace-agent helper - used by the read-only `WorkspaceAgents`
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
      // A refused read rejects: "no workspace agents" is a statement about the disk.
      return unwrapOk<WorkspaceAgentDto[] | null>(await api.listAgents()) ?? []
    },
  }
}
