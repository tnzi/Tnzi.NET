import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.9 — UsageDashboard analytics page test.
 *
 * Lessons applied:
 *   - 1: setActivePinia in beforeEach (translatePageKey reads admin app store)
 *   - 12: bridge mocked at the boundary, NOT @tnzi/core directly
 *   - 17: partial-failure tolerance — Promise.allSettled fan-out, surface
 *         per-source error banner without losing the successful cards
 */
const summaryMock = vi.fn(async () => ({
  totalTokens: 12345,
  totalCostUsd: 0.4567,
  requestCount: 89,
}))

const byAgentMock = vi.fn(async () => [
  {
    agentId: 'a1',
    agentName: 'Writer',
    totalRequests: 30,
    totalInputTokens: 1000,
    totalOutputTokens: 2000,
    totalTokens: 3000,
    averageDurationMs: 120,
    successRate: 1,
    totalEstimatedCostUsd: 0.25,
  },
  {
    agentId: 'a2',
    agentName: 'Coder',
    totalRequests: 59,
    totalInputTokens: 4000,
    totalOutputTokens: 5000,
    totalTokens: 9000,
    averageDurationMs: 200,
    successRate: 0.95,
    totalEstimatedCostUsd: 0.21,
  },
])

const byModelMock = vi.fn(async () => [
  {
    provider: 'openai',
    model: 'gpt-4o',
    totalRequests: 50,
    totalInputTokens: 3000,
    totalOutputTokens: 4000,
    totalTokens: 7000,
    averageDurationMs: 150,
    totalEstimatedCostUsd: 0.4,
  },
])

const byDayMock = vi.fn(async () => [
  {
    period: '2026-04-12',
    periodStart: '2026-04-12T00:00:00Z',
    totalRequests: 40,
    successfulRequests: 39,
    totalInputTokens: 2000,
    totalOutputTokens: 3000,
    totalTokens: 5000,
    averageDurationMs: 130,
    totalEstimatedCostUsd: 0.2,
  },
  {
    period: '2026-04-13',
    periodStart: '2026-04-13T00:00:00Z',
    totalRequests: 49,
    successfulRequests: 48,
    totalInputTokens: 2500,
    totalOutputTokens: 3500,
    totalTokens: 6000,
    averageDurationMs: 140,
    totalEstimatedCostUsd: 0.25,
  },
])

const costSummaryMock = vi.fn(async () => ({
  totalCostUsd: 0.4567,
  totalRequests: 89,
  totalInputTokens: 5000,
  totalOutputTokens: 7000,
  averageCostPerRequest: 0.0051,
  byProvider: [
    { provider: 'openai', totalCostUsd: 0.4, totalRequests: 50, costPercentage: 87.6 },
    { provider: 'anthropic', totalCostUsd: 0.0567, totalRequests: 39, costPercentage: 12.4 },
  ],
  byModel: [
    {
      provider: 'openai',
      model: 'gpt-4o',
      totalCostUsd: 0.4,
      totalRequests: 50,
      totalInputTokens: 3000,
      totalOutputTokens: 4000,
      costPercentage: 87.6,
    },
  ],
}))

const feedbackStatsMock = vi.fn(async () => [
  {
    agentId: 'a1',
    agentName: 'Writer',
    totalRated: 20,
    positiveCount: 18,
    negativeCount: 2,
    positiveRate: 0.9,
    tagDistribution: { helpful: 12, accurate: 6 },
  },
  {
    agentId: 'a2',
    agentName: 'Coder',
    totalRated: 10,
    positiveCount: 4,
    negativeCount: 6,
    positiveRate: 0.4,
    tagDistribution: { slow: 5 },
  },
])

const getLogsMock = vi.fn(async () => ({
  items: [
    {
      id: 'log-1',
      agentId: 'a1',
      threadId: null,
      provider: 'openai',
      model: 'gpt-4o',
      operationType: 'chat',
      inputTokens: 100,
      outputTokens: 200,
      totalTokens: 300,
      durationMs: 1200,
      isSuccess: true,
      errorMessage: null,
      creationTime: '2026-04-13T10:00:00Z',
      estimatedCostUsd: 0.012,
      cachedInputTokens: 0,
      cacheCreationTokens: 0,
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
}))

const byProviderMock = vi.fn(async () => [
  {
    provider: 'openai',
    totalRequests: 50,
    totalInputTokens: 3000,
    totalOutputTokens: 4000,
    totalTokens: 7000,
    averageDurationMs: 150,
    totalEstimatedCostUsd: 0.4,
  },
])

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    usage: {
      summary: summaryMock,
      byAgent: byAgentMock,
      byModel: byModelMock,
      byDay: byDayMock,
      costSummary: costSummaryMock,
      feedbackStats: feedbackStatsMock,
      getLogs: getLogsMock,
      byProvider: byProviderMock,
    },
  }),
}))

// UsageDashboard now wraps TDashboardPage → useEcharts → useTheme. Provide
// a minimal theme stub so tests can mount without installing @tnzi/ui plugin.
vi.mock('@tnzi/ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tnzi/ui')
  return {
    ...actual,
    useTheme: () => ({
      settings: { value: { mode: 'light', colors: { primary: '#646cff' } } },
      isDark: { value: false },
      resolvedMode: { value: 'light' },
      setColor: vi.fn(),
      setMode: vi.fn(),
      reset: vi.fn(),
      toggleTheme: vi.fn(),
    }),
  }
})

import UsageDashboard from '../../../src/pages/ai/usage/UsageDashboard.vue'

