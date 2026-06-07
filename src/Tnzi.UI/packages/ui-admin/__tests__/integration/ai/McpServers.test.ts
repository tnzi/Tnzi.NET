import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.11 — McpServers integration test.
 * Mirrors Knowledge.test.ts (CRUD + extra action canonical pattern).
 */
const testMock = vi.fn(async (_id: string) => ({ ok: true, latency: 18 }))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    mcpServers: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'mcp1',
            name: 'GitHub MCP',
            serverUrl: 'https://mcp.github.com',
            transport: 'streamable-http',
            command: null,
            arguments: null,
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
            name: 'Local Filesystem',
            serverUrl: 'stdio://local',
            transport: 'stdio',
            command: 'node ./fs-mcp.js',
            arguments: '--root\n/data',
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
      })),
      create: vi.fn(async (data: unknown) => ({ id: 'mcp3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      test: testMock,
    },
  }),
}))

import McpServers from '../../../src/pages/ai/mcp/McpServers.vue'

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
  Button: {
    name: 'Button',
    template: '<button @click="$emit(\'click\')"><slot /></button>',
  },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template:
      '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: {
    name: 'Popover',
    props: ['show'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

describe('McpServers page (Phase 5 Task 5.11)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    testMock.mockClear()
  })

  it('mounts, fetches MCP server registrations on mount, and displays rows', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('page title contains "MCP Server Registrations"', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('MCP Server Registrations')
  })

  it('test connection action calls bridge.mcpServers.test and records success', async () => {
    const wrapper = mount(McpServers, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      onTestConnection: (id: string) => Promise<void>
      testStatus: { kind: string; message: string } | null
    }
    await vm.onTestConnection('mcp1')
    await flushPromises()
    expect(testMock).toHaveBeenCalledWith('mcp1')
    expect(vm.testStatus).not.toBeNull()
    expect(vm.testStatus?.kind).toBe('success')
  })
})
