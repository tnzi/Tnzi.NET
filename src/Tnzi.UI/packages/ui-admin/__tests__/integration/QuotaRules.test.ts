import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.12 — QuotaRules integration test.
 * Standard CRUD page; mirrors PersonaList.test.ts.
 */
vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    quota: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'q1',
            userId: 'user-001',
            dailyTokenLimit: 100000,
            monthlyTokenLimit: 2000000,
            currentDailyUsage: 5000,
            currentMonthlyUsage: 50000,
            remainingDailyQuota: 95000,
            remainingMonthlyQuota: 1950000,
            dailyUsagePercentage: 0.05,
            monthlyUsagePercentage: 0.025,
            lastResetDate: '2026-04-01T00:00:00Z',
            isEnabled: true,
            warningThreshold: 0.8,
            criticalThreshold: 0.95,
            warningLevel: 0,
            creationTime: '2026-03-01T00:00:00Z',
          },
          {
            id: 'q2',
            userId: 'user-002',
            dailyTokenLimit: 50000,
            monthlyTokenLimit: 1000000,
            currentDailyUsage: 45000,
            currentMonthlyUsage: 800000,
            remainingDailyQuota: 5000,
            remainingMonthlyQuota: 200000,
            dailyUsagePercentage: 0.9,
            monthlyUsagePercentage: 0.8,
            lastResetDate: '2026-04-01T00:00:00Z',
            isEnabled: true,
            warningThreshold: 0.8,
            criticalThreshold: 0.95,
            warningLevel: 1,
            creationTime: '2026-03-01T00:00:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({ id: 'q3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
    },
  }),
}))

import QuotaRules from '../../src/pages/ai/quota/QuotaRules.vue'

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

describe('QuotaRules page (Phase 5 Task 5.12)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('mounts, fetches quotas on mount, and displays rows', async () => {
    const wrapper = mount(QuotaRules, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(QuotaRules, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('page title contains "Quota Rules"', async () => {
    const wrapper = mount(QuotaRules, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Quota Rules')
  })
})
