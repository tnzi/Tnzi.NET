import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Personas integration test — card grid (TCardPage) with create/edit modal +
 * detail drawer. The drawer's "used by agents" panel calls bridge.agents.fetch,
 * so both sub-contracts are mocked.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const personaFetch = vi.fn(async () => ({
  items: [
    {
      id: 'p1',
      name: 'Friendly Helper',
      slug: 'friendly-helper',
      content: 'You are a friendly assistant.',
      description: 'Casual conversational persona',
      scope: 1, // ResourceScope.Tenant
      creationTime: '2026-04-01T00:00:00Z',
    },
    {
      id: 'p2',
      name: 'Strict Reviewer',
      slug: 'strict-reviewer',
      content: 'You are a strict code reviewer.',
      description: 'Reviews code with rigor',
      scope: 0, // ResourceScope.System
      creationTime: '2026-04-02T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))
const agentFetch = vi.fn(async () => ({
  items: [{ id: 'a1', name: 'Support Bot', personaId: 'p1' }],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 200,
}))

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    personas: {
      fetch: personaFetch,
      create: vi.fn(async (data: unknown) => ({ id: 'p3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
    },
    agents: { fetch: agentFetch },
  }),
}))

import Personas from '../../../src/pages/ai/personas/Personas.vue'

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

describe('Personas page (TCardPage card grid)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    personaFetch.mockClear()
    agentFetch.mockClear()
  })

  it('mounts and fetches personas on mount', async () => {
    mount(Personas, { global: { stubs } })
    await flushPromises()
    expect(personaFetch).toHaveBeenCalledTimes(1)
  })

  it('renders one card per persona', async () => {
    const wrapper = mount(Personas, { global: { stubs } })
    await flushPromises()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
  })

  it('card shows persona names and title', async () => {
    const wrapper = mount(Personas, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Friendly Helper')
    expect(wrapper.text()).toContain('Strict Reviewer')
    expect(wrapper.text()).toContain('Personas')
  })

  it('openDetail opens the drawer and loads agents using the persona', async () => {
    const wrapper = mount(Personas, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openDetail: (row: { id: string; name: string }) => Promise<void>
      detailVisible: boolean
      detail: { id: string } | null
      relatedAgents: Array<{ id: string }>
    }
    await vm.openDetail({ id: 'p1', name: 'Friendly Helper' })
    await flushPromises()
    expect(vm.detailVisible).toBe(true)
    expect(vm.detail?.id).toBe('p1')
    expect(agentFetch).toHaveBeenCalledTimes(1)
    expect(vm.relatedAgents.map((a) => a.id)).toEqual(['a1'])
  })
})
