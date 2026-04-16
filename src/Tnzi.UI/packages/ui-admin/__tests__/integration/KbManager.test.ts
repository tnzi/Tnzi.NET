import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.10 — KbManager integration test.
 * Mirrors AgentList.test.ts plus a 4th reindex-wiring test.
 *
 * KB DTO is intentionally untyped at the bridge boundary
 * (`Record<string, unknown>`) — the underlying backend KnowledgeBase entity
 * is not exported from @tnzi/core/services/ai. Mock rows therefore mirror
 * what the backend likely returns: id/name/description/embeddingModel.
 */
const reindexMock = vi.fn(async (_id: string) => undefined)

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    knowledge: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'kb1',
            name: 'Product Docs',
            description: 'Customer-facing product documentation',
            embeddingModel: 'text-embedding-3-small',
            chunkCount: 1024,
            documentCount: 87,
            lastModificationTime: '2026-04-01T00:00:00Z',
          },
          {
            id: 'kb2',
            name: 'Internal Wiki',
            description: 'Engineering knowledge base',
            embeddingModel: 'text-embedding-3-large',
            chunkCount: 4096,
            documentCount: 312,
            lastModificationTime: '2026-04-02T00:00:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({ id: 'kb3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      reindex: reindexMock,
    },
  }),
}))

import KbManager from '../../src/pages/ai/knowledge/KbManager.vue'

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

describe('KbManager page (Phase 5 Task 5.10)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    reindexMock.mockClear()
  })

  it('mounts, fetches knowledge bases on mount, and displays rows', async () => {
    const wrapper = mount(KbManager, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(KbManager, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('page title contains "Knowledge Bases"', async () => {
    const wrapper = mount(KbManager, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Knowledge Bases')
  })

  it('reindex action calls bridge.knowledge.reindex with the row id and records success', async () => {
    const wrapper = mount(KbManager, { global: { stubs } })
    await flushPromises()
    // Invoke the page-exposed onReindex directly — TCrudPage edit-mode
    // wiring is exercised by the canonical edit/delete tests; here we
    // only need to verify the reindex bridge call + status update.
    // defineExpose unwraps refs at the template/instance boundary, so
    // `vm.reindexStatus` IS the inner value (object | null), not a Ref.
    const vm = wrapper.vm as unknown as {
      onReindex: (id: string) => Promise<void>
      reindexStatus: { kind: string; message: string } | null
    }
    await vm.onReindex('kb1')
    await flushPromises()
    expect(reindexMock).toHaveBeenCalledWith('kb1')
    expect(vm.reindexStatus).not.toBeNull()
    expect(vm.reindexStatus?.kind).toBe('success')
  })
})