describe('UsageDashboard page (Phase 5 Task 5.9 analytics)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    summaryMock.mockClear()
    byAgentMock.mockClear()
    byModelMock.mockClear()
    byDayMock.mockClear()
    costSummaryMock.mockClear()
    feedbackStatsMock.mockClear()
    getLogsMock.mockClear()
    byProviderMock.mockClear()
    summaryMock.mockResolvedValue({
      totalTokens: 12345,
      totalCostUsd: 0.4567,
      requestCount: 89,
    })
  })

  it('mounts, fans out the parallel bridge calls, renders stat cards', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    // Overview trio + the 3 new blocks (cost / feedback / logs) all fire once.
    expect(summaryMock).toHaveBeenCalledTimes(1)
    expect(byAgentMock).toHaveBeenCalledTimes(1)
    expect(byModelMock).toHaveBeenCalledTimes(1)
    expect(byDayMock).toHaveBeenCalledTimes(1)
    expect(costSummaryMock).toHaveBeenCalledTimes(1)
    expect(feedbackStatsMock).toHaveBeenCalledTimes(1)
    expect(getLogsMock).toHaveBeenCalledTimes(1)

    // All summary-style calls receive the same default 7-day window filters.
    const q = summaryMock.mock.calls[0]?.[0] as { filters: { startTime: string; endTime: string } }
    expect(q.filters.startTime).toMatch(/^\d{4}-\d{2}-\d{2}T/)
    expect(q.filters.endTime).toMatch(/^\d{4}-\d{2}-\d{2}T/)

    // Stat cards render the formatted values.
    const cards = wrapper.findAll('[data-stat]')
    expect(cards).toHaveLength(3)
    expect(wrapper.text()).toContain('89') // requestCount
    expect(wrapper.text()).toContain('$0.4567') // cost
    expect(wrapper.text()).toContain('12,345') // tokens

    // Per-agent rows
    expect(wrapper.text()).toContain('Writer')
    expect(wrapper.text()).toContain('Coder')
    // Per-model row
    expect(wrapper.text()).toContain('openai/gpt-4o')
    // Trend rows
    expect(wrapper.text()).toContain('2026-04-12')
  })

  it('refresh button re-invokes all the dashboard bridge calls', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    summaryMock.mockClear()
    byAgentMock.mockClear()
    byModelMock.mockClear()
    byDayMock.mockClear()
    costSummaryMock.mockClear()
    feedbackStatsMock.mockClear()
    getLogsMock.mockClear()

    await wrapper.find('[data-test="refresh-btn"]').trigger('click')
    await flushPromises()

    expect(summaryMock).toHaveBeenCalledTimes(1)
    expect(byAgentMock).toHaveBeenCalledTimes(1)
    expect(byModelMock).toHaveBeenCalledTimes(1)
    expect(byDayMock).toHaveBeenCalledTimes(1)
    expect(costSummaryMock).toHaveBeenCalledTimes(1)
    expect(feedbackStatsMock).toHaveBeenCalledTimes(1)
    expect(getLogsMock).toHaveBeenCalledTimes(1)
  })

  it('exposes cost / feedback / log data on the component instance', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      cost: { totalCostUsd: number; byProvider: Array<{ provider: string }> }
      feedback: Array<{ agentId: string; positiveRate: number }>
      logs: Array<{ id: string }>
      logTotal: number
    }

    // Cost summary loaded + providers sorted by spend (openai > anthropic).
    expect(vm.cost.totalCostUsd).toBeCloseTo(0.4567)
    expect(vm.cost.byProvider.map((p) => p.provider)).toEqual(['openai', 'anthropic'])

    // Feedback sorted by totalRated desc (Writer 20 before Coder 10).
    expect(vm.feedback.map((f) => f.agentId)).toEqual(['a1', 'a2'])

    // Logs page loaded with its total count.
    expect(vm.logs).toHaveLength(1)
    expect(vm.logs[0]?.id).toBe('log-1')
    expect(vm.logTotal).toBe(1)
  })

  it('renders the cost / feedback / logs tab content', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    // NTabs renders every pane into the DOM (display toggle, not v-if), so the
    // tab bodies are present without a click.
    const text = wrapper.text()
    // Cost tab — total + per-provider rows.
    expect(wrapper.find('[data-test="cost-tab"]').exists()).toBe(true)
    expect(text).toContain('anthropic')
    // Feedback tab — agent feedback rows keyed by agent id.
    expect(wrapper.find('[data-feedback-agent="a1"]').exists()).toBe(true)
    expect(text).toContain('helpful')
    // Logs tab — usage-log detail row.
    expect(wrapper.find('[data-test="logs-tab"]').exists()).toBe(true)
    expect(text).toContain('chat')
  })

  it('renders inline error banner for failed source while keeping successful cards', async () => {
    byAgentMock.mockRejectedValueOnce(new Error('agent stats unavailable'))
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    // Error banner shows the failed source.
    const banner = wrapper.find('[data-test="error-banner"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('byAgent')
    expect(banner.text()).toContain('agent stats unavailable')

    // Successful cards still render.
    expect(wrapper.text()).toContain('openai/gpt-4o') // byModel succeeded
    expect(wrapper.text()).toContain('2026-04-12') // byDay succeeded
    expect(wrapper.text()).toContain('89') // summary succeeded
  })

  it('renders the page root', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()
    // The page now renders through the TTabsPage container; assert its
    // fallthrough marker + the shared filter bar that drives every tab.
    expect(wrapper.find('[data-test="usage-dashboard"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="filter-bar"]').exists()).toBe(true)
  })
})
