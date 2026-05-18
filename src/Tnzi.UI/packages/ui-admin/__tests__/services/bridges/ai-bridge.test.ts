import { describe, it, expect, vi } from 'vitest'
import { createAiBridge } from '../../../src/services/bridges/ai-bridge'

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
    getExportMarkdownUrl: vi.fn(),
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
  }
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
    test: vi.fn(async () => ({ success: true, message: 'ok', latencyMs: 12 })),
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
    // Legacy hosting endpoints — still on the factory but no longer used by the bridge
    getStatus: vi.fn(async () => ({
      enabled: true,
      transport: 'http',
      endpoint: '/mcp',
      requireAuthentication: true,
      tenantIsolation: true,
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
    test: vi.fn(async () => ({ success: true, message: 'ok', latencyMs: 7 })),
  }
}

function mockQuotaApi() {
  return {
    getQuota: vi.fn(async (userId: string) => ({ userId, dailyTokenLimit: 1000 })),
    getList: vi.fn(async () => pagedList([{ id: 'q1', userId: 'u1', dailyTokenLimit: 1000 }])),
    setQuota: vi.fn(async (d: unknown) => ({ ...(d as object) })),
    resetQuota: vi.fn(),
  }
}

function mockPersonaApi() {
  return {
    getList: vi.fn(async () => pagedList([{ id: 'p1', name: 'Persona' }])),
    getById: vi.fn(),
    getBySlug: vi.fn(),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'p-new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
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
    quotaApi: mockQuotaApi() as never,
    personaApi: mockPersonaApi() as never,
    evaluationApi: mockEvaluationApi() as never,
  })
}

describe('ai-bridge', () => {
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
      'quota',
      'personas',
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

  it('agentRuns.tail rejects (not implemented in core)', async () => {
    const bridge = makeBridge()
    await expect(bridge.agentRuns.tail('run-1')).rejects.toThrow(/not implemented/)
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
    providerApi.test.mockResolvedValueOnce({ success: false, message: 'unreachable', latencyMs: 5 })
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
    mcpApi.test = vi.fn(async () => ({ success: false, message: 'Auth token missing', latencyMs: 12 }))
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

  it('quota.fetch delegates to quotaApi.getList with pagination + filters', async () => {
    const quotaApi = mockQuotaApi()
    const bridge = createAiBridge({ quotaApi: quotaApi as never, client: {} as never })
    const result = await bridge.quota.fetch({
      pageIndex: 2,
      pageSize: 50,
      searchText: '',
      filters: { userId: 'u1', isEnabled: true },
    })
    expect(quotaApi.getList).toHaveBeenCalledWith({
      pageIndex: 2,
      pageSize: 50,
      skip: 50,
      take: 50,
      userId: 'u1',
      isEnabled: true,
    })
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('quota.fetch passes nullish filters when none provided', async () => {
    const quotaApi = mockQuotaApi()
    const bridge = createAiBridge({ quotaApi: quotaApi as never, client: {} as never })
    await bridge.quota.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(quotaApi.getList).toHaveBeenCalledWith({
      pageIndex: 1,
      pageSize: 20,
      skip: 0,
      take: 20,
      userId: null,
      isEnabled: null,
    })
  })

  it('quota.create / update both call setQuota', async () => {
    const quotaApi = mockQuotaApi()
    const bridge = createAiBridge({ quotaApi: quotaApi as never, client: {} as never })
    await bridge.quota.create({ userId: 'u1', dailyTokenLimit: 500 } as never)
    expect(quotaApi.setQuota).toHaveBeenCalled()
    await bridge.quota.update('u1', { dailyTokenLimit: 800 } as never)
    expect(quotaApi.setQuota).toHaveBeenCalledTimes(2)
  })

  it('personas.fetch delegates to personaApi.getList', async () => {
    const personaApi = mockPersonaApi()
    const bridge = createAiBridge({ personaApi: personaApi as never, client: {} as never })
    const result = await bridge.personas.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: {},
    })
    expect(personaApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
  })

  it('evaluations.fetch / delete / create / runBatch delegate; run(id) rejects with descriptive error', async () => {
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

    await expect(bridge.evaluations.run('e1')).rejects.toThrow(/creates and runs in one call/)
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
