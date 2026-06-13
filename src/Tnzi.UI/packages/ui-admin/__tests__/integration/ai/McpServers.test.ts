import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * McpServers integration test — rebuilt three-tab page:
 *   Tab 1 «External Servers» — card grid CRUD over mcpServers (Tnzi as client)
 *   Tab 2 «This Server»       — read-only status (mcp.getStatus/getTools/
 *                               getExposedAgents) + exposed-agent management
 *   Tab 3 «Tool Analytics»    — mcpToolAnalytics.getMostUsedTools ranking
 *
 * Mirrors Personas.test.ts: mock the ai-bridge + client, mount with naive
 * stubs, assert mount fetches + getStatus call + external-server cards render +
 * switching to the analytics tab loads the popularity ranking.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const mcpServerFetch = vi.fn(async () => ({
  items: [
    {
      id: 'mcp1',
      name: 'GitHub MCP',
      serverUrl: 'https://mcp.github.com',
      transport: 'streamable-http',
      authType: 'bearer',
      hasAuthToken: true,
      priority: 10,
      isEnabled: true,
      description: 'Official GitHub MCP server',
      tags: 'official\ngithub',
      creationTime: '2026-04-01T00:00:00Z',
      lastModificationTime: '2026-04-10T00:00:00Z',
    },
    {
      id: 'mcp2',
      name: 'Docs Search',
      serverUrl: 'https://mcp.docs.example.com/sse',
      transport: 'sse',
      authType: 'none',
      hasAuthToken: false,
      priority: 0,
      isEnabled: false,
      description: null,
      tags: null,
      creationTime: '2026-04-02T00:00:00Z',
      lastModificationTime: '2026-04-09T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))
const testMock = vi.fn(async (_id: string) => ({ ok: true, latency: 18 }))

const getStatus = vi.fn(async () => ({
  enabled: true,
  transport: 'http',
  endpoint: '/mcp',
  requireAuthentication: true,
  rateLimitPerTenant: true,
  rateLimitPerMinute: 60,
  exposedAgentCount: 1,
  customToolCount: 3,
  totalToolCount: 12,
}))
const getTools = vi.fn(async () => [
  { name: 'read_file', description: 'Read a file from disk' },
  { name: 'write_file', description: 'Write a file to disk' },
])
const getExposedAgents = vi.fn(async () => ['a1'])
const exposeAgent = vi.fn(async () => undefined)
const removeAgent = vi.fn(async () => undefined)

const agentFetch = vi.fn(async () => ({
  items: [
    { id: 'a1', name: 'Support Bot' },
    { id: 'a2', name: 'Research Agent' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 200,
}))

const getMostUsedTools = vi.fn(async () => [
  { toolName: 'read_file', callCount: 240, successRate: 0.995, avgDurationMs: 12.4 },
  { toolName: 'write_file', callCount: 87, successRate: 0.92, avgDurationMs: 30.1 },
])
const getToolStats = vi.fn(async (toolName: string) => ({
  toolName,
  totalCalls: 240,
  avgDurationMs: 12.4,
  p95DurationMs: 40,
  errorRate: 0.005,
  uniqueCallers: 8,
  firstUsed: '2026-04-01T00:00:00Z',
  lastUsed: '2026-04-10T00:00:00Z',
}))
const getToolErrors = vi.fn(async () => [
  { errorMessage: 'permission denied', count: 2, lastOccurred: '2026-04-09T00:00:00Z' },
])
const cleanup = vi.fn(async () => 5)

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    mcpServers: {
      fetch: mcpServerFetch,
      create: vi.fn(async (data: unknown) => ({ id: 'mcp3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      test: testMock,
    },
    mcp: {
      getStatus,
      getTools,
      getExposedAgents,
      exposeAgent,
      removeAgent,
    },
    mcpToolAnalytics: {
      getMostUsedTools,
      getToolStats,
      getToolErrors,
      cleanup,
    },
    agents: { fetch: agentFetch },
  }),
}))

import McpServers from '../../../src/pages/ai/mcp/McpServers.vue'

const stubs = {
  Tabs: {
    name: 'Tabs',
    props: ['value'],
    emits: ['update:value'],
    // Render every tab pane so we can assert content across tabs without
    // simulating naive's pane-switching.
    template: '<div class="n-tabs-stub"><slot /></div>',
  },
  TabPane: { name: 'TabPane', props: ['name', 'tab'], template: '<div class="n-tab-pane-stub"><slot /></div>' },
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="(data || []).length"></div>',
  },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  InputNumber: { name: 'InputNumber', props: ['value'], template: '<input type="number" class="n-input-number-stub" :value="value" />' },
  Switch: { name: 'Switch', props: ['value'], template: '<button class="n-switch-stub" />' },
  Select: { name: 'Select', props: ['value', 'options'], template: '<select class="n-select-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot name="header-extra" /><slot /></div>' },
  Statistic: { name: 'Statistic', props: ['label', 'value'], template: '<div class="n-statistic-stub">{{ value }}<slot /></div>' },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Alert: { name: 'Alert', template: '<div class="n-alert-stub"><slot /></div>' },
  Spin: { name: 'Spin', props: ['show'], template: '<div class="n-spin-stub"><slot /></div>' },
  Descriptions: { name: 'Descriptions', template: '<div class="n-descriptions-stub"><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', props: ['label'], template: '<div class="n-descriptions-item-stub"><slot /></div>' },
  Drawer: {
    name: 'Drawer',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-drawer-stub"><slot /></div>',
  },
  DrawerContent: { name: 'DrawerContent', props: ['title'], template: '<div class="n-drawer-content-stub"><slot /></div>' },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

describe('McpServers page (three-tab MCP control surface)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mcpServerFetch.mockClear()
    testMock.mockClear()
    getStatus.mockClear()
    getTools.mockClear()
    getExposedAgents.mockClear()
    getMostUsedTools.mockClear()
    getToolStats.mockClear()
    agentFetch.mockClear()
    cleanup.mockClear()
  })

  it('mounts and fetches external servers + this-server status + analytics', async () => {
    mount(McpServers, { global: { stubs } })
    await flushPromises()
    expect(mcpServerFetch).toHaveBeenCalledTimes(1)
    expect(getStatus).toHaveBeenCalledTimes(1)
    expect(getMostUsedTools).toHaveBeenCalledTimes(1)
  })

  it('renders one card per external MCP server', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
    expect(wrapper.text()).toContain('GitHub MCP')
    expect(wrapper.text()).toContain('Docs Search')
  })

  it('test connection records per-card success state', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { onTestConnection: (id: string) => Promise<void> }
    await vm.onTestConnection('mcp1')
    await flushPromises()
    expect(testMock).toHaveBeenCalledWith('mcp1')
  })

  it('This Server tab maps exposed agent ids to names', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      status: { totalToolCount: number } | null
      exposedAgents: Array<{ agentId: string; name: string }>
    }
    expect(getExposedAgents).toHaveBeenCalledTimes(1)
    expect(agentFetch).toHaveBeenCalledTimes(1)
    expect(vm.status?.totalToolCount).toBe(12)
    expect(vm.exposedAgents).toEqual([{ agentId: 'a1', name: 'Support Bot' }])
  })

  it('analytics tab loads most-used tools ranking', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { popularTools: Array<{ toolName: string }> }
    expect(getMostUsedTools).toHaveBeenCalledWith(20)
    expect(vm.popularTools.map((t) => t.toolName)).toEqual(['read_file', 'write_file'])
  })

  it('opening a tool detail loads stats + errors into the drawer', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openToolDetail: (name: string) => Promise<void>
      toolStats: { toolName: string } | null
    }
    await vm.openToolDetail('read_file')
    await flushPromises()
    expect(getToolStats).toHaveBeenCalledWith('read_file')
    expect(vm.toolStats?.toolName).toBe('read_file')
  })

  it('cleanup prunes old usage records and reloads analytics', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    getMostUsedTools.mockClear()
    const vm = wrapper.vm as unknown as { onCleanup: () => Promise<void> }
    await vm.onCleanup()
    await flushPromises()
    expect(cleanup).toHaveBeenCalledWith(90)
    expect(getMostUsedTools).toHaveBeenCalledTimes(1)
  })
})
