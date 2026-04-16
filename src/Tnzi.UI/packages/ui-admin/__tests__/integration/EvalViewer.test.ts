import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.14 — EvalViewer integration test.
 * Has create (= create-and-run on backend), no update, has delete.
 */
vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    evaluations: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'eval-1',
            agentId: 'agent-001',
            caseCount: 10,
            passedCount: 9,
            averageScore: 0.92,
            status: 1,
            duration: '00:00:42',
            creationTime: '2026-04-10T00:00:00Z',
          },
          {
            id: 'eval-2',
            agentId: 'agent-002',
            caseCount: 5,
            passedCount: 5,
            averageScore: 1.0,
            status: 1,
            duration: '00:00:18',
            creationTime: '2026-04-11T00:00:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({
        id: 'eval-3',
        ...(data as object),
        caseCount: 1,
        passedCount: 1,
        averageScore: 1.0,
        status: 1,
        duration: '00:00:01',
        creationTime: '2026-04-12T00:00:00Z',
      })),
      delete: vi.fn(async () => undefined),
    },
  }),
}))

import EvalViewer from '../../src/pages/ai/evaluations/EvalViewer.vue'

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
    template: '<input class="n-input-stub" :value="value" />',
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
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

describe('EvalViewer page (Phase 5 Task 5.14)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('mounts, fetches evaluation runs, and displays rows', async () => {
    const wrapper = mount(EvalViewer, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal (create-and-run flow)', async () => {
    const wrapper = mount(EvalViewer, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('header note explains create-and-run semantics', async () => {
    const wrapper = mount(EvalViewer, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('create-and-run')
  })
})
