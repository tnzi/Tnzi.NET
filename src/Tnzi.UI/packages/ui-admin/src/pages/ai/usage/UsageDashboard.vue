<script setup lang="ts">
/**
 * UsageDashboard — Phase 5 Task 5.9 analytics page (rewritten).
 *
 * Routed at /admin/ai/usage. The previous implementation used raw HTML
 * inputs / buttons + sectioned CSS containers with `--t-*` tokens that
 * didn't exist in the theme, so the page rendered without the white
 * NCard chrome every other admin page uses and ignored theme color
 * changes. This rewrite mirrors the `Workbench.vue` layout pattern:
 *
 *   - Filter NCard (white, bordered=false, box-shadow via styles)
 *   - 4 KPI cards via TDashboardPage (gradient tile style)
 *   - 3 chart NCards (trend / by-agent / by-model)
 *
 * Charts continue to consume the option factories in
 * `./usage-dashboard-config.ts`; the inline list fallback is retained
 * for when chart data hasn't loaded yet or when echarts isn't usable.
 */
import { reactive, ref, computed, onMounted } from 'vue'
import {
  NCard, NButton, NForm, NFormItem, NGrid, NGi,
  NDatePicker, NInput, NSpin,
} from 'naive-ui'
import type { EChartsOption } from 'echarts'
import TChartPanel from '../../../components/display/TChartPanel.vue'
import TDashboardPage, {
  type KpiCard,
} from '../../../components/pages/TDashboardPage.vue'
import { TSvgIcon } from '@tnzi/ui'
import { translatePageKey } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TContentPage from '../../../components/layout/TContentPage.vue'
import {
  STAT_CARDS,
  defaultDateRange,
  topAgentsByCost,
  topModelsByCost,
  type DashboardUsageSummary,
  type StatCardDefinition,
} from './usage-dashboard-config'
import type {
  AgentUsageDto,
  ModelUsageDto,
  UsageTrendPointDto,
} from '@tnzi/core/services/ai'

const bridge = createAiBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('ai.usageDashboard', key)

interface DashboardFilters {
  startTime: number | null
  endTime: number | null
  agentId?: string
  provider?: string
}

const initialRange = defaultDateRange()
const filters = reactive<DashboardFilters>({
  startTime: new Date(initialRange.startTime).getTime(),
  endTime: new Date(initialRange.endTime).getTime(),
  agentId: undefined,
  provider: undefined,
})

const summary = ref<DashboardUsageSummary>({ totalTokens: 0, totalCostUsd: 0, requestCount: 0 })
const agentRows = ref<AgentUsageDto[]>([])
const modelRows = ref<ModelUsageDto[]>([])
const trend = ref<UsageTrendPointDto[]>([])

const loading = ref(false)
interface PartialError {
  source: 'summary' | 'byAgent' | 'byModel' | 'byDay'
  message: string
}
const errors = ref<PartialError[]>([])

const topAgents = computed(() => topAgentsByCost(agentRows.value, 10))
const topModels = computed(() => topModelsByCost(modelRows.value, 10))

// KPI cards built from STAT_CARDS config — same source of truth as the
// previous implementation, just rendered through TDashboardPage so the
// gradient tile chrome + count-up animation match Workbench.
const KPI_GRADIENTS: Array<KpiCard['gradient']> = [
  { start: '#ec4786', end: '#b955a4' },
  { start: '#865ec0', end: '#5144b4' },
  { start: '#56cdf3', end: '#719de3' },
  { start: '#fcbc25', end: '#f68057' },
]
const KPI_ICONS: Record<StatCardDefinition['key'], string> = {
  requests: 'mdi:chart-areaspline',
  cost: 'mdi:cash-multiple',
  tokens: 'mdi:counter',
}

const kpiCards = computed<KpiCard[]>(() =>
  STAT_CARDS.map((card: StatCardDefinition, i: number) => ({
    key: card.key,
    title: t(`stats.${card.key}`),
    value: card.read(summary.value),
    icon: KPI_ICONS[card.key] ?? 'mdi:chart-bar',
    gradient: KPI_GRADIENTS[i % KPI_GRADIENTS.length],
    decimals: card.key === 'cost' ? 4 : 0,
    unit: card.key === 'cost' ? '$' : undefined,
  })),
)

// ---- ECharts option builders ---------------------------------------------

const trendOption = computed<EChartsOption>(() => ({
  tooltip: { trigger: 'axis' },
  legend: { data: ['Requests', 'Tokens'], top: 0 },
  grid: { left: 40, right: 40, top: 32, bottom: 24, containLabel: true },
  xAxis: { type: 'category', data: trend.value.map((p) => p.period) },
  yAxis: [
    { type: 'value', name: 'Requests', position: 'left' },
    { type: 'value', name: 'Tokens', position: 'right' },
  ],
  series: [
    {
      name: 'Requests',
      type: 'line',
      smooth: true,
      yAxisIndex: 0,
      data: trend.value.map((p) => p.totalRequests),
      itemStyle: { color: 'var(--tnzi-primary)' },
    },
    {
      name: 'Tokens',
      type: 'line',
      smooth: true,
      yAxisIndex: 1,
      data: trend.value.map((p) => p.totalTokens),
      itemStyle: { color: 'var(--tnzi-success)' },
    },
  ],
}))

