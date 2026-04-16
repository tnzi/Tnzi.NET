import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.2 — AgentList canonical page integration test.
 *
 * Mocks the ai-bridge module (NOT @tnzi/core/services/ai directly — the
 * Phase 3 convention is to mock at the bridge boundary so pages remain
 * decoupled from the underlying API factory shapes). Mirrors the
 * UserManagement.test.ts pattern.
 *
 * Stub naming: Naive UI components register both with and without the `N`
 * prefix. Vue Test Utils stubs match the component's registered name; the
 * Phase 2b/3 convention is to stub by the un-prefixed name (`DataTable`,
 * not `NDataTable`) — see Phase 2b stub-without-N-prefix gotcha doc.
 */
vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agents: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'a1',
            name: 'Writer',
            description: 'Generates marketing copy',
            provider: 'openai',
            model: 'gpt-4o',
            isEnabled: true,
            qualityTier: 2,
            latencyTier: 1,
            costTier: 2,
            executionMode: 0,
            creationTime: '2026-04-01T00:00:00Z',
          },
          {
            id: 'a2',
            name: 'Reviewer',
            description: 'Reviews drafts',
            provider: 'anthropic',
            model: 'claude-3-5-sonnet',
            isEnabled: false,
            qualityTier: 3,
            latencyTier: 2,
            costTier: 3,
            executionMode: 0,
            creationTime: '2026-04-02T00:00:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({ id: 'a3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
    },
    // The bridge surface has 13 sub-contracts; AgentList only exercises `agents`.
    // The other 12 are not referenced by this page so they don't need stubs.
  }),
}))

import AgentList from '../../src/pages/ai/agents/AgentList.vue'

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

describe('AgentList page (Phase 5 canonical AI page)', () => {
  // AgentList consumes translatePageKey() which reads useAdminAppStore()
  // (a Pinia store) for the active locale. Mounting without an active Pinia
  // throws synchronously in setup, so each test gets a fresh pinia.
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('mounts, fetches agents on mount, and displays rows', async () => {
    const wrapper = mount(AgentList, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(AgentList, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('page title contains "Agents"', async () => {
    const wrapper = mount(AgentList, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Agents')
  })
})
