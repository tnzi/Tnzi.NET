<script setup lang="ts">
import { computed, watch } from 'vue'
import { NCard, NGrid, NGi } from 'naive-ui'
import { TCountTo } from '@tnzi/ui'
import { TSvgIcon } from '@tnzi/ui'
import { useEcharts } from '../../headless/useEcharts'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { translatePageKey } from '../../pages/_shared/translate'
import type { EChartsOption } from 'echarts'

/**
 * `TDashboardPage` - opinionated dashboard scaffold: KPI hero row +
 * line chart + pie chart + activity slot. Drop into a route to get an
 * immediately-presentable admin home. All data is consumer-supplied;
 * the component owns layout, animation, and theme reactivity only.
 */
export interface KpiCardGradient {
  /** Start color (top-left). */
  start: string
  /** End color (bottom-right). */
  end: string
}

export interface KpiCard {
  /** Card key (also used for stable list rendering). */
  key: string
  /** Card title (e.g. "Total Users"). */
  title: string
  /** Big number to animate to. */
  value: number
  /** Optional decimal places. */
  decimals?: number
  /** Iconify icon name. */
  icon: string
  /** Color preset for the icon chip (used when `gradient` is unset). */
  tone?: 'primary' | 'info' | 'success' | 'warning' | 'error'
  /**
   * Gradient palette (soybean-style). When supplied the whole card renders
   * as a coloured tile (white text, no icon chip) - mutually exclusive with
   * `tone`, and `delta` colors are remapped to translucent white.
   */
  gradient?: KpiCardGradient
  /** Optional unit prefix shown before the value (e.g. `'$'`). */
  unit?: string
  /** Optional comparison delta - e.g. "+12.4%". */
  delta?: string
  /** Whether the delta is up (positive) or down (negative). */
  deltaTrend?: 'up' | 'down' | 'flat'
}

export interface ChartSeriesPoint {
  name: string
  value: number
}

interface Props {
  /** KPI hero cards. */
  kpis?: KpiCard[]
  /** Line chart series - pass two series for over-time comparison. */
  lineSeries?: Array<{ name: string; data: number[] }>
  /** Line chart x-axis categories. */
  lineCategories?: string[]
  /** Pie chart segments. */
  pieData?: ChartSeriesPoint[]
  /** Line chart title. */
  lineTitle?: string
  /** Pie chart title. */
  pieTitle?: string
  /** Override the "No data" placeholder shown when a chart has no series. */
  emptyText?: string
}

const props = withDefaults(defineProps<Props>(), {
  kpis: () => [],
  lineSeries: () => [],
  lineCategories: () => [],
  pieData: () => [],
  lineTitle: 'Activity',
  pieTitle: 'Distribution',
  emptyText: undefined,
})

const emptyLabel = computed(() => {
  if (props.emptyText) return props.emptyText
  return translatePageKey('', 'admin.common.noData') || 'No data'
})

const lineHasData = computed(
  () => props.lineSeries.length > 0 && props.lineCategories.length > 0,
)
const pieHasData = computed(() => props.pieData.length > 0)

/**
 * NGi `span` string per KPI card. Picks one of three layouts based on
 * how many KPIs we render:
 *  - 4 KPIs → 1/2/4 across xs/sm/md+ (24 / 12 / 6)
 *  - 3 KPIs → 1/3 across xs / md+    (24 / 8)
 *  - 2 KPIs → 1/2 across xs / md+    (24 / 12)
 *  - 1 or 5+ → 1 col on xs, even split on md+ (24 / 24/count rounded down)
 * Dashboards with 6+ KPIs are uncommon - for those we fall back to 6 per
 * row so the cards never become microscopic. */
const kpiSpan = computed<string>(() => {
  const count = props.kpis.length
  if (count === 4) return '24 s:12 m:6'
  if (count === 3) return '24 s:24 m:8'
  if (count === 2) return '24 s:12 m:12'
  if (count >= 5) return '24 s:12 m:6'
  return '24'
})

// Phone (<768px): a left-side vertical pie legend squeezes the pie into a
// sliver. Below the breakpoint the legend moves to a horizontal strip at the
// bottom so the pie keeps its size (see buildPieOption).
const { isSm } = useBreakpoint()

const { containerRef: lineRef } = useEcharts({
  optionFactory: (mode) => buildLineOption(mode),
})
const { containerRef: pieRef, setOption: setPieOption } = useEcharts({
  optionFactory: (mode) => buildPieOption(mode),
})
// useEcharts only rebuilds the option on theme-mode change, so re-run the
// factory when crossing the breakpoint to reposition the legend live.
watch(isSm, () => setPieOption(true))