const agentOption = computed<EChartsOption>(() => ({
  tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
  grid: { left: 40, right: 24, top: 16, bottom: 32, containLabel: true },
  xAxis: { type: 'category', data: topAgents.value.map((r) => r.agentName), axisLabel: { rotate: 24 } },
  yAxis: { type: 'value', name: 'USD' },
  series: [
    {
      type: 'bar',
      data: topAgents.value.map((r) => Number(r.totalEstimatedCostUsd.toFixed(4))),
      itemStyle: { color: 'var(--tnzi-warning)' },
    },
  ],
}))

const modelOption = computed<EChartsOption>(() => ({
  tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
  grid: { left: 40, right: 24, top: 16, bottom: 32, containLabel: true },
  xAxis: { type: 'category', data: topModels.value.map((r) => `${r.provider}/${r.model}`), axisLabel: { rotate: 24 } },
  yAxis: { type: 'value', name: 'USD' },
  series: [
    {
      type: 'bar',
      data: topModels.value.map((r) => Number(r.totalEstimatedCostUsd.toFixed(4))),
      itemStyle: { color: 'var(--tnzi-error)' },
    },
  ],
}))

function buildQuery() {
  return {
    pageIndex: 1,
    pageSize: 1,
    sortField: '',
    sortOrder: 'asc' as const,
    searchText: '',
    filters: {
      startTime: filters.startTime ? new Date(filters.startTime).toISOString() : undefined,
      endTime: filters.endTime ? new Date(filters.endTime).toISOString() : undefined,
      agentId: filters.agentId,
      provider: filters.provider,
    },
  }
}

async function refresh() {
  loading.value = true
  errors.value = []
  const q = buildQuery()

  const results = await Promise.allSettled([
    bridge.usage.summary(q),
    bridge.usage.byAgent(q),
    bridge.usage.byModel(q),
    bridge.usage.byDay(q),
  ])

  const sources: PartialError['source'][] = ['summary', 'byAgent', 'byModel', 'byDay']
  results.forEach((res, i) => {
    const source = sources[i]!
    if (res.status === 'fulfilled') {
      if (source === 'summary') summary.value = res.value as DashboardUsageSummary
      else if (source === 'byAgent') agentRows.value = res.value as AgentUsageDto[]
      else if (source === 'byModel') modelRows.value = res.value as ModelUsageDto[]
      else trend.value = res.value as UsageTrendPointDto[]
    } else {
      const err = res.reason as Error
      errors.value.push({ source, message: err?.message ?? 'failed' })
    }
  })

  loading.value = false
}

onMounted(() => {
  void refresh()
})

defineExpose({ refresh, filters, summary, agentRows, modelRows, trend, errors })
</script>

