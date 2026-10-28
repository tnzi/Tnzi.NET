import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Usage-limits page: everyone's defaults, the users who used AI (each opens
 * their own limits), and the tenant's cost budget.
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

const window = (window: 'Session' | 'Weekly', usedUsd: number, limitUsd: number | null, exceeded = false) => ({
  window,
  durationMinutes: window === 'Session' ? 300 : 10080,
  limitUsd,
  usedUsd,
  usedPercent: limitUsd ? (usedUsd / limitUsd) * 100 : null,
  startedAt: '2026-10-21T09:00:00Z',
  resetsAt: '2026-10-21T14:00:00Z',
  exceeded,
  warningReached: exceeded,
})

const fetchUsers = vi.fn(async () => ({
  items: [
    { userId: '11111111-1111-1111-1111-111111111111', hasCustomPolicy: true, windows: [window('Session', 4, 10), window('Weekly', 20, 50)], lastUsedAt: '2026-10-21T10:00:00Z' },
    { userId: '22222222-2222-2222-2222-222222222222', hasCustomPolicy: false, windows: [window('Session', 10.2, 10, true), window('Weekly', 30, null)], lastUsedAt: '2026-10-21T09:30:00Z' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

const getUser = vi.fn(async (userId: string) => ({
  userId,
  hasCustomPolicy: true,
  policy: { sessionLimit: { mode: 'Limited', amountUsd: 10 }, weeklyLimit: { mode: 'Inherit', amountUsd: null }, allowedModels: null, defaultModel: null },
  defaults: { sessionLimitUsd: 5, weeklyLimitUsd: 50, allowedModels: [], defaultModel: null },
  effective: { sessionLimitUsd: 10, weeklyLimitUsd: 50, allowedModels: [], defaultModel: null },
  catalog: [],
  usage: { windows: [window('Session', 4, 10), window('Weekly', 20, 50)], warningThreshold: 0.8, checkedAt: '2026-10-21T10:00:00Z', lastUsedAt: '2026-10-21T10:00:00Z' },
}))

const getDefaults = vi.fn(async () => ({
  policy: { sessionLimit: { mode: 'Inherit', amountUsd: null }, weeklyLimit: { mode: 'Inherit', amountUsd: null }, allowedModels: null, defaultModel: null },
  configuration: { sessionLimitUsd: null, weeklyLimitUsd: null, allowedModels: [], defaultModel: null, sessionWindowMinutes: 300, weeklyWindowMinutes: 10080, warningThreshold: 0.8 },
  effective: { sessionLimitUsd: null, weeklyLimitUsd: null, allowedModels: [], defaultModel: null },
  catalog: [],
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  UsageLimitWindow: { Session: 'Session', Weekly: 'Weekly' },
  createAiBridge: () => ({
    usageLimits: {
      getDefaults,
      saveDefaults: vi.fn(),
      getUser,
      saveUser: vi.fn(),
      clearUser: vi.fn(),
      resetUser: vi.fn(),
      fetchUsers,
      getBudgetSummary,
    },
  }),
}))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: {
      getById: vi.fn(async (id: string) => (id.startsWith('1111')
        ? { id, userName: 'alice', email: 'alice@example.com' }
        : { id, userName: 'bob', email: null })),
      fetch: vi.fn(async () => ({ items: [], totalCount: 0 })),
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
  Progress: {
    name: 'Progress',
    props: ['percentage', 'status'],
    template: '<div class="n-progress-stub" :data-percentage="percentage" :data-status="status"></div>',
  },
}

describe('Usage limits page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('shows everyone\'s defaults with the configured windows', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    expect(getDefaults).toHaveBeenCalled()
    expect(wrapper.text()).toContain('Defaults for everyone')
    expect(wrapper.text()).toContain('Session window: 5 hours. Weekly window: 7 days.')
  })

  it('lists the users who used AI by name, with each window and their state', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()

    expect(fetchUsers).toHaveBeenCalled()
    const cards = wrapper.findAll('.t-item-card')
    expect(cards).toHaveLength(2)
    expect(cards[0]!.text()).toContain('alice (alice@example.com)')
    expect(cards[0]!.text()).toContain('Own settings')
    expect(cards[0]!.text()).toContain('$4.00 / $10.00')
    expect(cards[1]!.text()).toContain('bob')
    expect(cards[1]!.text()).toContain('At limit')
    // The unlimited weekly window says so instead of drawing a bar.
    expect(cards[1]!.text()).toContain('Unlimited')
  })

  it("opening a user shows that user's limits", async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()

    await wrapper.findAll('.t-item-card')[0]!.trigger('click')
    await flushPromises()

    expect(getUser).toHaveBeenCalledWith('11111111-1111-1111-1111-111111111111')
    expect(document.body.textContent).toContain('Limits for this user')
  })

  it('renders the budget dashboard KPI cards from getBudgetSummary', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    expect(getBudgetSummary).toHaveBeenCalled()
    const text = wrapper.text()
    expect(text).toContain('$42.50')
    expect(text).toContain('$100.00')
    expect(text).toContain('42.5%')
    const progresses = wrapper.findAll('.n-progress-stub')
    expect(progresses.some((p) => p.attributes('data-status') === 'warning')).toBe(true)
    const tables = wrapper.findAll('.n-data-table-stub')
    expect(tables.length).toBe(1)
    expect(tables[0]?.attributes('data-rows')).toBe('2')
  })

  it('renders an unmeasurable budget as a warning, never as within budget', async () => {
    getBudgetSummary.mockResolvedValueOnce({
      periodStart: '2026-04-01T00:00:00Z',
      periodEnd: '2026-04-30T23:59:59Z',
      currentSpendUsd: 0,
      budgetLimitUsd: 100,
      usagePercentage: 0,
      status: 3,
      byAgent: [],
    })
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Cannot be measured')
    expect(wrapper.text()).not.toContain('Within Budget')
  })

  // A refused or failed budget read is not a month with no spend.
  it('★ a budget that could not be read shows the failure, never "Within Budget, $0.00"', async () => {
    getBudgetSummary.mockRejectedValueOnce(new Error('Forbidden'))
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    const text = wrapper.text()
    expect(text).not.toContain('Within Budget')
    expect(wrapper.find('[data-test="budget-error"]').text()).toContain('Forbidden')
    expect(text).toContain('Unavailable')
  })

  it('page title is "Usage Limits"', async () => {
    const wrapper = mount(Quotas, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Usage Limits')
  })
})
