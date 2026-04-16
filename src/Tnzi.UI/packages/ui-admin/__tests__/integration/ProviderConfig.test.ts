import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.8 — ProviderConfig integration test.
 * Mirrors KbManager.test.ts (CRUD + reindex/test action canonical pattern).
 */
const testMock = vi.fn(async (_id: string) => ({ ok: true, latency: 42 }))

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    providers: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'prov1',
            name: 'OpenAI Prod',
            providerType: 'OpenAI',
            endpoint: 'https://api.openai.com/v1',
            defaultModel: 'gpt-4o-mini',
            priority: 10,
            isEnabled: true,
            hasApiKey: true,
            creationTime: '2026-04-01T00:00:00Z',
            lastModificationTime: '2026-04-10T00:00:00Z',
          },
          {
            id: 'prov2',
            name: 'Local Ollama',
            providerType: 'Ollama',
            endpoint: 'http://localhost:11434',
            defaultModel: 'llama3',
            priority: 0,
            isEnabled: false,
            hasApiKey: false,
            creationTime: '2026-04-02T00:00:00Z',
            lastModificationTime: '2026-04-09T00:00:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({ id: 'prov3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      test: testMock,
    },
  }),
}))

import ProviderConfig from '../../src/pages/ai/providers/ProviderConfig.vue'

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

describe('ProviderConfig page (Phase 5 Task 5.8)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    testMock.mockClear()
  })

  it('mounts, fetches providers on mount, and displays rows', async () => {
    const wrapper = mount(ProviderConfig, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(ProviderConfig, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('page title contains "LLM Providers"', async () => {
    const wrapper = mount(ProviderConfig, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('LLM Providers')
  })

  it('test connection action calls bridge.providers.test and records success', async () => {
    const wrapper = mount(ProviderConfig, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      onTestConnection: (id: string) => Promise<void>
      testStatus: { kind: string; message: string } | null
    }
    await vm.onTestConnection('prov1')
    await flushPromises()
    expect(testMock).toHaveBeenCalledWith('prov1')
    expect(vm.testStatus).not.toBeNull()
    expect(vm.testStatus?.kind).toBe('success')
  })
})
