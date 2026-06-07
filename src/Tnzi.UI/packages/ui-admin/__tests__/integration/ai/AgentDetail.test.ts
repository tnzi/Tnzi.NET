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

const fetchAgentMock = vi.fn(async () => ({
  items: [
    {
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
    },
  ],
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

const fetchPersonasMock = vi.fn(async () => ({
  items: [
    { id: 'persona-1', name: 'Sales rep', slug: 'sales-rep', content: 'You speak with empathy.', isSystem: false },
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

const routerReplaceMock = vi.fn(async () => undefined)

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agents: {
      fetch: fetchAgentMock,
      create: vi.fn(),
      update: updateMock,
      delete: vi.fn(),
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
    },
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
    fetchAgentMock.mockClear()
    fetchRunsMock.mockClear()
    fetchPersonasMock.mockClear()
    fetchProvidersMock.mockClear()
    updateMock.mockClear()
    routerReplaceMock.mockClear()
  })

  it('mounts with TDetailLayout tabs and fetches the agent by route id', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()

    expect(fetchAgentMock).toHaveBeenCalledTimes(1)
    const queryArg = fetchAgentMock.mock.calls[0]?.[0] as { filters: { id: string } }
    expect(queryArg.filters.id).toBe('agent-1')

    // Agent name appears in the #title slot
    expect(wrapper.text()).toContain('Writer')

    // TDetailLayout renders tab panes — 5 sections total
    const tabs = wrapper.findAll('.n-tab-pane, [role="tab"]')
    // Section nav tabs are rendered by NTabs
    expect(wrapper.find('.t-detail-layout').exists()).toBe(true)
  })

  it('loads providers, personas, and recent runs in parallel', async () => {
    mount(AgentDetail)
    await flushPromises()
    expect(fetchProvidersMock).toHaveBeenCalledTimes(1)
    expect(fetchPersonasMock).toHaveBeenCalledTimes(1)
    expect(fetchRunsMock).toHaveBeenCalledTimes(1)
    const runsQuery = fetchRunsMock.mock.calls[0]?.[0] as { filters: { agentId: string } }
    expect(runsQuery.filters.agentId).toBe('agent-1')
  })

  it('renders error banner when fetch rejects', async () => {
    fetchAgentMock.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mount(AgentDetail)
    await flushPromises()
    expect(wrapper.text()).toContain('boom')
    // The error div is present; no NCard section panels are shown
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('has 5 sections: identity, provider, persona, tools, runs', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()
    // The component exposes sections as a const array — verify via the rendered
    // TDetailLayout which passes sections to NTabs as tab panes.
    // NTabs renders each pane with the section key as the name attribute.
    const html = wrapper.html()
    // All 5 section keys appear in the rendered output (as tab pane names or aria attrs)
    expect(html).toContain('identity')
    expect(html).toContain('provider')
    expect(html).toContain('persona')
    expect(html).toContain('tools')
    expect(html).toContain('runs')
  })
})
