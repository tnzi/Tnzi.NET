/**
 * UsageDashboard pure-data config: stat-card definitions, the default date
 * range, top-N selectors and the cost / feedback helpers. Kept dependency-free
 * so it can be unit-tested without mounting the page.
 */
import type {
  AgentUsageDto,
  ModelUsageDto,
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
