import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const updateMock = vi.fn(async (id: string, data: unknown) => ({
  id,
  name: 'Writer',
  description: 'Generates marketing copy',
  provider: 'openai',
  model: 'gpt-4o',
  instructions: 'You are a copywriter.',
  isEnabled: true,
  executionMode: 0,
  qualityTier: 2,
  latencyTier: 1,
  costTier: 2,
  creationTime: '2026-04-01T00:00:00Z',
  ...(data as object),
}))

const AGENT_1 = {
  id: 'agent-1',
  name: 'Writer',
  description: 'Generates marketing copy',
  provider: 'openai',
  model: 'gpt-4o',
  instructions: 'You are a copywriter.',
  isEnabled: true,
  executionMode: 0,
  qualityTier: 2,
  latencyTier: 1,
  costTier: 2,
  personaId: null,
  creationTime: '2026-04-01T00:00:00Z',
}
// loadAgent() now uses bridge.agents.getById(id) (exact lookup).
const getByIdMock = vi.fn(async () => ({ ...AGENT_1 }))
const fetchAgentMock = vi.fn(async () => ({
  items: [{ ...AGENT_1 }],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 1,
}))

const fetchRunsMock = vi.fn(async () => ({
  items: [
    { id: 'run-1', status: 'Completed', creationTime: '2026-04-01T00:00:00Z' },
    { id: 'run-2', status: 'Running', creationTime: '2026-04-01T00:01:00Z' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 8,
}))

const getVersionsMock = vi.fn(async () => ({
  items: [
    {
      id: 'ver-2',
      agentId: 'agent-1',
      version: 2,
      changeNote: 'Tuned temperature',
      configSnapshot: '{"temperature":0.5}',
      creationTime: '2026-04-02T00:00:00Z',
    },
    {
      id: 'ver-1',
      agentId: 'agent-1',
      version: 1,
      changeNote: null,
      configSnapshot: '{"temperature":0.7}',
      creationTime: '2026-04-01T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 50,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
}))

const rollbackMock = vi.fn(async (_id: string, version: number) => ({
  id: 'agent-1',
  name: 'Writer',
  description: 'Generates marketing copy',
  provider: 'openai',
  model: 'gpt-4o',
  instructions: 'You are a copywriter.',
  isEnabled: true,
  executionMode: 0,
  qualityTier: 2,
  latencyTier: 1,
  costTier: 2,
  personaId: null,
  creationTime: '2026-04-01T00:00:00Z',
  _rolledBackTo: version,
}))

const validateMock = vi.fn(async () => ({
  agentId: 'agent-1',
  agentName: 'Writer',
  isValid: false,
  checks: [
    { name: 'Provider configured', passed: true, details: 'openai' },
    { name: 'Model resolvable', passed: false, details: 'gpt-4o not found' },
  ],
}))

const configureAbTestMock = vi.fn(async (_id: string, _data: unknown) => ({
  id: 'agent-1',
  name: 'Writer',
  provider: 'openai',
  model: 'gpt-4o',
  isEnabled: true,
  executionMode: 0,
  qualityTier: 2,
  latencyTier: 1,
  costTier: 2,
  personaId: null,
  creationTime: '2026-04-01T00:00:00Z',
}))

const stopAbTestMock = vi.fn(async (_id: string) => ({
  id: 'agent-1',
  name: 'Writer',
  provider: 'openai',
  model: 'gpt-4o',
  isEnabled: true,
  executionMode: 0,
  qualityTier: 2,
  latencyTier: 1,
  costTier: 2,
  personaId: null,
  creationTime: '2026-04-01T00:00:00Z',
}))

const fetchPersonasMock = vi.fn(async () => ({
  items: [
    { id: 'persona-1', name: 'Sales rep', slug: 'sales-rep', content: 'You speak with empathy.', scope: 1 },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 200,
}))

const fetchProvidersMock = vi.fn(async () => ({
  items: [
    { id: 'p1', name: 'openai', providerType: 'openai', priority: 1, isEnabled: true, hasApiKey: true, creationTime: '2026-04-01T00:00:00Z' },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 100,
}))

const getOptionsMock = vi.fn(async () => [
  { id: 'p1', name: 'openai', providerType: 'openai', defaultModel: 'gpt-4o' },
])
const listModelsMock = vi.fn(async () => ({ models: ['gpt-4o', 'gpt-4o-mini'], source: 'fallback' }))
const getToolGroupsMock = vi.fn(async () => [{ name: 'web', toolCount: 2, toolNames: ['web_search', 'web_fetch'] }])
const emptyPage = { items: [], totalCount: 0, pageIndex: 1, pageSize: 200 }
const knowledgeFetchMock = vi.fn(async () => ({ ...emptyPage }))
const skillsFetchMock = vi.fn(async () => ({ ...emptyPage }))
const getMemoryMock = vi.fn(async () => ({ ...emptyPage }))

const routerReplaceMock = vi.fn(async () => undefined)

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agents: {
      fetch: fetchAgentMock,
      create: vi.fn(),
      update: updateMock,
      delete: vi.fn(),
      getById: getByIdMock,
      clone: vi.fn(),
      getVersions: getVersionsMock,
      getVersion: vi.fn(),
      rollbackToVersion: rollbackMock,
      validate: validateMock,
      getHealth: vi.fn(),
      configureAbTest: configureAbTestMock,
      stopAbTest: stopAbTestMock,
      getToolGroups: getToolGroupsMock,
      getMemory: getMemoryMock,
      createMemory: vi.fn(),
      updateMemory: vi.fn(),
      deleteMemory: vi.fn(),
    },
    agentRuns: {
      fetch: fetchRunsMock,
      cancel: vi.fn(),
      tail: vi.fn(),
    },
    personas: {
      fetch: fetchPersonasMock,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
    providers: {
      fetch: fetchProvidersMock,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      test: vi.fn(),
      getOptions: getOptionsMock,
      listModels: listModelsMock,
    },
    knowledge: { fetch: knowledgeFetchMock },
    skills: { fetch: skillsFetchMock },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'agent-1' }, query: {} }),
  useRouter: () => ({ replace: routerReplaceMock }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

import AgentDetail from '../../../src/pages/ai/agents/AgentDetail.vue'

describe('AgentDetail page (TDetailLayout tabs)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getByIdMock.mockClear()
    fetchAgentMock.mockClear()
    fetchRunsMock.mockClear()
    fetchPersonasMock.mockClear()
    fetchProvidersMock.mockClear()
    updateMock.mockClear()
    routerReplaceMock.mockClear()
    getVersionsMock.mockClear()
    rollbackMock.mockClear()
    validateMock.mockClear()
    configureAbTestMock.mockClear()
    stopAbTestMock.mockClear()
    getOptionsMock.mockClear()
    listModelsMock.mockClear()
    getToolGroupsMock.mockClear()
    knowledgeFetchMock.mockClear()
    skillsFetchMock.mockClear()
    getMemoryMock.mockClear()
  })

  it('mounts with TDetailLayout tabs and fetches the agent by route id', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()

    expect(getByIdMock).toHaveBeenCalledTimes(1)
    expect(getByIdMock.mock.calls[0]?.[0]).toBe('agent-1')

    // Agent name appears in the #title slot
    expect(wrapper.text()).toContain('Writer')

    // TDetailLayout renders tab panes — 5 sections total
    const tabs = wrapper.findAll('.n-tab-pane, [role="tab"]')
    // Section nav tabs are rendered by NTabs
    expect(wrapper.find('.t-detail-layout').exists()).toBe(true)
  })

  it('loads provider options, personas, and recent runs in parallel', async () => {
    mount(AgentDetail)
    await flushPromises()
    expect(getOptionsMock).toHaveBeenCalledTimes(1)
    expect(fetchPersonasMock).toHaveBeenCalledTimes(1)
    expect(fetchRunsMock).toHaveBeenCalledTimes(1)
    const runsQuery = fetchRunsMock.mock.calls[0]?.[0] as { filters: { agentId: string } }
    expect(runsQuery.filters.agentId).toBe('agent-1')
  })

  it('renders error banner when fetch rejects', async () => {
    getByIdMock.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mount(AgentDetail)
    await flushPromises()
    expect(wrapper.text()).toContain('boom')
    // The error div is present; no NCard section panels are shown
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('has the Setup/Operations sections incl. persona, knowledge, skills, memory', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    // The component exposes its `sections` config (layout-agnostic — the
    // side/tabs/plain layouts all consume the same array). Verify the full set
    // of section keys rather than scraping the rendered nav, which differs by
    // layout (NMenu labels vs NTabPane names). Identity/Provider/Capabilities
    // are now merged into the single Basic Info page (mirrors Fabrikam BotDetail);
    // Persona is its own rich page.
    const vm = wrapper.vm as unknown as { sections: Array<{ key: string }> }
    const keys = vm.sections.map((s) => s.key)
    expect(keys).toEqual(
      expect.arrayContaining([
        'basicInfo', 'persona', 'skills', 'tools', 'knowledge', 'memory',
        'runs', 'versions', 'abtest', 'validation',
      ]),
    )
    expect(keys).toHaveLength(10)
  })

  it('lazy-loads version history when the versions section activates', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    // Versions are NOT fetched on first paint (lazy section).
    expect(getVersionsMock).not.toHaveBeenCalled()

    // Drive the activeSection change the same way the tab nav would.
    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('versions')
    await flushPromises()

    expect(getVersionsMock).toHaveBeenCalledTimes(1)
    const idArg = getVersionsMock.mock.calls[0]?.[0] as string
    expect(idArg).toBe('agent-1')
    // The version rows render (version number + change note).
    expect(wrapper.text()).toContain('Tuned temperature')
  })

  it('rolls back to a prior version via the bridge', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('versions')
    await flushPromises()

    // Invoke the rollback handler the popconfirm would fire.
    await (wrapper.vm as unknown as { handleRollback: (v: number) => Promise<void> }).handleRollback(1)
    await flushPromises()

    expect(rollbackMock).toHaveBeenCalledTimes(1)
    expect(rollbackMock.mock.calls[0]?.[1]).toBe(1)
    // Detail + versions re-fetched after a successful rollback.
    expect(getByIdMock).toHaveBeenCalledTimes(2)
  })

  it('runs configuration validation when the validation section activates', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    expect(validateMock).not.toHaveBeenCalled()

    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('validation')
    await flushPromises()

    expect(validateMock).toHaveBeenCalledTimes(1)
    expect(validateMock.mock.calls[0]?.[0]).toBe('agent-1')
    // The failing check detail surfaces in the rendered list.
    expect(wrapper.text()).toContain('gpt-4o not found')
  })

  it('configures and stops an A/B test via the bridge', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      handleStartAbTest: () => Promise<void>
      handleStopAbTest: () => Promise<void>
    }
    await vm.handleStartAbTest()
    await flushPromises()
    expect(configureAbTestMock).toHaveBeenCalledTimes(1)
    const abArg = configureAbTestMock.mock.calls[0]?.[1] as {
      versionA: number
      versionB: number
      trafficPercentB: number
    }
    expect(abArg.versionA).toBe(1)
    expect(abArg.versionB).toBe(2)
    expect(abArg.trafficPercentB).toBe(10)

    await vm.handleStopAbTest()
    await flushPromises()
    expect(stopAbTestMock).toHaveBeenCalledTimes(1)
  })
})
