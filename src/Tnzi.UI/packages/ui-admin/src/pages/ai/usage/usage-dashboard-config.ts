/**
 * Phase 5 Task 5.9 — UsageDashboard pure-data config.
 *
 * Stat-card definitions, chart option factories, and i18n key catalog.
 * Kept dependency-free so it can be unit-tested without mounting the page.
 *
 * Chart library note: vue-echarts is NOT a dependency of @tnzi/ui-admin in
 * Phase 5. Per dispatch instructions, adding it is scope creep; the dashboard
 * renders top-N data in plain lists. The chart-option factories below are
 * defined as pure data so that a future Phase 6 task can wire them into
 * vue-echarts (or any other library) without touching the page component.
 */
import type {
  AgentUsageDto,
  ModelUsageDto,
  UsageTrendPointDto,
} from '@tnzi/core/services/ai'

/** Bridge UsageSummary shape (re-declared to avoid a runtime import cycle). */
export interface DashboardUsageSummary {
  totalTokens: number
  totalCostUsd: number
  requestCount: number
}

export interface StatCardDefinition {
  /** i18n key under admin.modules.ai.usageDashboard.stats */
  key: 'requests' | 'cost' | 'tokens'
  /** Reader picks the value off the summary record. */
  read: (s: DashboardUsageSummary) => number
  /** Optional formatter for display (USD, integer, etc). */
  format?: (n: number) => string
}

export const STAT_CARDS: StatCardDefinition[] = [
  { key: 'requests', read: (s) => s.requestCount, format: (n) => n.toLocaleString() },
  {
    key: 'cost',
    read: (s) => s.totalCostUsd,
    format: (n) => `$${n.toFixed(4)}`,
  },
  { key: 'tokens', read: (s) => s.totalTokens, format: (n) => n.toLocaleString() },
]

/**
 * Default time range — last 7 days. Returned as ISO strings so callers can
 * feed straight into the bridge without further conversion.
 */
export function defaultDateRange(now: Date = new Date()): { startTime: string; endTime: string } {
  const end = now
  const start = new Date(end.getTime() - 7 * 24 * 60 * 60 * 1000)
  return { startTime: start.toISOString(), endTime: end.toISOString() }
}

/**
 * Top-N selector for agent rows by cost. Pure function — easy to unit test
 * if we ever decide to.
 */
export function topAgentsByCost(rows: AgentUsageDto[], n = 10): AgentUsageDto[] {
  return [...rows]
    .sort((a, b) => b.totalEstimatedCostUsd - a.totalEstimatedCostUsd)
    .slice(0, n)
}

export function topModelsByCost(rows: ModelUsageDto[], n = 10): ModelUsageDto[] {
  return [...rows]
    .sort((a, b) => b.totalEstimatedCostUsd - a.totalEstimatedCostUsd)
    .slice(0, n)
}

/**
 * Chart-option factory placeholders. When vue-echarts lands in Phase 6 these
 * become EChartsOption builders. For now they are documented stubs returning
 * a discriminated marker so the page can pass a "spec" to the placeholder
 * card and switch to a real chart with a one-line edit.
 */
export interface ChartSpec {
  kind: 'line' | 'bar'
  xAxis: string[]
  series: Array<{ name: string; data: number[] }>
}

export function buildTrendChartSpec(points: UsageTrendPointDto[]): ChartSpec {
  return {
    kind: 'line',
    xAxis: points.map((p) => p.period),
    series: [
      { name: 'requests', data: points.map((p) => p.totalRequests) },
      { name: 'tokens', data: points.map((p) => p.totalTokens) },
    ],
  }
}

export function buildAgentBarSpec(rows: AgentUsageDto[]): ChartSpec {
  return {
    kind: 'bar',
    xAxis: rows.map((r) => r.agentName),
    series: [{ name: 'cost', data: rows.map((r) => r.totalEstimatedCostUsd) }],
  }
}

export function buildModelBarSpec(rows: ModelUsageDto[]): ChartSpec {
  return {
    kind: 'bar',
    xAxis: rows.map((r) => `${r.provider}/${r.model}`),
    series: [{ name: 'cost', data: rows.map((r) => r.totalEstimatedCostUsd) }],
  }
}

/**
 * i18n key catalog — populated as comments only. Locale entries are added in
 * Phase 5 cleanup task 5.16. translatePageKey() falls back to the bare key
 * when no translation is registered, so the page stays functional.
 *
 * admin.modules.ai.usageDashboard.title
 * admin.modules.ai.usageDashboard.refresh
 * admin.modules.ai.usageDashboard.loading
 * admin.modules.ai.usageDashboard.errors.partial
 * admin.modules.ai.usageDashboard.filter.startTime
 * admin.modules.ai.usageDashboard.filter.endTime
 * admin.modules.ai.usageDashboard.filter.agent
 * admin.modules.ai.usageDashboard.filter.provider
 * admin.modules.ai.usageDashboard.stats.requests
 * admin.modules.ai.usageDashboard.stats.cost
 * admin.modules.ai.usageDashboard.stats.tokens
 * admin.modules.ai.usageDashboard.charts.trend
 * admin.modules.ai.usageDashboard.charts.byAgent
 * admin.modules.ai.usageDashboard.charts.byModel
 * admin.modules.ai.usageDashboard.charts.placeholder
 */
export const I18N_KEYS = [
  'title',
  'refresh',
  'loading',
  'errors.partial',
  'filter.startTime',
  'filter.endTime',
  'filter.agent',
  'filter.provider',
  'stats.requests',
  'stats.cost',
  'stats.tokens',
  'charts.trend',
  'charts.byAgent',
  'charts.byModel',
  'charts.placeholder',
] as const