function buildLineOption(mode: 'light' | 'dark'): EChartsOption {
  const textColor = mode === 'dark' ? '#d6d6d6' : '#444'
  const gridLine = mode === 'dark' ? '#3a3a3a' : '#eee'
  return {
    backgroundColor: 'transparent',
    grid: { left: 36, right: 16, top: 28, bottom: 28 },
    tooltip: { trigger: 'axis' },
    legend: { right: 0, top: 0, textStyle: { color: textColor } },
    xAxis: {
      type: 'category',
      data: props.lineCategories,
      axisLabel: { color: textColor },
      axisLine: { lineStyle: { color: gridLine } },
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: textColor },
      splitLine: { lineStyle: { color: gridLine } },
    },
    series: props.lineSeries.map((s) => ({
      name: s.name,
      data: s.data,
      type: 'line',
      smooth: true,
      symbolSize: 6,
      lineStyle: { width: 2 },
      areaStyle: { opacity: 0.12 },
    })),
  }
}

function buildPieOption(mode: 'light' | 'dark'): EChartsOption {
  const textColor = mode === 'dark' ? '#d6d6d6' : '#444'
  // Phone: horizontal legend pinned to the bottom keeps the pie from being
  // crushed by a left-side vertical legend on narrow screens.
  const legend: EChartsOption['legend'] = isSm.value
    ? { orient: 'horizontal', bottom: 0, left: 'center', textStyle: { color: textColor } }
    : { orient: 'vertical', left: 'left', textStyle: { color: textColor } }
  return {
    backgroundColor: 'transparent',
    tooltip: { trigger: 'item' },
    legend,
    series: [
      {
        type: 'pie',
        radius: ['40%', '70%'],
        // On phones the bottom legend needs vertical room, so nudge the pie
        // center up; desktop keeps the default centered layout.
        center: isSm.value ? ['50%', '42%'] : ['50%', '50%'],
        avoidLabelOverlap: false,
        label: { show: false },
        labelLine: { show: false },
        data: props.pieData,
      },
    ],
  }
}
</script>

<template>
  <div class="t-dashboard-page">
    <!-- Header banner slot - typically a THeaderBanner with greeting + time -->
    <div v-if="$slots.header" class="t-dashboard-page__header">
      <slot name="header" />
    </div>

    <!-- KPI hero row - responsive breakpoints (NGrid `responsive="screen"`
         with per-NGi `span` string overrides the parent `:cols`). At xs/sm
         each card claims the full row (24); at md two per row (12); at lg+
         four per row (6). This replaces the previous `:cols="kpis.length"`
         that forced 4 tiny tiles on phones and 3 (or 2) odd splits on
         dashboards with fewer KPIs. -->
    <NGrid v-if="kpis.length" :x-gap="12" :y-gap="12" responsive="screen" item-responsive cols="24">
      <NGi v-for="kpi in kpis" :key="kpi.key" :span="kpiSpan">
        <NCard
          size="small"
          :class="[
            't-dashboard-page__kpi-card',
            { 't-dashboard-page__kpi-card--gradient': !!kpi.gradient },
          ]"
          :content-style="
            kpi.gradient
              ? {
                  backgroundImage: `linear-gradient(135deg, ${kpi.gradient.start} 0%, ${kpi.gradient.end} 100%)`,
                }
              : undefined
          "
        >
          <div class="t-dashboard-page__kpi-row">
            <div class="t-dashboard-page__kpi-text">
              <div class="t-dashboard-page__kpi-title">{{ kpi.title }}</div>
              <div class="t-dashboard-page__kpi-value">
                <span v-if="kpi.unit" class="t-dashboard-page__kpi-unit">{{ kpi.unit }}</span>
                <TCountTo
                  :end-value="kpi.value"
                  :decimals="kpi.decimals ?? 0"
                  :duration="1200"
                />
              </div>
              <div
                v-if="kpi.delta"
                class="t-dashboard-page__kpi-delta"
                :data-trend="kpi.deltaTrend ?? 'flat'"
              >
                {{ kpi.delta }}
              </div>
            </div>
            <div
              class="t-dashboard-page__kpi-icon"
              :data-tone="kpi.tone ?? 'primary'"
            >
              <TSvgIcon :icon="kpi.icon" :size="kpi.gradient ? 32 : 24" />
            </div>
          </div>
        </NCard>
      </NGi>
    </NGrid>

    <!-- Chart row - span="24 m:24 l:16" for the line chart and
         "24 m:24 l:8" for the pie. Below `lg` the two charts stack so
         each gets the full content width; at `lg+` they sit side-by-side
         in the historical 2:1 split. Previously `:cols="3"` with hard
         spans forced both charts into the 2:1 ratio at every breakpoint
         which made the pie unreadable on phones / tablets. -->
    <NGrid :x-gap="12" :y-gap="12" responsive="screen" item-responsive cols="24" class="t-dashboard-page__chart-row">
      <NGi span="24 m:24 l:16">
        <NCard :title="lineTitle" size="small" class="t-dashboard-page__chart-card">
          <div v-if="lineHasData" ref="lineRef" class="t-dashboard-page__chart" />
          <div v-else class="t-dashboard-page__empty">{{ emptyLabel }}</div>
        </NCard>
      </NGi>
      <NGi span="24 m:24 l:8">
        <NCard :title="pieTitle" size="small" class="t-dashboard-page__chart-card">
          <div v-if="pieHasData" ref="pieRef" class="t-dashboard-page__chart" />
          <div v-else class="t-dashboard-page__empty">{{ emptyLabel }}</div>
        </NCard>
      </NGi>
    </NGrid>

    <!-- Activity / extra slot row -->
    <div v-if="$slots.default" class="t-dashboard-page__extras">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.t-dashboard-page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  /* No padding - the host page (Workbench / UsageDashboard) sits inside
     TAdminContent which already supplies the 12px page-edge gutter. An
     extra padding here was creating a transparent "凹陷" frame around
     the KPI + chart cards because this wrapper has no background of
     its own (the surrounding layout shows through). */
}

