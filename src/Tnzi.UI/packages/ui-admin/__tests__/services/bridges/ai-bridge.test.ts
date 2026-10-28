import { describe, it, expect, vi } from 'vitest'
import { createAiBridge } from '../../../src/services/bridges/ai-bridge'

/**
 * The wire shape every core api returns: `ApiResult<T>` with the DTO in `data`.
 * The probe DTOs (`{ success, message, latencyMs }`) carry their own `success`
 * field, so a fixture that returns the bare DTO is not a harmless shortcut: the
 * envelope helpers read `success` off whatever they are handed, and a bare
 * `{ success: false }` reads as a refused envelope instead of a failed probe.
 */
function ok<T>(data: T) {
  return { succeeded: true, success: true, code: 200, data, message: '' }
}

function pagedList<T>(items: T[]) {
  return {
    items,
    totalCount: items.length,
    pageIndex: 1,
    pageSize: 20,
    totalPages: 1,
    hasNextPage: false,
    hasPreviousPage: false,
  }
}

function mockAgentApi() {
  return {
    getList: vi.fn(async () => pagedList([{ id: 'a1', name: 'Bot' }])),
    getById: vi.fn(async () => ({ id: 'a1' })),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    clone: vi.fn(),
    run: vi.fn(),
    getRunStreamUrl: vi.fn(),
    getVersions: vi.fn(),
    getVersion: vi.fn(),
    rollbackToVersion: vi.fn(),
    validate: vi.fn(),
    getHealth: vi.fn(),
  }
}

function mockAgentRunApi() {
  return {
    getStats: vi.fn(),
    getList: vi.fn(async () => pagedList([{ id: 'r1' }])),
    getById: vi.fn(),
    getNodes: vi.fn(),
    getNode: vi.fn(),
    getNodeTraces: vi.fn(),
    getRunTraces: vi.fn(),
    cancel: vi.fn(async () => undefined),
    approve: vi.fn(),
    reject: vi.fn(),
    retryNode: vi.fn(),
  }
}

function mockThreadApi() {
  return {
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 't-new' })),
    getList: vi.fn(async () => pagedList([{ id: 't1', title: 'Thread' }])),
    getById: vi.fn(),
    getDetail: vi.fn(),
    updateTitle: vi.fn(async (id: string, d: { title: string }) => ({ id, title: d.title })),
    delete: vi.fn(async () => undefined),
    exportJson: vi.fn(),
    exportMarkdown: vi.fn(),
  }
}

function mockWorkflowApi() {
  return {
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'wf-new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(),
    getById: vi.fn(async (id: string) => ({ id, isEnabled: true })),
    getList: vi.fn(async () => pagedList([{ id: 'wf1' }])),
    run: vi.fn(),
    getRunStreamUrl: vi.fn(),
    clone: vi.fn(async (id: string) => ({ id: `${id}-clone` })),
    getExecutionStatus: vi.fn(),
    getExecutionDetail: vi.fn(),
    resumeExecution: vi.fn(),
    approveStep: vi.fn(),
    rejectStep: vi.fn(),
    getExecutions: vi.fn(async () => pagedList([{ id: 'exec1' }])),
    batchDelete: vi.fn(async () => 0),
    batchEnable: vi.fn(async () => 1),
    batchDisable: vi.fn(),
    getStats: vi.fn(),
    validate: vi.fn(),
    getExecutionStats: vi.fn(),
    getVersions: vi.fn(),
    getVersion: vi.fn(),
    restoreVersion: vi.fn(),
    cancelExecution: vi.fn(),
    getPendingInterrupt: vi.fn(),
    resumeWithInput: vi.fn(),
    getPendingSignals: vi.fn(),
  }
}

/** The envelope a refused request resolves to (HttpClient does not reject on 4xx/5xx). */
function refused(code: number, message: string) {
  return { succeeded: false, success: false, code, data: null, message }
}

function mockSkillApi() {
  return {
    getPaged: vi.fn(async () => pagedList([{ id: 's1', slug: 'skill-1' }])),
    getBySlug: vi.fn(),
    search: vi.fn(),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 's-new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(),
    batchDelete: vi.fn(async () => 0),
    batchEnable: vi.fn(async () => 1),
    batchDisable: vi.fn(async () => 1),
    getUsageStats: vi.fn(),
    getPopular: vi.fn(),
    export: vi.fn(),
    import: vi.fn(),
  }
}

