import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * WorkspaceAgents integration test — card grid (TCardPage).
 *
 * The page is read-only: it calls createWorkspaceAgentsBridge().list() which
 * returns a plain WorkspaceAgentDto[]. The test mocks the bridge, mounts the
 * page, and asserts the card markup, scope filtering, and drawer behaviour.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const mockList = vi.fn(async () => [
  {
    agentId: 'agent-global-1',
    name: 'Planner',
    workspaceScope: 'Global',
    description: 'Plans implementation tasks',
    provider: 'openai',
    model: 'gpt-4o',
    domains: ['planning'],
    roles: ['architect'],
    hasPersona: false,
    filePath: '/home/user/.tnzi/agents/planner/AGENT.md',
    instructions: '# Planner\nYou are a planning agent.',
    personaContent: null,
    executionMode: 'sequential',
    toolGroups: ['bash'],
  },
  {
    agentId: 'agent-project-1',
    name: 'Reviewer',
    workspaceScope: 'Project',
    description: 'Reviews code quality',
    provider: null,
    model: null,
    domains: [],
    roles: ['reviewer'],
    hasPersona: true,
    filePath: '/project/.tnzi/agents/reviewer/AGENT.md',
    instructions: '# Reviewer\nReview code carefully.',
    personaContent: 'You are a strict senior engineer.',
    executionMode: null,
    toolGroups: null,
  },
])

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createWorkspaceAgentsBridge: () => ({
    list: mockList,
  }),
}))

import WorkspaceAgents from '../../../src/pages/ai/workspace/WorkspaceAgents.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="data.length"></div>',
  },
  Pagination: {
    name: 'Pagination',
    props: ['page', 'itemCount', 'pageSize'],
    emits: ['update:page', 'update:pageSize'],
    template: '<div class="n-pagination-stub"></div>',
  },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: {
    name: 'Button',
    template: '<button @click="$emit(\'click\')"><slot /></button>',
  },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: {
    name: 'Popover',
    props: ['show'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
}

describe('WorkspaceAgents page (TCardPage card grid)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockList.mockClear()
  })

  it('mounts and fetches agents on mount', async () => {
    mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()
    expect(mockList).toHaveBeenCalledTimes(1)
  })

  it('renders one card per agent after load', async () => {
    const wrapper = mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()
    // Each agent card renders inside .t-workspace-agent-page__agent-card
    const cards = wrapper.findAll('.t-workspace-agent-page__agent-card')
    expect(cards).toHaveLength(2)
  })

  it('card shows agent name', async () => {
    const wrapper = mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Planner')
    expect(wrapper.text()).toContain('Reviewer')
  })

  it('page title contains "Workspace Agents"', async () => {
    const wrapper = mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Workspace Agents')
  })

  it('scope filter triggers a refetch with filtered items', async () => {
    const wrapper = mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()
    expect(mockList).toHaveBeenCalledTimes(1)

    // Trigger scope filter to "Project" via the vm method
    const vm = wrapper.vm as unknown as {
      onScopeChange: (v: string) => void
    }
    vm.onScopeChange('Project')
    await flushPromises()

    // list() should have been called again
    expect(mockList).toHaveBeenCalledTimes(2)
    // Only the Project-scoped agent should appear
    const cards = wrapper.findAll('.t-workspace-agent-page__agent-card')
    expect(cards).toHaveLength(1)
    expect(wrapper.text()).toContain('Reviewer')
    expect(wrapper.text()).not.toContain('Planner')
  })

  it('View button opens the detail drawer for the correct agent', async () => {
    const wrapper = mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openDetail: (row: { agentId: string; name: string }) => void
      detailVisible: boolean
      detail: { agentId: string; name: string } | null
    }
    expect(vm.detailVisible).toBe(false)

    vm.openDetail({ agentId: 'agent-global-1', name: 'Planner' })
    await flushPromises()

    expect(vm.detailVisible).toBe(true)
    expect(vm.detail?.agentId).toBe('agent-global-1')
  })

  it('no create/edit/delete controls are rendered (read-only page)', async () => {
    const wrapper = mount(WorkspaceAgents, { global: { stubs } })
    await flushPromises()
    // TListShell/TCardPage hides these when canCreate/canUpdate/canDelete are false
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(false)
  })
})
