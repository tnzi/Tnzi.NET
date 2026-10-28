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
  persona: null,
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
  persona: null,
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
  persona: null,
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
  persona: null,
  creationTime: '2026-04-01T00:00:00Z',
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
const updateMemoryMock = vi.fn(async () => undefined)

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
      updateMemory: updateMemoryMock,
      deleteMemory: vi.fn(),
    },
    agentRuns: {
      fetch: fetchRunsMock,
      cancel: vi.fn(),
      tail: vi.fn(),
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

const cliBindingGetMock = vi.fn(async (_agentId: string): Promise<unknown> => null)
const cliRuntimesListMock = vi.fn(async (): Promise<unknown[]> => [])
vi.mock('../../../src/services/bridges/cli-agent-bridge', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../src/services/bridges/cli-agent-bridge')>()
  return {
    ...original,
    createCliAgentBridge: () => ({
      runtimes: { list: cliRuntimesListMock, providers: vi.fn(async () => []), probe: vi.fn(), update: vi.fn(), remove: vi.fn() },
      bindings: { get: cliBindingGetMock, upsert: vi.fn(), remove: vi.fn() },
      runs: { list: vi.fn(), get: vi.fn(), messages: vi.fn(async () => []), cancel: vi.fn(), streamUrl: vi.fn(() => '') },
    }),
  }
})

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'agent-1' }, query: {} }),
  // `resolve` is part of the real router contract - the page uses it to build
  // its back-target fallback by route NAME (path literals dangle under a
  // custom basePath).
  useRouter: () => ({
    replace: routerReplaceMock,
    resolve: (to: { name: string }) => ({ path: `/admin/${String(to.name).replace(/\./g, '/')}` }),
  }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

import AgentDetail from '../../../src/pages/ai/agents/AgentDetail.vue'
import AgentExecutionPanel from '../../../src/pages/ai/agents/sections/AgentExecutionPanel.vue'

describe('AgentDetail page (TDetailLayout tabs)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getByIdMock.mockClear()
    fetchAgentMock.mockClear()
    fetchRunsMock.mockClear()
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

    // TDetailLayout renders tab panes - 5 sections total
    const tabs = wrapper.findAll('.n-tab-pane, [role="tab"]')
    // Section nav tabs are rendered by NTabs
    expect(wrapper.find('.t-detail-layout').exists()).toBe(true)
  })

  it('loads provider options and recent runs in parallel', async () => {
    mount(AgentDetail)
    await flushPromises()
    expect(getOptionsMock).toHaveBeenCalledTimes(1)
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
    // The component exposes its `sections` config (layout-agnostic - the
    // side/tabs/plain layouts all consume the same array). Verify the full set
    // of section keys rather than scraping the rendered nav, which differs by
    // layout (NMenu labels vs NTabPane names). Identity/Provider/Capabilities
    // are now merged into the single Basic Info page (mirrors a consumer's BotDetail);
    // Persona is its own rich page.
    const vm = wrapper.vm as unknown as { sections: Array<{ key: string }> }
    const keys = vm.sections.map((s) => s.key)
    expect(keys).toEqual(
      expect.arrayContaining([
        'basicInfo', 'persona', 'skills', 'tools', 'knowledge', 'memory',
        // Execution decides HOW this agent runs (built-in pipeline vs an
        // external CLI runtime), so it belongs with Setup, not Operations.
        'execution',
        'runs', 'versions', 'abtest', 'validation',
      ]),
    )
    expect(keys).toHaveLength(11)
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

// An emptied field went out as `null`, which the update reads as "no change":
// the page said "Saved", the old system prompt / forced model stayed stored and
// the field snapped back. Text fields clear with an empty string; the numbers
// cannot be cleared through an update, so the page does not offer it.
describe('AgentDetail basic info - emptied fields', () => {
  type EditVm = {
    edit: Record<string, unknown>
    isDirty: boolean
    handleSave: () => Promise<void>
  }

  beforeEach(() => {
    setActivePinia(createPinia())
    updateMock.mockClear()
    getByIdMock.mockClear()
  })

  it('a cleared description, prompt and model go out as empty strings', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    const vm = wrapper.vm as unknown as EditVm

    vm.edit.description = ''
    vm.edit.instructions = ''
    vm.edit.model = null // the model select's clear button
    await vm.handleSave()

    expect(updateMock).toHaveBeenCalledTimes(1)
    expect(updateMock.mock.calls[0]![1]).toEqual({ description: '', instructions: '', model: '' })
  })

  it('a model that was never set and is cleared again is not a change', async () => {
    getByIdMock.mockResolvedValueOnce({ ...AGENT_1, model: null } as never)
    const wrapper = mount(AgentDetail)
    await flushPromises()
    const vm = wrapper.vm as unknown as EditVm

    vm.edit.model = null
    await flushPromises()
    expect(vm.isDirty).toBe(false)
  })

  it('an emptied temperature or max tokens returns to the stored value', async () => {
    getByIdMock.mockResolvedValueOnce({ ...AGENT_1, temperature: 0.7, maxTokens: 2048 } as never)
    const wrapper = mount(AgentDetail)
    await flushPromises()
    const vm = wrapper.vm as unknown as EditVm

    const numberInputs = wrapper.findAllComponents({ name: 'InputNumber' })
    const temperature = numberInputs.find((c) => c.props('value') === 0.7)!
    const maxTokens = numberInputs.find((c) => c.props('value') === 2048)!
    expect(temperature.props('clearable')).toBeFalsy()
    expect(maxTokens.props('clearable')).toBeFalsy()

    temperature.vm.$emit('update:value', null)
    maxTokens.vm.$emit('update:value', null)
    temperature.vm.$emit('blur', new FocusEvent('blur'))
    maxTokens.vm.$emit('blur', new FocusEvent('blur'))
    await flushPromises()

    expect(vm.edit.temperature).toBe(0.7)
    expect(vm.edit.maxTokens).toBe(2048)
    expect(vm.isDirty).toBe(false)
  })
})

describe('AgentDetail memory - clearing a category', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    updateMemoryMock.mockClear()
  })

  // The update reads null as "no change" and an empty string as "clear".
  it('an emptied category goes out as an empty string', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openMemoryEdit: (row: { id: string; content: string; category: string | null; importance: number }) => void
      memoryModal: { category: string }
      saveMemory: () => Promise<void>
    }
    vm.openMemoryEdit({ id: 'mem-1', content: 'Prefers metric units', category: 'preferences', importance: 0.5 })
    vm.memoryModal.category = ''
    await vm.saveMemory()

    expect(updateMemoryMock).toHaveBeenCalledWith('agent-1', 'mem-1', { content: 'Prefers metric units', category: '', importance: 0.5 })
  })
})