function mockProviderApi() {
  return {
    getProviders: vi.fn(async () => ['openai', 'anthropic']),
    getDefaultModel: vi.fn(),
    getList: vi.fn(async () =>
      pagedList([
        { id: 'p1', name: 'openai-prod', providerType: 'OpenAI', priority: 0, isEnabled: true, hasApiKey: true },
        { id: 'p2', name: 'anthropic-prod', providerType: 'Anthropic', priority: 0, isEnabled: true, hasApiKey: false },
      ]),
    ),
    getById: vi.fn(),
    create: vi.fn(async (d: unknown) => ({ id: 'p-new', hasApiKey: true, ...(d as object) })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, hasApiKey: false, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    test: vi.fn(async () => ok({ success: true, message: 'ok', latencyMs: 12 })),
  }
}

function mockUsageApi() {
  return {
    getSummary: vi.fn(async () => ({
      totalTokens: 1000,
      totalEstimatedCostUsd: 1.5,
      totalRequests: 10,
      successfulRequests: 10,
      failedRequests: 0,
      totalInputTokens: 600,
      totalOutputTokens: 400,
      averageDurationMs: 100,
      successRate: 1,
    })),
    getLogs: vi.fn(),
    getByProvider: vi.fn(),
    getByModel: vi.fn(async () => [{ model: 'gpt-4', tokens: 500 }]),
    getTrend: vi.fn(async () => [{ time: '2026-04-12', tokens: 500 }]),
    getByAgent: vi.fn(async () => [{ agentId: 'a1', tokens: 500 }]),
    getAgentFeedbackStats: vi.fn(),
    getCostSummary: vi.fn(),
  }
}

function mockKnowledgeBaseApi() {
  return {
    getList: vi.fn(async () => pagedList([{ id: 'kb1' }])),
    getById: vi.fn(),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'kb-new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    uploadDocument: vi.fn(),
    getDocuments: vi.fn(),
    deleteDocument: vi.fn(),
    searchTest: vi.fn(),
    getStats: vi.fn(),
    reindex: vi.fn(async (_id: string) => ({
      knowledgeBaseId: _id,
      chunkCount: 0,
      documentCount: 0,
      durationMs: 1,
    })),
  }
}

function mockMcpApi() {
  return {
    // Legacy hosting endpoints - still on the factory but no longer used by the bridge
    getStatus: vi.fn(async () => ({
      enabled: true,
      transport: 'http',
      endpoint: '/mcp',
      requireAuthentication: true,
      rateLimitPerMinute: 60,
      exposedAgentCount: 0,
      customToolCount: 3,
      totalToolCount: 3,
    })),
    getTools: vi.fn(async () => [{ name: 'tool-1' }, { name: 'tool-2' }, { name: 'tool-3' }]),
    getExposedAgents: vi.fn(async () => []),
    exposeAgent: vi.fn(),
    removeAgent: vi.fn(),
    // Phase 5 entity-driven CRUD
    getList: vi.fn(async () =>
      pagedList([
        {
          id: 'm1',
          name: 'github-mcp',
          serverUrl: 'https://api.githubcopilot.com/mcp/',
          transport: 'streamable-http',
          authType: 'bearer',
          hasAuthToken: true,
          priority: 0,
          isEnabled: true,
          creationTime: new Date().toISOString(),
        },
      ]),
    ),
    getById: vi.fn(),
    create: vi.fn(async (d: unknown) => ({ id: 'm-new', hasAuthToken: false, ...(d as object) })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, hasAuthToken: false, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    test: vi.fn(async () => ok({ success: true, message: 'ok', latencyMs: 7 })),
  }
}

function mockUsageLimitApi() {
  return {
    getDefaults: vi.fn(async () => ok({ policy: {}, configuration: {}, effective: {}, catalog: [] })),
    saveDefaults: vi.fn(async (d: unknown) => ok({ policy: d, configuration: {}, effective: {}, catalog: [] })),
    getUser: vi.fn(async (userId: string) => ok({ userId, hasCustomPolicy: false })),
    saveUser: vi.fn(async (userId: string, d: unknown) => ok({ userId, hasCustomPolicy: true, policy: d })),
    clearUser: vi.fn(async () => ok(undefined)),
    resetUser: vi.fn(async () => ok(undefined)),
    queryUsers: vi.fn(async () => ok(pagedList([{ userId: 'u1', hasCustomPolicy: false, windows: [] }]))),
    getBudgetSummary: vi.fn(async () => ok({ byAgent: [] })),
  }
}

function mockEvaluationApi() {
  return {
    getById: vi.fn(),
    getList: vi.fn(async () => pagedList([{ id: 'e1' }])),
    delete: vi.fn(async () => undefined),
    create: vi.fn(async (d: unknown) => ({ id: 'e-new', ...(d as object) })),
    runBatch: vi.fn(async () => ({ results: [], totalDuration: '00:00:01' })),
  }
}

function makeBridge() {
  return createAiBridge({
    agentApi: mockAgentApi() as never,
    agentRunApi: mockAgentRunApi() as never,
    threadApi: mockThreadApi() as never,
    workflowApi: mockWorkflowApi() as never,
    skillApi: mockSkillApi() as never,
    providerApi: mockProviderApi() as never,
    usageApi: mockUsageApi() as never,
    knowledgeBaseApi: mockKnowledgeBaseApi() as never,
    mcpApi: mockMcpApi() as never,
    usageLimitApi: mockUsageLimitApi() as never,
    evaluationApi: mockEvaluationApi() as never,
  })
}

describe('ai-bridge', () => {
  describe('rejects with the server message on a failed envelope', () => {
    // These verbs (rollback / configure / reindex / cleanup, and `create` behind a
    // non-null-asserted receiver) were outside the write gate's reach, so a refusal
    // resolved `undefined`: AgentDetail then did `agent.value = undefined` and toasted a
    // TypeError; Knowledge read `.chunkCount` of undefined.
    const refused = (message: string) => ({ succeeded: false, success: false, code: 409, data: null, message })

    it('agents.rollbackToVersion / configureAbTest', async () => {
      const agentApi = {
        ...mockAgentApi(),
        rollbackToVersion: vi.fn(async () => refused('version is archived')),
        configureAbTest: vi.fn(async () => refused('agent is locked')),
      }
      const bridge = createAiBridge({ agentApi: agentApi as never, client: {} as never })
      await expect(bridge.agents.rollbackToVersion('a1', 2)).rejects.toThrow('version is archived')
      await expect(bridge.agents.configureAbTest('a1', {} as never)).rejects.toThrow('agent is locked')
    })

    // Threads.vue serialises the export result into a file: a refusal resolved to
    // `undefined` produced a download whose body was the string `undefined` plus a
    // success toast.
    it('threads.exportJson', async () => {
      const threadApi = { ...mockThreadApi(), exportJson: vi.fn(async () => refused('thread is being archived')) }
      const bridge = createAiBridge({ threadApi: threadApi as never, client: {} as never })
      await expect(bridge.threads.exportJson('t1')).rejects.toThrow('thread is being archived')
    })

    // Same for the Markdown file: a refused export must not become a download.
    it('threads.exportMarkdown', async () => {
      const threadApi = { ...mockThreadApi(), exportMarkdown: vi.fn(async () => refused('thread is being archived')) }
      const bridge = createAiBridge({ threadApi: threadApi as never, client: {} as never })
      await expect(bridge.threads.exportMarkdown('t1')).rejects.toThrow('thread is being archived')
    })

    it('knowledgeBases.reindex', async () => {
      const knowledgeBaseApi = { ...mockKnowledgeBaseApi(), reindex: vi.fn(async () => refused('reindex already running')) }
      const bridge = createAiBridge({ knowledgeBaseApi: knowledgeBaseApi as never, client: {} as never })
      await expect(bridge.knowledge.reindex('kb1')).rejects.toThrow('reindex already running')
    })

    // skillCategories / mcpToolAnalytics are built straight from `deps.client` (no
    // injectable api), so the refusal is fed through a client whose writes all refuse.
    it('mcpToolAnalytics.cleanup', async () => {
      const client = {
        get: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: [] })),
        post: vi.fn(async () => refused('retention too short')),
        put: vi.fn(async () => refused('retention too short')),
        delete: vi.fn(async () => refused('retention too short')),
      }
      const bridge = createAiBridge({ client: client as never })
      await expect(bridge.mcpToolAnalytics.cleanup(1)).rejects.toThrow('retention too short')
    })

    // Single-record reads: a refused envelope resolved null, and the page then
    // failed on a TypeError ("Cannot read properties of null") instead of
    // showing the server's 404 / 403 text.
    it('single-record reads reject a refusal with the server reason', async () => {
      const notFound = () => refused('No such record')
      const bridge = createAiBridge({
        agentApi: { ...mockAgentApi(), getById: vi.fn(async () => notFound()), validate: vi.fn(async () => notFound()) } as never,
        agentRunApi: { getById: vi.fn(async () => notFound()), getRunTraces: vi.fn(async () => notFound()) } as never,
        threadApi: { getDetail: vi.fn(async () => notFound()) } as never,
        workflowApi: {
          ...mockWorkflowApi(),
          getById: vi.fn(async () => notFound()),
          validate: vi.fn(async () => notFound()),
          getExecutionDetail: vi.fn(async () => notFound()),
        } as never,
        skillApi: { getBySlug: vi.fn(async () => notFound()) } as never,
        evaluationApi: { ...mockEvaluationApi(), getById: vi.fn(async () => notFound()) } as never,
        providerApi: { ...mockProviderApi(), getOptions: vi.fn(async () => notFound()) } as never,
        client: {} as never,
      })
      await expect(bridge.agents.getById('a1')).rejects.toThrow('No such record')
      await expect(bridge.agents.validate('a1')).rejects.toThrow('No such record')
      await expect(bridge.agentRuns.getById('r1')).rejects.toThrow('No such record')
      await expect(bridge.agentRuns.getTraces('r1')).rejects.toThrow('No such record')
      await expect(bridge.threads.getDetail('t1')).rejects.toThrow('No such record')
      await expect(bridge.workflows.getById('w1')).rejects.toThrow('No such record')
      await expect(bridge.workflows.validate('w1')).rejects.toThrow('No such record')
      await expect(bridge.workflowRuns.getDetail('x1')).rejects.toThrow('No such record')
      await expect(bridge.skills.getBySlug('s')).rejects.toThrow('No such record')
      await expect(bridge.evaluations.getDetail('e1')).rejects.toThrow('No such record')
      await expect(bridge.providers.getOptions()).rejects.toThrow('No such record')
    })

    // Read-only probes with a non-optional result type: a refused envelope (403/404)
    // used to hand back `undefined`, and the very next line read `.success` off it, so
    // Providers / McpServers showed "Cannot read properties of undefined" instead of
    // the server's reason. Not a false success, but the same masked refusal.
    it('providers.test / mcpServers.test', async () => {
      const providerApi = { ...mockProviderApi(), test: vi.fn(async () => refused('provider is disabled')) }
      const mcpApi = { ...mockMcpApi(), test: vi.fn(async () => refused('server not found')) }
      const bridge = createAiBridge({ providerApi: providerApi as never, mcpApi: mcpApi as never, client: {} as never })
      await expect(bridge.providers.test('p1')).rejects.toThrow('provider is disabled')
      await expect(bridge.mcpServers.test('m1')).rejects.toThrow('server not found')
    })

    // The usage dashboard and the budget tab are where an operator watches spend. A
    // refused read (403, 500, a timeout) resolved to null and was mapped to zero
    // usage / "Within budget, $0.00", which reads exactly like a quiet month.
    it('usage analytics reads and the budget summary', async () => {
      const denied = () => vi.fn(async () => refused('cost tracking unavailable'))
      const usageApi = {
        getSummary: denied(),
        getLogs: denied(),
        getByProvider: denied(),
        getByModel: denied(),
        getTrend: denied(),
        getByAgent: denied(),
        getAgentFeedbackStats: denied(),
        getCostSummary: denied(),
      }
      const usageLimitApi = { ...mockUsageLimitApi(), getBudgetSummary: denied() }
      const bridge = createAiBridge({ usageApi: usageApi as never, usageLimitApi: usageLimitApi as never, client: {} as never })
      const q = { pageIndex: 1, pageSize: 20, searchText: '', filters: { startTime: '2026-04-01', endTime: '2026-04-30' } }

      await expect(bridge.usage.summary(q)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usage.byAgent(q)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usage.byModel(q)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usage.byDay(q)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usage.getLogs(q)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usage.feedbackStats(20)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usage.costSummary(q)).rejects.toThrow('cost tracking unavailable')
      await expect(bridge.usageLimits.getBudgetSummary()).rejects.toThrow('cost tracking unavailable')
    })
  })

  it('throws synchronously when called with no deps (fail-fast at construction)', () => {
    expect(() => createAiBridge()).toThrow(/provide either `client`/)
  })

  it('throws synchronously when called with partial api deps and no client', () => {
    expect(() =>
      createAiBridge({ agentApi: mockAgentApi() as never }),
    ).toThrow(/provide either `client`/)
  })

  it('exposes all 13 sub-contracts', () => {
    const bridge = makeBridge()
    const keys = [
      'agents',
      'threads',
      'agentRuns',
      'workflows',
      'workflowRuns',
      'skills',
      'providers',
      'usage',
      'knowledge',
      'mcpServers',
      'usageLimits',
      'evaluations',
    ] as const
    for (const key of keys) expect(bridge).toHaveProperty(key)
  })

  it('agents.fetch maps query keyword and calls agentApi.getList', async () => {
    const agentApi = mockAgentApi()
    const bridge = createAiBridge({ agentApi: agentApi as never, client: {} as never })
    const result = await bridge.agents.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: 'bot',
      filters: {},
    })
    expect(agentApi.getList).toHaveBeenCalledWith(expect.objectContaining({ keyword: 'bot' }))
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('list reads reject a refused read instead of answering an empty list', async () => {
    const refused = { succeeded: false, success: false, code: 500, data: null, message: 'Database unavailable' }
    const agentApi = { ...mockAgentApi(), getList: vi.fn(async () => refused) }
    const bridge = createAiBridge({ agentApi: agentApi as never, client: {} as never })
    await expect(bridge.agents.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow('Database unavailable')
  })

  it('agents.create / update reject with the server message on a failed envelope', async () => {
    const refused = { succeeded: false, success: false, code: 400, data: null, message: 'Agent name must be unique' }
    const agentApi = { ...mockAgentApi(), create: vi.fn(async () => refused), update: vi.fn(async () => refused) }
    const bridge = createAiBridge({ agentApi: agentApi as never, client: {} as never })
    await expect(bridge.agents.create({ name: 'X' } as never)).rejects.toThrow('Agent name must be unique')
    await expect(bridge.agents.update('a1', { name: 'Y' } as never)).rejects.toThrow('Agent name must be unique')
  })

  it('agents.create / update / delete delegate to agentApi', async () => {
    const agentApi = mockAgentApi()
    const bridge = createAiBridge({ agentApi: agentApi as never, client: {} as never })
    await bridge.agents.create({ name: 'X' } as never)
    expect(agentApi.create).toHaveBeenCalledWith({ name: 'X' })
    await bridge.agents.update('a1', { name: 'Y' } as never)
    expect(agentApi.update).toHaveBeenCalledWith('a1', { name: 'Y' })
    await bridge.agents.delete(['a1', 'a2'])
    expect(agentApi.delete).toHaveBeenCalledTimes(2)
  })

  it('threads.update routes to updateTitle', async () => {
    const threadApi = mockThreadApi()
    const bridge = createAiBridge({ threadApi: threadApi as never, client: {} as never })
    await bridge.threads.update('t1', { title: 'New' } as never)
    expect(threadApi.updateTitle).toHaveBeenCalledWith('t1', { title: 'New' })
  })

  it('agentRuns.cancel delegates to agentRunApi.cancel', async () => {
    const agentRunApi = mockAgentRunApi()
    const bridge = createAiBridge({ agentRunApi: agentRunApi as never, client: {} as never })
    await bridge.agentRuns.cancel('run-1')
    expect(agentRunApi.cancel).toHaveBeenCalledWith('run-1')
  })

  it('agentRuns.getTraces delegates to agentRunApi.getRunTraces', async () => {
    const agentRunApi = mockAgentRunApi()
    agentRunApi.getRunTraces.mockResolvedValueOnce([
      { id: 't1', runId: 'run-1', eventType: 'tool_call', eventData: 'x', durationMs: 1, creationTime: 'now' },
    ])
    const bridge = createAiBridge({ agentRunApi: agentRunApi as never, client: {} as never })
    const traces = await bridge.agentRuns.getTraces('run-1')
    expect(agentRunApi.getRunTraces).toHaveBeenCalledWith('run-1')
    expect(traces).toHaveLength(1)
    expect(traces[0]?.eventType).toBe('tool_call')
  })

  it('agentRuns.getById reads one run', async () => {
    const run = { id: 'run-1', status: 'Running' }
    const bridge = createAiBridge({
      agentRunApi: { getById: vi.fn(async () => ok(run)) } as never,
      client: {} as never,
    })
    await expect(bridge.agentRuns.getById('run-1')).resolves.toEqual(run)
  })

  it('workflows.delete uses batchDelete and clone delegates correctly', async () => {
    const workflowApi = mockWorkflowApi()
    const bridge = createAiBridge({ workflowApi: workflowApi as never, client: {} as never })
    await bridge.workflows.delete(['w1', 'w2'])
    expect(workflowApi.batchDelete).toHaveBeenCalledWith(['w1', 'w2'])
    await bridge.workflows.clone('w1')
    expect(workflowApi.clone).toHaveBeenCalledWith('w1')
  })

  it('workflows.publish wraps batchEnable + getById (IsEnabled is the publish semantic)', async () => {
    const workflowApi = mockWorkflowApi()
    const bridge = createAiBridge({ workflowApi: workflowApi as never, client: {} as never })
    const result = await bridge.workflows.publish('w1')
    expect(workflowApi.batchEnable).toHaveBeenCalledWith(['w1'])
    expect(workflowApi.getById).toHaveBeenCalledWith('w1')
    expect((result as { id: string }).id).toBe('w1')
  })

  it('workflowRuns.fetch delegates to workflowApi.getExecutions', async () => {
    const workflowApi = mockWorkflowApi()
    const bridge = createAiBridge({ workflowApi: workflowApi as never, client: {} as never })
    const result = await bridge.workflowRuns.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: {},
    })
    expect(workflowApi.getExecutions).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
  })

  it('workflowRuns.getDetail delegates to workflowApi.getExecutionDetail', async () => {
    const workflowApi = mockWorkflowApi()
    workflowApi.getExecutionDetail.mockResolvedValueOnce({
      id: 'exec-1',
      status: 'Completed',
      completedStepIds: ['s1'],
      stepsAwaitingApproval: [],
      stepOutputs: {},
      initialInput: '',
      creationTime: '2026-04-14T00:00:00Z',
    } as never)
    const bridge = createAiBridge({ workflowApi: workflowApi as never, client: {} as never })
    const result = await bridge.workflowRuns.getDetail('exec-1')
    expect(workflowApi.getExecutionDetail).toHaveBeenCalledWith('exec-1')
    expect(result.id).toBe('exec-1')
  })

  describe('workflow versions / execution control / stats', () => {
    function setup() {
      const workflowApi = mockWorkflowApi()
      const bridge = createAiBridge({ workflowApi: workflowApi as never, client: {} as never })
      return { workflowApi, bridge }
    }

    it('getVersions / getVersion unwrap the envelope', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.getVersions.mockResolvedValueOnce(ok([{ id: 'v', versionNumber: 2 }]) as never)
      workflowApi.getVersion.mockResolvedValueOnce(ok({ id: 'v', versionNumber: 2, definition: '{}' }) as never)
      expect(await bridge.workflows.getVersions('wf1')).toEqual([{ id: 'v', versionNumber: 2 }])
      expect(workflowApi.getVersions).toHaveBeenCalledWith('wf1')
      expect((await bridge.workflows.getVersion('wf1', 2)).definition).toBe('{}')
      expect(workflowApi.getVersion).toHaveBeenCalledWith('wf1', 2)
    })

    it('a refused history read throws instead of reading as an empty history', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.getVersions.mockResolvedValueOnce(refused(404, 'Workflow not found') as never)
      await expect(bridge.workflows.getVersions('wf1')).rejects.toThrow('Workflow not found')
    })

    it('restoreVersion posts the trimmed note and throws on refusal', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.restoreVersion.mockResolvedValueOnce(ok(null) as never)
      await bridge.workflows.restoreVersion('wf1', 3, '  back  ')
      expect(workflowApi.restoreVersion).toHaveBeenCalledWith('wf1', 3, { changeDescription: 'back' })

      workflowApi.restoreVersion.mockResolvedValueOnce(refused(500, 'Workflow version snapshot is not restorable') as never)
      await expect(bridge.workflows.restoreVersion('wf1', 3)).rejects.toThrow(/not restorable/)
      expect(workflowApi.restoreVersion).toHaveBeenLastCalledWith('wf1', 3, undefined)
    })

    it('a refused stats read throws instead of reading as zero runs', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.getExecutionStats.mockResolvedValueOnce(refused(403, 'Forbidden') as never)
      await expect(bridge.workflows.getExecutionStats('wf1')).rejects.toThrow('Forbidden')
    })

    it('cancel sends the reason as feedback and throws on refusal', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.cancelExecution.mockResolvedValueOnce(ok(null) as never)
      await bridge.workflowRuns.cancel('exec-1', 'stop it')
      expect(workflowApi.cancelExecution).toHaveBeenCalledWith('exec-1', { feedback: 'stop it' })

      workflowApi.cancelExecution.mockResolvedValueOnce(refused(400, 'already in a terminal state') as never)
      await expect(bridge.workflowRuns.cancel('exec-1')).rejects.toThrow(/terminal state/)
    })

    it('resumeWithInput posts { stepId, input } and throws on refusal', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.resumeWithInput.mockResolvedValueOnce(ok({ status: 'Completed', output: 'done' }) as never)
      const result = await bridge.workflowRuns.resumeWithInput('exec-1', 'gate', { approved: true })
      expect(workflowApi.resumeWithInput).toHaveBeenCalledWith('exec-1', { stepId: 'gate', input: { approved: true } })
      expect(result.status).toBe('Completed')

      workflowApi.resumeWithInput.mockResolvedValueOnce(refused(409, 'already being resumed') as never)
      await expect(bridge.workflowRuns.resumeWithInput('exec-1', 'gate', {})).rejects.toThrow(/already being resumed/)
    })

    it('getInterrupt / getSignals throw when the read is refused', async () => {
      const { workflowApi, bridge } = setup()
      workflowApi.getPendingInterrupt.mockResolvedValueOnce(refused(404, 'No pending interrupt') as never)
      await expect(bridge.workflowRuns.getInterrupt('exec-1')).rejects.toThrow('No pending interrupt')
      workflowApi.getPendingSignals.mockResolvedValueOnce(refused(501, 'Workflow mailbox is not available') as never)
      await expect(bridge.workflowRuns.getSignals('exec-1')).rejects.toThrow(/mailbox/)

      workflowApi.getPendingSignals.mockResolvedValueOnce(ok([{ signalId: 's', type: 'cancel', createdAt: 'x' }]) as never)
      expect(await bridge.workflowRuns.getSignals('exec-1')).toHaveLength(1)
    })
  })

  it('skills.activate maps to batchEnable with the single id', async () => {
    const skillApi = mockSkillApi()
    const bridge = createAiBridge({ skillApi: skillApi as never, client: {} as never })
    await bridge.skills.activate('skill-1')
    expect(skillApi.batchEnable).toHaveBeenCalledWith(['skill-1'])
  })

  it('skills.deactivate maps to batchDisable', async () => {
    const skillApi = mockSkillApi()
    const bridge = createAiBridge({ skillApi: skillApi as never, client: {} as never })
    await bridge.skills.deactivate('skill-1')
    expect(skillApi.batchDisable).toHaveBeenCalledWith(['skill-1'])
  })

  // The batch endpoints answer 200 with the number of rows they changed and skip
  // what they may not touch (System-scope rows, a file-source row's empty id). A
  // count of 0 used to read as success: "Deactivated", and the skill still offered.
  it('skills.activate / deactivate / delete fail when the server changed nothing', async () => {
    const skillApi = {
      ...mockSkillApi(),
      batchEnable: vi.fn(async () => ok(0)),
      batchDisable: vi.fn(async () => ok(0)),
      batchDelete: vi.fn(async () => ok(1)),
    }
    const bridge = createAiBridge({ skillApi: skillApi as never, client: {} as never })
    await expect(bridge.skills.activate('skill-1')).rejects.toThrow(/not changed/)
    await expect(bridge.skills.deactivate('skill-1')).rejects.toThrow(/not changed/)
    await expect(bridge.skills.delete(['skill-1', 'skill-2'])).rejects.toThrow(/1 of 2/)

    skillApi.batchDisable.mockResolvedValueOnce(ok(1))
    await expect(bridge.skills.deactivate('skill-1')).resolves.toBeUndefined()
    skillApi.batchDelete.mockResolvedValueOnce(ok(2))
    await expect(bridge.skills.delete(['skill-1', 'skill-2'])).resolves.toBeUndefined()
  })

  it('providers.fetch delegates to providerApi.getList', async () => {
    const providerApi = mockProviderApi()
    const bridge = createAiBridge({ providerApi: providerApi as never, client: {} as never })
    const result = await bridge.providers.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: {},
    })
    expect(providerApi.getList).toHaveBeenCalled()
    expect(result.items.length).toBe(2)
    expect(result.items[0].name).toBe('openai-prod')
    expect(result.items[0].hasApiKey).toBe(true)
    expect(result.totalCount).toBe(2)
  })

  it('providers.create delegates to providerApi.create', async () => {
    const providerApi = mockProviderApi()
    const bridge = createAiBridge({ providerApi: providerApi as never, client: {} as never })
    const created = await bridge.providers.create({
      name: 'new-provider',
      providerType: 'OpenAI',
      apiKey: 'sk-secret',
    })
    expect(providerApi.create).toHaveBeenCalledWith({
      name: 'new-provider',
      providerType: 'OpenAI',
      apiKey: 'sk-secret',
    })
    expect(created.id).toBe('p-new')
  })

  it('providers.update delegates to providerApi.update', async () => {
    const providerApi = mockProviderApi()
    const bridge = createAiBridge({ providerApi: providerApi as never, client: {} as never })
    const updated = await bridge.providers.update('p1', { description: 'updated' })
    expect(providerApi.update).toHaveBeenCalledWith('p1', { description: 'updated' })
    expect(updated.id).toBe('p1')
  })

  it('providers.delete iterates ids', async () => {
    const providerApi = mockProviderApi()
    const bridge = createAiBridge({ providerApi: providerApi as never, client: {} as never })
    await bridge.providers.delete(['p1', 'p2'])
    expect(providerApi.delete).toHaveBeenCalledTimes(2)
    expect(providerApi.delete).toHaveBeenCalledWith('p1')
    expect(providerApi.delete).toHaveBeenCalledWith('p2')
  })

  it('providers.test maps backend ProviderTestResultDto to {ok,latency,error}', async () => {
    const providerApi = mockProviderApi()
    const bridge = createAiBridge({ providerApi: providerApi as never, client: {} as never })
    const result = await bridge.providers.test('p1')
    expect(providerApi.test).toHaveBeenCalledWith('p1')
    expect(result.ok).toBe(true)
    expect(result.latency).toBe(12)
    expect(result.error).toBeUndefined()
  })

  it('providers.test surfaces failure message in error field', async () => {
    const providerApi = mockProviderApi()
    providerApi.test.mockResolvedValueOnce(ok({ success: false, message: 'unreachable', latencyMs: 5 }))
    const bridge = createAiBridge({ providerApi: providerApi as never, client: {} as never })
    const result = await bridge.providers.test('p1')
    expect(result.ok).toBe(false)
    expect(result.error).toBe('unreachable')
  })

  it('usage.summary delegates to usageApi.getSummary and maps fields', async () => {
    const usageApi = mockUsageApi()
    const bridge = createAiBridge({ usageApi: usageApi as never, client: {} as never })
    const summary = await bridge.usage.summary({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { startTime: '2026-04-01', endTime: '2026-04-12' },
    })
    expect(usageApi.getSummary).toHaveBeenCalled()
    expect(summary).toEqual({ totalTokens: 1000, totalCostUsd: 1.5, requestCount: 10 })
  })

  it('usage.byAgent / byModel / byDay all delegate to the right usage methods', async () => {
    const usageApi = mockUsageApi()
    const bridge = createAiBridge({ usageApi: usageApi as never, client: {} as never })
    const baseQuery = {
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { startTime: '2026-04-01', endTime: '2026-04-12' },
    }
    await bridge.usage.byAgent(baseQuery)
    expect(usageApi.getByAgent).toHaveBeenCalled()
    await bridge.usage.byModel(baseQuery)
    expect(usageApi.getByModel).toHaveBeenCalled()
    await bridge.usage.byDay(baseQuery)
    expect(usageApi.getTrend).toHaveBeenCalled()
  })

  // The dashboard filters by agent and provider. Each panel used to receive a
  // different subset, so the KPI cards, the trend and the breakdowns described
  // different rows on the same screen.
  it('usage reads forward the agent and provider filters to every panel that takes them', async () => {
    const usageApi = mockUsageApi()
    usageApi.getCostSummary.mockResolvedValueOnce({ totalCostUsd: 0 })
    const bridge = createAiBridge({ usageApi: usageApi as never, client: {} as never })
    const q = {
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { startTime: '2026-04-01T00:00:00.000Z', endTime: '2026-04-12T00:00:00.000Z', agentId: 'agent-7', provider: 'OpenAI' },
    }
    await bridge.usage.summary(q)
    await bridge.usage.byDay(q)
    await bridge.usage.byAgent(q)
    await bridge.usage.byModel(q)
    await bridge.usage.costSummary(q)

    expect(usageApi.getSummary).toHaveBeenCalledWith(expect.objectContaining({ agentId: 'agent-7', provider: 'OpenAI' }))
    expect(usageApi.getTrend).toHaveBeenCalledWith(expect.objectContaining({ agentId: 'agent-7', provider: 'OpenAI' }))
    expect(usageApi.getByAgent).toHaveBeenCalledWith(
      '2026-04-01T00:00:00.000Z', '2026-04-12T00:00:00.000Z', 20,
      expect.objectContaining({ agentId: 'agent-7', provider: 'OpenAI' }),
    )
    expect(usageApi.getByModel).toHaveBeenCalledWith('2026-04-01T00:00:00.000Z', '2026-04-12T00:00:00.000Z', 'OpenAI', 'agent-7')
    expect(usageApi.getCostSummary).toHaveBeenCalledWith('2026-04-01T00:00:00.000Z', '2026-04-12T00:00:00.000Z', 'OpenAI', undefined, 'agent-7')
  })

  // A cleared date picker went out as '' and every endpoint (non-nullable
  // DateTime) answered 400; the trend skipped its request and drew nothing.
  it('usage reads send a cleared date as an open bound, never as an empty string', async () => {
    const usageApi = mockUsageApi()
    const bridge = createAiBridge({ usageApi: usageApi as never, client: {} as never })
    const q = { pageIndex: 1, pageSize: 20, searchText: '', filters: { startTime: undefined, endTime: '', agentId: '  ' } }
    await bridge.usage.summary(q)
    await bridge.usage.byDay(q)
    await bridge.usage.byAgent(q)

    const summaryQuery = usageApi.getSummary.mock.calls[0]?.[0] as { startTime: string; endTime: string; agentId?: string }
    expect(summaryQuery.startTime).toBe(new Date(0).toISOString())
    expect(Number.isNaN(Date.parse(summaryQuery.endTime))).toBe(false)
    expect(summaryQuery.agentId).toBeUndefined()
    expect(usageApi.getTrend).toHaveBeenCalledTimes(1)
    const [start, end] = usageApi.getByAgent.mock.calls[0] as unknown as [string, string]
    expect(start).not.toBe('')
    expect(end).not.toBe('')
  })

  it('knowledge.fetch handles paged-list shape from kb api', async () => {
    const kbApi = mockKnowledgeBaseApi()
    const bridge = createAiBridge({ knowledgeBaseApi: kbApi as never, client: {} as never })
    const result = await bridge.knowledge.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: {},
    })
    expect(kbApi.getList).toHaveBeenCalledWith({ pageIndex: 1, pageSize: 20 })
    expect(result.items).toHaveLength(1)
  })

  it('knowledge.reindex calls kbApi.reindex with the id', async () => {
    const kbApi = mockKnowledgeBaseApi()
    const bridge = createAiBridge({ knowledgeBaseApi: kbApi as never, client: {} as never })
    await bridge.knowledge.reindex('kb1')
    expect(kbApi.reindex).toHaveBeenCalledWith('kb1')
  })

  // The markdown export is a file behind an authenticated admin endpoint. It goes
  // through the client (which sends the token) and comes back as a Blob; a bare URL
  // handed to the browser carries no Authorization header and always got a 401.
  it('threads.exportMarkdown downloads the file through the client', async () => {
    const file = new Blob(['# Thread'], { type: 'text/markdown' })
    const threadApi = { ...mockThreadApi(), exportMarkdown: vi.fn(async () => ok(file)) }
    const bridge = createAiBridge({ threadApi: threadApi as never, client: {} as never })
    await expect(bridge.threads.exportMarkdown('t1')).resolves.toBe(file)
    expect(threadApi.exportMarkdown).toHaveBeenCalledWith('t1')
  })

  it('mcpServers.fetch delegates to mcpApi.getList with paged query + filters', async () => {
    const mcpApi = mockMcpApi()
    const bridge = createAiBridge({ mcpApi: mcpApi as never, client: {} as never })
    const result = await bridge.mcpServers.fetch({
      pageIndex: 2,
      pageSize: 50,
      searchText: 'github',
      filters: { transport: 'streamable-http', isEnabled: true },
    })
    expect(mcpApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({
        pageIndex: 2,
        pageSize: 50,
        skip: 50,
        take: 50,
        keyword: 'github',
        transport: 'streamable-http',
        isEnabled: true,
      }),
    )
    expect(result.items).toHaveLength(1)
    expect(result.items[0]).toMatchObject({ name: 'github-mcp', hasAuthToken: true })
  })

  it('mcpServers.create / update / delete delegate to mcpApi entity methods', async () => {
    const mcpApi = mockMcpApi()
    const bridge = createAiBridge({ mcpApi: mcpApi as never, client: {} as never })
    await bridge.mcpServers.create({
      name: 'my-mcp',
      serverUrl: 'https://x.example.com/mcp',
      transport: 'sse',
      authToken: 'tok',
    } as never)
    expect(mcpApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'my-mcp', authToken: 'tok' }),
    )
    await bridge.mcpServers.update('m1', { description: 'updated' } as never)
    expect(mcpApi.update).toHaveBeenCalledWith('m1', { description: 'updated' })
    await bridge.mcpServers.delete(['m1', 'm2'])
    expect(mcpApi.delete).toHaveBeenCalledTimes(2)
  })

  it('mcpServers.test maps backend test result {success,message,latencyMs} → UI {ok,latency,error}', async () => {
    const mcpApi = mockMcpApi()
    mcpApi.test = vi.fn(async () => ok({ success: false, message: 'Auth token missing', latencyMs: 12 }))
    const bridge = createAiBridge({ mcpApi: mcpApi as never, client: {} as never })
    const result = await bridge.mcpServers.test('m1')
    expect(mcpApi.test).toHaveBeenCalledWith('m1')
    expect(result).toEqual({ ok: false, latency: 12, error: 'Auth token missing' })
  })

  it('mcpServers.test omits error when success=true', async () => {
    const mcpApi = mockMcpApi()
    const bridge = createAiBridge({ mcpApi: mcpApi as never, client: {} as never })
    const result = await bridge.mcpServers.test('m1')
    expect(result).toEqual({ ok: true, latency: 7, error: undefined })
  })

  it('usageLimits.fetchUsers pages the metered users and filters by user', async () => {
    const usageLimitApi = mockUsageLimitApi()
    const bridge = createAiBridge({ usageLimitApi: usageLimitApi as never, client: {} as never })
    const result = await bridge.usageLimits.fetchUsers({
      pageIndex: 2,
      pageSize: 50,
      searchText: '',
      filters: { userId: 'u1' },
    })
    expect(usageLimitApi.queryUsers).toHaveBeenCalledWith({ pageIndex: 2, pageSize: 50, skip: 50, take: 50, userId: 'u1' })
    expect(result.items).toHaveLength(1)

    await bridge.usageLimits.fetchUsers({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(usageLimitApi.queryUsers).toHaveBeenLastCalledWith({ pageIndex: 1, pageSize: 20, skip: 0, take: 20, userId: null })
  })

  it('usageLimits writes delegate, and a refused write rejects instead of reading as saved', async () => {
    const usageLimitApi = mockUsageLimitApi()
    const bridge = createAiBridge({ usageLimitApi: usageLimitApi as never, client: {} as never })
    const payload = { sessionLimit: { mode: 'Limited', amountUsd: 5 }, weeklyLimit: { mode: 'Inherit' } } as never

    await bridge.usageLimits.saveDefaults(payload)
    expect(usageLimitApi.saveDefaults).toHaveBeenCalledWith(payload)
    await expect(bridge.usageLimits.saveUser('u1', payload)).resolves.toMatchObject({ hasCustomPolicy: true })
    await bridge.usageLimits.clearUser('u1')
    expect(usageLimitApi.clearUser).toHaveBeenCalledWith('u1')
    await bridge.usageLimits.resetUser('u1')
    expect(usageLimitApi.resetUser).toHaveBeenCalledWith('u1')

    usageLimitApi.saveUser.mockResolvedValueOnce({ succeeded: false, code: 400, message: 'Invalid policy' } as never)
    await expect(bridge.usageLimits.saveUser('u1', payload)).rejects.toThrow('Invalid policy')
    usageLimitApi.resetUser.mockResolvedValueOnce({ succeeded: false, code: 403, message: 'Forbidden' } as never)
    await expect(bridge.usageLimits.resetUser('u1')).rejects.toThrow('Forbidden')
  })

  it('evaluations.fetch / delete / create / runBatch delegate', async () => {
    const evaluationApi = mockEvaluationApi()
    const bridge = createAiBridge({ evaluationApi: evaluationApi as never, client: {} as never })
    await bridge.evaluations.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(evaluationApi.getList).toHaveBeenCalled()
    await bridge.evaluations.delete(['e1'])
    expect(evaluationApi.delete).toHaveBeenCalledWith('e1')

    await bridge.evaluations.create({
      agentId: 'a1',
      cases: [{ input: 'hi', expectedOutput: 'hello' }],
    })
    expect(evaluationApi.create).toHaveBeenCalledWith({
      agentId: 'a1',
      cases: [{ input: 'hi', expectedOutput: 'hello' }],
    })

    await bridge.evaluations.runBatch({
      targets: [{ agentId: 'a1' }],
      cases: [{ input: 'hi' }],
    })
    expect(evaluationApi.runBatch).toHaveBeenCalled()
  })

  it('evaluations.getDetail delegates to evaluationApi.getById', async () => {
    const evaluationApi = mockEvaluationApi()
    evaluationApi.getById.mockResolvedValueOnce({
      id: 'e1',
      agentId: 'a1',
      caseCount: 2,
      passedCount: 2,
      averageScore: 1.0,
      status: 'Completed',
      duration: '1s',
      creationTime: '2026-04-14T00:00:00Z',
      resultsJson: '{"cases":[]}',
    } as never)
    const bridge = createAiBridge({ evaluationApi: evaluationApi as never, client: {} as never })
    const result = await bridge.evaluations.getDetail('e1')
    expect(evaluationApi.getById).toHaveBeenCalledWith('e1')
    expect(result.id).toBe('e1')
    expect(result.resultsJson).toContain('cases')
  })
})
