import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Providers integration test - card grid (TCardPage) with create/edit modal +
 * per-card connection test. The bridge's providers sub-contract is mocked:
 * fetch returns 2 rows, test resolves { ok:true, latency:42 }.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const providerFetch = vi.fn(async () => ({
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
}))
const providerTest = vi.fn(async (_id: string) => ({ ok: true, latency: 42 }))

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    providers: {
      fetch: providerFetch,
      create: vi.fn(async (data: unknown) => ({ id: 'prov3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      test: providerTest,
    },
  }),
}))

import Providers from '../../../src/pages/ai/providers/Providers.vue'

const stubs = {
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
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
}

describe('Providers page (TCardPage card grid)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    providerFetch.mockClear()
    providerTest.mockClear()
  })

  it('mounts and fetches providers on mount', async () => {
    mount(Providers, { global: { stubs } })
    await flushPromises()
    expect(providerFetch).toHaveBeenCalledTimes(1)
  })

  it('renders one card per provider', async () => {
    const wrapper = mount(Providers, { global: { stubs } })
    await flushPromises()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
  })

  it('card shows provider names', async () => {
    const wrapper = mount(Providers, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('OpenAI Prod')
    expect(wrapper.text()).toContain('Local Ollama')
  })

  it('testing a provider calls bridge.providers.test and records ok + latency', async () => {
    const wrapper = mount(Providers, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      testOne: (item: { id: string }) => Promise<void>
      testStateOf: (id: string) => { status: string; latency?: number; error?: string }
    }
    await vm.testOne({ id: 'prov1' })
    await flushPromises()
    expect(providerTest).toHaveBeenCalledWith('prov1')
    expect(vm.testStateOf('prov1').status).toBe('ok')
    expect(vm.testStateOf('prov1').latency).toBe(42)
  })
})
