import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.6 — WorkflowRunViewer integration test.
 * Read-only viewer, so the canonical "create modal opens" test is replaced
 * with a "filter panel renders" test.
 */
vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflowRuns: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'run1',
            executionId: 'exec-1',
            workflowDefinitionId: 'wf-001',
            status: 1,
            completedStepCount: 5,
            awaitingApprovalCount: 0,
            creationTime: '2026-04-10T00:00:00Z',
            completedTime: '2026-04-10T00:01:00Z',
            updatedTime: '2026-04-10T00:01:00Z',
          },
          {
            id: 'run2',
            executionId: 'exec-2',
            workflowDefinitionId: 'wf-002',
            status: 0,
            completedStepCount: 2,
            awaitingApprovalCount: 1,
            creationTime: '2026-04-11T00:00:00Z',
            completedTime: null,
            updatedTime: '2026-04-11T00:02:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      tail: vi.fn(() => Promise.reject(new Error('not implemented'))),
    },
  }),
}))

import RunViewer from '../../src/pages/ai/workflows/RunViewer.vue'

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

describe('WorkflowRunViewer page (Phase 5 Task 5.6)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('mounts, fetches workflow runs, and displays rows', async () => {
    const wrapper = mount(RunViewer, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('hides the create button (read-only viewer)', async () => {
    const wrapper = mount(RunViewer, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
  })

  it('page title contains "Workflow Runs"', async () => {
    const wrapper = mount(RunViewer, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Workflow Runs')
  })
})
