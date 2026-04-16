import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.3 — AgentDetail master-detail page test.
 *
 * Lessons applied:
 *   - 1: setActivePinia in beforeEach (translatePageKey reads admin app store)
 *   - 12: bridge mocked at the boundary, NOT @tnzi/core directly
 *   - vue-router useRoute mocked at module level (no real router instance)
 */
const updateMock = vi.fn(async (id: string, data: unknown) => ({
  id,
  name: 'Writer (edited)',
  description: 'Generates marketing copy',
  provider: 'openai',
  model: 'gpt-4o',
  instructions: null,
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
      creationTime: '2026-04-01T00:00:00Z',
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 1,
}))

const fetchRunsMock = vi.fn(async () => ({
  items: [
    { id: 'run-1', status: 'succeeded' },
    { id: 'run-2', status: 'running' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 10,
}))

const fetchSkillsMock = vi.fn(async () => ({
  items: [{ id: 'skill-1', name: 'web-search' }],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
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
    skills: {
      fetch: fetchSkillsMock,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      activate: vi.fn(),
      deactivate: vi.fn(),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'agent-1' } }),
}))

import AgentDetail from '../../src/pages/ai/agents/AgentDetail.vue'

const stubs = {
  Tabs: {
    name: 'Tabs',
    props: ['value'],
    emits: ['update:value'],
    template: '<div class="n-tabs-stub"><slot /></div>',
  },
  TabPane: {
    name: 'TabPane',
    props: ['name', 'tab'],
    template: '<div class="n-tab-pane-stub" :data-name="name"><slot /></div>',
  },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  InputNumber: {
    name: 'InputNumber',
    props: ['value'],
    emits: ['update:value'],
    template: '<input type="number" class="n-input-number-stub" :value="value" />',
  },
  Switch: {
    name: 'Switch',
    props: ['value'],
    emits: ['update:value'],
    template: '<button class="n-switch-stub" />',
  },
  Select: {
    name: 'Select',
    props: ['value', 'options'],
    emits: ['update:value'],
    template: '<select class="n-select-stub" />',
  },
  DatePicker: {
    name: 'DatePicker',
    props: ['value'],
    emits: ['update:value'],
    template: '<input type="date" class="n-date-picker-stub" />',
  },
  RouterLink: {
    name: 'RouterLink',
    props: ['to'],
    template: '<a><slot /></a>',
  },
}

describe('AgentDetail page (Phase 5 Task 5.3 master-detail)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchAgentMock.mockClear()
    fetchRunsMock.mockClear()
    fetchSkillsMock.mockClear()
    updateMock.mockClear()
  })

  it('mounts, fetches the agent by route id, renders header + 4 tab panes', async () => {
    const wrapper = mount(AgentDetail, { global: { stubs } })
    await flushPromises()

    // Lesson 15: single-record fetch via filtered list (no getById on bridge).
    expect(fetchAgentMock).toHaveBeenCalledTimes(1)
    const queryArg = fetchAgentMock.mock.calls[0]?.[0] as { filters: { id: string } }
    expect(queryArg.filters.id).toBe('agent-1')

    expect(wrapper.text()).toContain('Writer')
    const panes = wrapper.findAll('.n-tab-pane-stub')
    expect(panes).toHaveLength(4)
    expect(panes.map((p) => p.attributes('data-name'))).toEqual([
      'overview',
      'versions',
      'runs',
      'skills',
    ])
  })

  it('saves overview edits via bridge.agents.update', async () => {
    const wrapper = mount(AgentDetail, { global: { stubs } })
    await flushPromises()

    await wrapper.find('.t-agent-detail__save').trigger('click')
    await flushPromises()

    expect(updateMock).toHaveBeenCalledTimes(1)
    const [calledId, calledData] = updateMock.mock.calls[0] as [string, Record<string, unknown>]
    expect(calledId).toBe('agent-1')
    // Adapter: only Overview-form fields are sent, NOT executionMode/tiers.
    expect(calledData.name).toBe('Writer')
    expect(calledData.provider).toBe('openai')
    expect('executionMode' in calledData).toBe(false)
  })

  it('renders error banner when fetch rejects', async () => {
    fetchAgentMock.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mount(AgentDetail, { global: { stubs } })
    await flushPromises()

    expect(wrapper.text()).toContain('boom')
    // Tabs should not render in error state.
    expect(wrapper.find('.n-tabs-stub').exists()).toBe(false)
  })

  it('loads recent runs in parallel after agent loads', async () => {
    mount(AgentDetail, { global: { stubs } })
    await flushPromises()
    expect(fetchRunsMock).toHaveBeenCalledTimes(1)
    expect(fetchSkillsMock).toHaveBeenCalledTimes(1)
    const runsQuery = fetchRunsMock.mock.calls[0]?.[0] as { filters: { agentId: string } }
    expect(runsQuery.filters.agentId).toBe('agent-1')
  })
})
