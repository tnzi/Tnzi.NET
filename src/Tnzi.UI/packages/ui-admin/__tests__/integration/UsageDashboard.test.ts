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

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    usage: {
      summary: summaryMock,
      byAgent: byAgentMock,
      byModel: byModelMock,
      byDay: byDayMock,
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

import UsageDashboard from '../../src/pages/ai/usage/UsageDashboard.vue'

describe('UsageDashboard page (Phase 5 Task 5.9 analytics)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    summaryMock.mockClear()
    byAgentMock.mockClear()
    byModelMock.mockClear()
    byDayMock.mockClear()
    summaryMock.mockResolvedValue({
      totalTokens: 12345,
      totalCostUsd: 0.4567,
      requestCount: 89,
    })
  })

  it('mounts, fans out 4 parallel bridge calls, renders stat cards', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    expect(summaryMock).toHaveBeenCalledTimes(1)
    expect(byAgentMock).toHaveBeenCalledTimes(1)
    expect(byModelMock).toHaveBeenCalledTimes(1)
    expect(byDayMock).toHaveBeenCalledTimes(1)

    // All 4 calls receive the same default 7-day window filters.
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

  it('refresh button re-invokes all 4 bridge calls', async () => {
    const wrapper = mount(UsageDashboard)
    await flushPromises()

    summaryMock.mockClear()
    byAgentMock.mockClear()
    byModelMock.mockClear()
    byDayMock.mockClear()

    await wrapper.find('[data-test="refresh-btn"]').trigger('click')
    await flushPromises()

    expect(summaryMock).toHaveBeenCalledTimes(1)
    expect(byAgentMock).toHaveBeenCalledTimes(1)
    expect(byModelMock).toHaveBeenCalledTimes(1)
    expect(byDayMock).toHaveBeenCalledTimes(1)
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
    // Phase 1 rewrite: title is now provided by the NCard filter slot
    // (no dedicated h2). Assert the root container instead.
    expect(wrapper.find('.t-usage-dashboard').exists()).toBe(true)
    expect(wrapper.find('[data-test="usage-dashboard"]').exists()).toBe(true)
  })
})
