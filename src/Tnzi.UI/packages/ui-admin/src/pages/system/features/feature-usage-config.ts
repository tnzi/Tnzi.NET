import type { EChartsOption } from 'echarts'
import type {
  FeaturePopularityDto,
  FeatureUsagePeriod,
  FeatureUsageRangeQuery,
  FeatureUsageTrendDto,
} from '../../../services/bridges/feature-bridge'

export const USAGE_TOP_OPTIONS = [10, 20, 50, 100] as const

export const USAGE_PERIOD_OPTIONS: FeaturePopularityPeriodOption[] = [
  { value: 'daily', labelKey: 'usage.period.daily' },
  { value: 'weekly', labelKey: 'usage.period.weekly' },
  { value: 'monthly', labelKey: 'usage.period.monthly' },
]

export interface FeaturePopularityPeriodOption {
  value: FeatureUsagePeriod
  labelKey: string
}

/** Default window: the last 30 days, end-inclusive, as epoch millis for `NDatePicker`. */
export function defaultUsageRange(now = new Date()): [number, number] {
  const end = new Date(now)
  end.setHours(23, 59, 59, 999)
  const start = new Date(now)
  start.setDate(start.getDate() - 29)
  start.setHours(0, 0, 0, 0)
  return [start.getTime(), end.getTime()]
}

/** `[from, to]` epoch millis → the ISO strings the backend filters on (inclusive both ends). */
export function toRangeQuery(range: [number, number] | null): FeatureUsageRangeQuery {
  if (!range) return {}
  return { from: new Date(range[0]).toISOString(), to: new Date(range[1]).toISOString() }
}

/** `enableRate` arrives as a 0-100 percentage; render with one decimal. */
export function formatRate(rate: number | null | undefined): string {
  if (rate == null || !Number.isFinite(rate)) return ''
  return `${rate.toFixed(1)}%`
}

/** The ranking sorted by checks, highest first (the backend already does; guard against a re-sorted copy). */
export function sortPopularity(rows: FeaturePopularityDto[]): FeaturePopularityDto[] {
  return [...rows].sort((a, b) => b.checkCount - a.checkCount)
}

/**
 * Stacked enabled / disabled bars per bucket. Stacking rather than two lines:
 * the question this chart answers is "how often was it checked, and of those
 * how many said yes", and the bar height IS the first number.
 */
export function buildTrendOption(
  trend: FeatureUsageTrendDto[],
  labels: { enabled: string; disabled: string },
): EChartsOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: [labels.enabled, labels.disabled], top: 0 },
    grid: { left: 8, right: 8, top: 32, bottom: 8, containLabel: true },
    xAxis: { type: 'category', data: trend.map((p) => p.period) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      {
        name: labels.enabled,
        type: 'bar',
        stack: 'checks',
        data: trend.map((p) => p.enableCount),
        itemStyle: { color: '#18a058' },
      },
      {
        name: labels.disabled,
        type: 'bar',
        stack: 'checks',
        data: trend.map((p) => p.disableCount),
        itemStyle: { color: '#d9d9d9' },
      },
    ],
  }
}
