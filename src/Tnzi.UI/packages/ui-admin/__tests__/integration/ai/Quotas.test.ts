import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Quotas integration test — budget cost dashboard + per-user quota CRUD.
 *
 * Asserts:
 *  - the quota rules table still renders its rows;
 *  - the budget dashboard (getBudgetSummary) renders its KPI cards +
 *    per-agent breakdown;
 *  - the create form modal still opens.
 */
const getBudgetSummary = vi.fn(async () => ({
  periodStart: '2026-04-01T00:00:00Z',
  periodEnd: '2026-04-30T23:59:59Z',
  currentSpendUsd: 42.5,
  budgetLimitUsd: 100,
  usagePercentage: 0.425,
  status: 1,
  byAgent: [
    { agentId: 'a1', agentName: 'Support Bot', spendUsd: 30, requestCount: 1200, agentBudgetLimitUsd: 60 },
    { agentId: 'a2', agentName: 'Sales Bot', spendUsd: 12.5, requestCount: 400, agentBudgetLimitUsd: null },
  ],
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
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
      getBudgetSummary,
    },
  }),
}))

import Quotas from '../../../src/pages/ai/quota/Quotas.vue'

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
  Card: {
    name: 'Card',
    props: ['title'],
    template: '<div class="n-card-stub"><slot name="header-extra" /><slot /></div>',
  },
  Statistic: {
    name: 'Statistic',
    props: ['label'],
    template: '<div class="n-statistic-stub"><span class="n-statistic-stub__label">{{ label }}</span><slot /></div>',
  },
  Progress: {
    name: 'Progress',
    props: ['percentage', 'status'],
    template: '<div class="n-progress-stub" :data-percentage="percentage" :data-status="status"></div>',
  },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

describe('Quotas page (budget dashboard + quota CRUD)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getBudgetSummary.mockClear()
  })

  it('mounts, fetches quotas on mount, and displays rows', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    const tables = wrapper.findAll('.n-data-table-stub')
    // The quota rules table is the one bound with the 2 quota rows.
    const quotaTable = tables.find((tw) => tw.attributes('data-rows') === '2')
    expect(quotaTable).toBeTruthy()
  })

  it('renders the budget dashboard KPI cards from getBudgetSummary', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    expect(getBudgetSummary).toHaveBeenCalled()
    // KPI cards render their labels + the formatted spend/limit values.
    const text = wrapper.text()
    expect(text).toContain('$42.50')
    expect(text).toContain('$100.00')
    expect(text).toContain('42.5%')
    // The usage KPI card renders a progress bar reflecting the 42.5% spend,
    // coloured `warning` because the budget status is WarningThreshold (1).
    const progress = wrapper.find('.n-progress-stub')
    expect(progress.exists()).toBe(true)
    expect(progress.attributes('data-status')).toBe('warning')
    // Two data-tables render: the per-agent breakdown (2 agents) + the quota
    // rules table (2 rows) — both via the DataTable stub with data-rows="2".
    const tables = wrapper.findAll('.n-data-table-stub')
    expect(tables.length).toBe(2)
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('page title contains "Quota Rules"', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Quota Rules')
  })
})
