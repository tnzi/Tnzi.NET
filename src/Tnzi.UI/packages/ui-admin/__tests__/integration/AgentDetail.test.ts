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

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
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
  useRoute: () => ({ params: { id: 'agent-1' } }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

import AgentDetail from '../../src/pages/ai/agents/AgentDetail.vue'

describe('AgentDetail page (Tier 3: 4-quadrant editor)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchAgentMock.mockClear()
    fetchRunsMock.mockClear()
    fetchPersonasMock.mockClear()
    fetchProvidersMock.mockClear()
    updateMock.mockClear()
  })

  it('mounts the 4-panel layout and fetches the agent by route id', async () => {
    const wrapper = mount(AgentDetail)
    await flushPromises()

    expect(fetchAgentMock).toHaveBeenCalledTimes(1)
    const queryArg = fetchAgentMock.mock.calls[0]?.[0] as { filters: { id: string } }
    expect(queryArg.filters.id).toBe('agent-1')

    expect(wrapper.text()).toContain('Writer')
    const panels = wrapper.findAll('.t-agent-detail__panel')
    expect(panels).toHaveLength(4)
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
    expect(wrapper.findAll('.t-agent-detail__panel')).toHaveLength(0)
  })
})