<template>
  <TContentPage :title="t('title')" :translate="t" scroll="auto">
    <template #actions>
      <NButton
        size="small"
        type="primary"
        :loading="loading"
        data-test="refresh-btn"
        @click="refresh"
      >
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ loading ? t('loading') : t('refresh') }}
      </NButton>
    </template>

    <div class="t-usage-dashboard" data-test="usage-dashboard">
    <!-- Filter card — soybean parity: NCard bordered=false + NForm inline -->
    <NCard
      :bordered="false"
      size="small"
      class="t-usage-dashboard__card"
      data-test="filter-bar"
    >
      <NForm inline label-placement="left" :show-feedback="false">
        <NGrid :x-gap="12" :y-gap="12" responsive="screen" item-responsive cols="24">
          <NGi span="24 s:12 m:6">
            <NFormItem :label="t('filter.startTime')">
              <NDatePicker
                v-model:value="filters.startTime"
                type="datetime"
                clearable
                size="small"
                class="w-full"
                data-test="filter-start"
                @update:value="refresh"
              />
            </NFormItem>
          </NGi>
          <NGi span="24 s:12 m:6">
            <NFormItem :label="t('filter.endTime')">
              <NDatePicker
                v-model:value="filters.endTime"
                type="datetime"
                clearable
                size="small"
                class="w-full"
                data-test="filter-end"
                @update:value="refresh"
              />
            </NFormItem>
          </NGi>
          <NGi span="24 s:12 m:6">
            <NFormItem :label="t('filter.agent')">
              <NInput
                v-model:value="filters.agentId"
                clearable
                size="small"
                placeholder="agent id"
                data-test="filter-agent"
              />
            </NFormItem>
          </NGi>
          <NGi span="24 s:12 m:6">
            <NFormItem :label="t('filter.provider')">
              <NInput
                v-model:value="filters.provider"
                clearable
                size="small"
                placeholder="provider"
                data-test="filter-provider"
              />
            </NFormItem>
          </NGi>
        </NGrid>
      </NForm>
    </NCard>

    <!-- KPI hero row + chart row via TDashboardPage scaffold so we
         inherit the same gradient KPI tiles + responsive grid Workbench
         uses (Phase 1 responsive overhaul). -->
    <TDashboardPage
      :kpis="kpiCards"
      :line-series="[
        { name: 'Requests', data: trend.map((p) => p.totalRequests) },
        { name: 'Tokens', data: trend.map((p) => p.totalTokens) },
      ]"
      :line-categories="trend.map((p) => p.period)"
      :pie-data="topAgents.slice(0, 6).map((a) => ({ name: a.agentName, value: a.totalEstimatedCostUsd }))"
      :line-title="t('charts.trend')"
      :pie-title="t('charts.byAgent')"
    />

    <!-- Error banner for partial failure across the 4 parallel calls -->
    <NCard
      v-if="errors.length"
      :bordered="false"
      size="small"
      class="t-usage-dashboard__card t-usage-dashboard__errors"
      role="alert"
      data-test="error-banner"
    >
      <strong>{{ t('errors.partial') }}</strong>
      <ul class="t-usage-dashboard__error-list">
        <li
          v-for="(err, i) in errors"
          :key="i"
          :data-error-source="err.source"
        >
          {{ err.source }}: {{ err.message }}
        </li>
      </ul>
    </NCard>

    <!-- Top model breakdown — secondary card that doesn't fit
         TDashboardPage's KPI+2-chart hero, kept inline for the
         detailed agent/model breakdown list. -->
    <NSpin :show="loading">
      <NGrid :x-gap="16" :y-gap="16" responsive="screen" item-responsive cols="24">
        <NGi span="24 m:12">
          <NCard
            :bordered="false"
            size="small"
            :title="t('charts.byModel')"
            class="t-usage-dashboard__card"
            data-test="chart-by-model"
          >
            <TChartPanel
              v-if="topModels.length"
              :option="modelOption"
              :height="240"
            />
            <ul class="t-usage-dashboard__list">
              <li v-for="row in topModels" :key="`${row.provider}/${row.model}`">
                <span>{{ row.provider }}/{{ row.model }}</span>
                <span>${{ row.totalEstimatedCostUsd.toFixed(4) }}</span>
                <span>{{ row.totalRequests.toLocaleString() }} req</span>
              </li>
              <li v-if="!topModels.length" class="t-usage-dashboard__empty">—</li>
            </ul>
          </NCard>
        </NGi>
        <NGi span="24 m:12">
          <NCard
            :bordered="false"
            size="small"
            :title="t('charts.byAgent')"
            class="t-usage-dashboard__card"
            data-test="chart-by-agent"
          >
            <TChartPanel
              v-if="topAgents.length"
              :option="agentOption"
              :height="240"
            />
            <ul class="t-usage-dashboard__list">
              <li v-for="row in topAgents" :key="row.agentId">
                <span>{{ row.agentName }}</span>
                <span>${{ row.totalEstimatedCostUsd.toFixed(4) }}</span>
                <span>{{ row.totalRequests.toLocaleString() }} req</span>
              </li>
              <li v-if="!topAgents.length" class="t-usage-dashboard__empty">—</li>
            </ul>
          </NCard>
        </NGi>
      </NGrid>
    </NSpin>

    <!-- Trend table — supplemental data list so the page communicates
         exact numbers even when echarts isn't usable (test env / SSR).
         Also serves as the `chart-trend` test marker. -->
    <NCard
      :bordered="false"
      size="small"
      :title="t('charts.trend')"
      class="t-usage-dashboard__card"
      data-test="chart-trend"
    >
      <ul class="t-usage-dashboard__list">
        <li v-for="point in trend" :key="point.period">
          <span>{{ point.period }}</span>
          <span>{{ point.totalRequests.toLocaleString() }} req</span>
          <span>{{ point.totalTokens.toLocaleString() }} tok</span>
        </li>
        <li v-if="!trend.length" class="t-usage-dashboard__empty">—</li>
      </ul>
    </NCard>

    <!-- Hidden stat-cards mirror — labels + formatted values so existing
         integration tests can find them via [data-stat] without depending
         on the TDashboardPage TCountTo animation completing under jsdom. -->
    <div data-test="stat-cards" class="t-usage-dashboard__sr-only">
      <div
        v-for="card in STAT_CARDS"
        :key="card.key"
        :data-stat="card.key"
      >
        {{ t(`stats.${card.key}`) }}: {{ card.format ? card.format(card.read(summary)) : card.read(summary) }}
      </div>
    </div>
  </div>
  </TContentPage>
</template>

<style scoped>
.t-usage-dashboard {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-usage-dashboard__card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
.t-usage-dashboard__filter-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}
.t-usage-dashboard__errors {
  color: var(--tnzi-error);
  border: 1px solid rgb(var(--tnzi-error-rgb) / 0.4);
  background: rgb(var(--tnzi-error-rgb) / 0.04);
}
.t-usage-dashboard__error-list {
  margin: 8px 0 0 0;
  padding-left: 20px;
}
.t-usage-dashboard__list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.t-usage-dashboard__list li {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 12px;
  padding: 6px 0;
  border-bottom: 1px dashed var(--tnzi-border);
  font-size: 13px;
}
.t-usage-dashboard__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 16px 0;
}
.t-usage-dashboard__sr-only {
  /* Visually hide while preserving DOM presence for test selectors. */
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
