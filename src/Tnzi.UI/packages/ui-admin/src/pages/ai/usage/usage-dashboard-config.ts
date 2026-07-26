/**
 * Phase 5 Task 5.9 - UsageDashboard pure-data config.
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
  ProviderCostDto,
  ModelCostDto,
  CostSummaryDto,
  AgentFeedbackStatsDto,
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
 * Default time range - last 30 days. Returned as ISO strings so callers can
 * feed straight into the bridge without further conversion.
 */
export function defaultDateRange(now: Date = new Date()): { startTime: string; endTime: string } {
  const end = now
  const start = new Date(end.getTime() - 30 * 24 * 60 * 60 * 1000)
  return { startTime: start.toISOString(), endTime: end.toISOString() }
}

/**
 * Top-N selector for agent rows by cost. Pure function - easy to unit test
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

// ---- Cost breakdown helpers ----------------------------------------------

/** Empty CostSummary placeholder so the page never reads off `null`. */
export const EMPTY_COST_SUMMARY: CostSummaryDto = {
  totalCostUsd: 0,
  totalRequests: 0,
  totalInputTokens: 0,
  totalOutputTokens: 0,
  averageCostPerRequest: 0,
  byProvider: [],
  byModel: [],
}

/** Providers sorted by spend descending (drives the cost table + pie). */
export function sortProviderCosts(rows: ProviderCostDto[]): ProviderCostDto[] {
  return [...rows].sort((a, b) => b.totalCostUsd - a.totalCostUsd)
}

/** Models belonging to a provider, sorted by spend descending. Used to
 *  populate the expandable `byModel` rows under each provider. The backend
 *  ships a FLAT `byModel` array on CostSummary (not nested under each
 *  provider), so we filter by `provider` here. */
export function modelCostsForProvider(
  models: ModelCostDto[],
  provider: string,
): ModelCostDto[] {
  return models
    .filter((m) => m.provider === provider)
    .sort((a, b) => b.totalCostUsd - a.totalCostUsd)
}

// ---- Agent feedback helpers ----------------------------------------------

/** Naive progress-bar status from a positive-feedback rate (0-1 or 0-100). */
export function feedbackBarStatus(positiveRate: number): 'success' | 'warning' | 'error' {
  const pct = positiveRate <= 1 ? positiveRate * 100 : positiveRate
  if (pct >= 80) return 'success'
  if (pct >= 50) return 'warning'
  return 'error'
}

/** Normalise a 0-1 OR 0-100 positive-rate into an integer percentage. */
export function toPercent(rate: number): number {
  const pct = rate <= 1 ? rate * 100 : rate
  return Math.round(pct)
}

/** Flatten a tag-distribution record into sorted `{ tag, count }` rows so the
 *  template can `v-for` them into NTag chips without re-sorting inline. */
export function tagDistributionEntries(
  dist: Record<string, number> | null | undefined,
): Array<{ tag: string; count: number }> {
  if (!dist) return []
  return Object.entries(dist)
    .map(([tag, count]) => ({ tag, count }))
    .sort((a, b) => b.count - a.count)
}

/** Feedback rows sorted by total-rated descending so the busiest agents lead. */
export function sortFeedbackStats(rows: AgentFeedbackStatsDto[]): AgentFeedbackStatsDto[] {
  return [...rows].sort((a, b) => b.totalRated - a.totalRated)
}

/**
 * i18n key catalog - populated as comments only. Locale entries are added in
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
 * admin.modules.ai.usageDashboard.tabs.{overview,cost,feedback,logs}
 * admin.modules.ai.usageDashboard.cost.{title,totalCost,byProvider,provider,share,requests,model,empty}
 * admin.modules.ai.usageDashboard.feedback.{title,agent,positiveRate,totalRated,tags,empty}
 * admin.modules.ai.usageDashboard.logs.{title,time,operation,model,agent,tokens,cost,duration,result,success,failed,empty}
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
  'tabs.overview',
  'tabs.cost',
  'tabs.feedback',
  'tabs.logs',
  'cost.title',
  'cost.totalCost',
  'cost.byProvider',
  'cost.provider',
  'cost.share',
  'cost.requests',
  'cost.model',
  'cost.empty',
  'feedback.title',
  'feedback.agent',
  'feedback.positiveRate',
  'feedback.totalRated',
  'feedback.tags',
  'feedback.empty',
  'logs.title',
  'logs.time',
  'logs.operation',
  'logs.model',
  'logs.agent',
  'logs.tokens',
  'logs.cost',
  'logs.duration',
  'logs.result',
  'logs.success',
  'logs.failed',
  'logs.empty',
] as const