describe('AgentDetail memory and versions - every page, not the first', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getMemoryMock.mockClear()
    getVersionsMock.mockClear()
  })

  // The server clamps PageSize to 100 without saying so: one request for 200
  // returned the first 100 memories, and entries past them could not be
  // reviewed or deleted. Versions stopped at 50.
  function clampedPages<T>(all: T[]) {
    return async (_id: string, query?: { pageIndex?: number; pageSize?: number }) => {
      const size = Math.min(query?.pageSize ?? 10, 100)
      const index = query?.pageIndex ?? 1
      const items = all.slice((index - 1) * size, index * size)
      return {
        items,
        totalCount: all.length,
        pageIndex: index,
        pageSize: size,
        totalPages: Math.ceil(all.length / size),
        hasNextPage: index * size < all.length,
        hasPreviousPage: index > 1,
      }
    }
  }

  it('lists all 150 memories of an agent', async () => {
    const memories = Array.from({ length: 150 }, (_, i) => ({
      id: `mem-${i + 1}`, agentId: 'agent-1', content: `m${i + 1}`, category: null, importance: 0.5,
    }))
    getMemoryMock.mockImplementation(clampedPages(memories) as never)
    const wrapper = mount(AgentDetail)
    await flushPromises()
    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('memory')
    await flushPromises()
    const vm = wrapper.vm as unknown as { memoryRows: unknown[] }
    expect(vm.memoryRows).toHaveLength(150)
    getMemoryMock.mockReset().mockImplementation(async () => ({ ...emptyPage }))
  })

  it('lists all 60 versions of an agent', async () => {
    const versions = Array.from({ length: 60 }, (_, i) => ({
      id: `ver-${60 - i}`, agentId: 'agent-1', version: 60 - i, changeNote: null, configSnapshot: '{}',
      creationTime: '2026-04-01T00:00:00Z',
    }))
    const original = getVersionsMock.getMockImplementation()
    getVersionsMock.mockImplementation(clampedPages(versions) as never)
    const wrapper = mount(AgentDetail)
    await flushPromises()
    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('versions')
    await flushPromises()
    const vm = wrapper.vm as unknown as { versions: unknown[] }
    expect(vm.versions).toHaveLength(60)
    if (original) getVersionsMock.mockImplementation(original)
  })
})

describe('AgentDetail execution - a binding that could not be read', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    cliBindingGetMock.mockClear()
    cliRuntimesListMock.mockClear()
  })

  it('hands the read failure to the panel instead of an empty binding', async () => {
    cliBindingGetMock.mockRejectedValueOnce(new Error('Forbidden'))
    cliRuntimesListMock.mockResolvedValueOnce([{ id: 'rt-1', name: 'Claude Code @ BUILD-01' }])
    const wrapper = mount(AgentDetail)
    await flushPromises()
    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('execution')
    await flushPromises()

    expect(cliBindingGetMock).toHaveBeenCalledWith('agent-1')
    const panel = wrapper.findComponent(AgentExecutionPanel)
    expect(panel.props('loadError')).toBe('Forbidden')
    expect(panel.find('[data-test="cli-execution-load-error"]').exists()).toBe(true)
  })

  it('clears the failure once the binding reads', async () => {
    cliBindingGetMock.mockResolvedValueOnce(null)
    const wrapper = mount(AgentDetail)
    await flushPromises()
    ;(wrapper.vm as unknown as { setSection: (k: string) => void }).setSection('execution')
    await flushPromises()
    expect(wrapper.findComponent(AgentExecutionPanel).props('loadError')).toBeNull()
  })
})