.t-dashboard-page__kpi-card {
  /* soybean parity: soft drop shadow on every unbordered card by default,
     not just on hover (matches soybean's home-page card treatment). */
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  transition: transform var(--tnzi-admin-motion-duration-fast, 0.15s) ease,
    box-shadow var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}
.t-dashboard-page__kpi-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-dashboard-page__kpi-card--gradient :deep(.n-card-header) {
  display: none;
}
.t-dashboard-page__kpi-card--gradient :deep(.n-card__content) {
  padding: 18px 20px;
  color: #fff;
}
.t-dashboard-page__kpi-card--gradient .t-dashboard-page__kpi-title,
.t-dashboard-page__kpi-card--gradient .t-dashboard-page__kpi-value,
.t-dashboard-page__kpi-card--gradient .t-dashboard-page__kpi-delta {
  color: rgba(255, 255, 255, 0.96);
}
.t-dashboard-page__kpi-card--gradient .t-dashboard-page__kpi-title {
  color: rgba(255, 255, 255, 0.86);
}
.t-dashboard-page__kpi-card--gradient .t-dashboard-page__kpi-icon {
  background-color: rgba(255, 255, 255, 0.16);
  color: rgba(255, 255, 255, 0.96);
}
.t-dashboard-page__kpi-unit {
  font-size: 18px;
  font-weight: 500;
  margin-right: 2px;
}
.t-dashboard-page__header {
  /* Reset - let the consumer banner style itself end-to-end. */
}

.t-dashboard-page__kpi-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.t-dashboard-page__kpi-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.t-dashboard-page__kpi-title {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-dashboard-page__kpi-value {
  font-size: 28px;
  font-weight: 600;
  line-height: 1.2;
  color: var(--tnzi-base-text);
}
.t-dashboard-page__kpi-delta {
  font-size: 12px;
  font-weight: 500;
}
.t-dashboard-page__kpi-delta[data-trend='up'] {
  color: var(--tnzi-success);
}
.t-dashboard-page__kpi-delta[data-trend='down'] {
  color: var(--tnzi-error);
}
.t-dashboard-page__kpi-delta[data-trend='flat'] {
  color: var(--tnzi-base-text-muted);
}

.t-dashboard-page__kpi-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  flex-shrink: 0;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
}
.t-dashboard-page__kpi-icon[data-tone='info'] {
  background-color: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-dashboard-page__kpi-icon[data-tone='success'] {
  background-color: rgb(var(--tnzi-success-rgb, 24 160 88) / 0.12);
  color: var(--tnzi-success);
}
.t-dashboard-page__kpi-icon[data-tone='warning'] {
  background-color: rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.12);
  color: var(--tnzi-warning);
}
.t-dashboard-page__kpi-icon[data-tone='error'] {
  background-color: rgb(var(--tnzi-error-rgb, 208 48 80) / 0.12);
  color: var(--tnzi-error);
}

.t-dashboard-page__chart-card {
  height: 360px;
  /* soybean parity - drop shadow + 8px radius matching the KPI cards. */
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
.t-dashboard-page__chart {
  width: 100%;
  height: 300px;
}
.t-dashboard-page__empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 300px;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}

/* Mobile: charts shrink + KPI hero font scales down so the dashboard
   stays digestible without horizontal scrolling on iPhone 14 (390px). */
@media (max-width: 640px) {
  .t-dashboard-page {
    padding: 12px;
    gap: 12px;
  }
  .t-dashboard-page__chart-card {
    height: 300px;
  }
  .t-dashboard-page__chart,
  .t-dashboard-page__empty {
    height: 240px;
  }
  .t-dashboard-page__kpi-value {
    font-size: 22px;
  }
  .t-dashboard-page__kpi-icon {
    width: 40px;
    height: 40px;
  }
}

.t-dashboard-page__extras {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
</style>
