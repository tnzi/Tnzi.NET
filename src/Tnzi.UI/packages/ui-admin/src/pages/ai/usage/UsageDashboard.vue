<template>
  <!--
    UsageDashboard — Phase 5 Task 5.9 analytics page.
    Routed at /admin/ai/usage. NOT a TCrudPage — filter bar + multiple cards.

    Lessons applied:
      - 1: setActivePinia in tests
      - 2: real DTOs from @tnzi/core/services/ai
      - 4: vue-tsc typecheck
      - 7: inline status banner instead of useMessage
      - 17: tolerate partial failure across 4 parallel bridge calls

    Chart-library decision (DONE_WITH_CONCERNS):
      vue-echarts is NOT a dep of @tnzi/ui-admin in Phase 5 — adding it is
      explicit scope creep per dispatch. The page renders top-N data as
      ordered lists inside the chart cards. The factories in
      ./usage-dashboard-config.ts already produce ChartSpec records that a
      Phase 6 task can hand to vue-echarts with a one-line edit.
  -->
  <div class="t-usage-dashboard t-page-scroll" data-test="usage-dashboard">
    <header class="t-usage-dashboard__header">
      <h2 class="t-usage-dashboard__title">{{ t('title') }}</h2>
      <button
        type="button"
        class="t-usage-dashboard__refresh"
        :disabled="loading"
        data-test="refresh-btn"
        @click="refresh"
      >
        {{ loading ? t('loading') : t('refresh') }}
      </button>
    </header>

    <section class="t-usage-dashboard__filters" data-test="filter-bar">
      <label class="t-usage-dashboard__field">
        <span>{{ t('filter.startTime') }}</span>
        <input
          type="datetime-local"
          :value="toLocalInput(filters.startTime)"
          data-test="filter-start"
          @change="onStartChange"
        />
      </label>
      <label class="t-usage-dashboard__field">
        <span>{{ t('filter.endTime') }}</span>
        <input
          type="datetime-local"
          :value="toLocalInput(filters.endTime)"
          data-test="filter-end"
          @change="onEndChange"
        />
      </label>
      <label class="t-usage-dashboard__field">
        <span>{{ t('filter.agent') }}</span>
        <input
          type="text"
          :value="filters.agentId ?? ''"
          data-test="filter-agent"
          @input="onAgentChange"
        />
      </label>
      <label class="t-usage-dashboard__field">
        <span>{{ t('filter.provider') }}</span>
        <input
          type="text"
          :value="filters.provider ?? ''"
          data-test="filter-provider"
          @input="onProviderChange"
        />
      </label>
    </section>

    <section class="t-usage-dashboard__stats" data-test="stat-cards">
      <div
        v-for="card in STAT_CARDS"
        :key="card.key"
        class="t-usage-dashboard__stat"
        :data-stat="card.key"
      >
        <div class="t-usage-dashboard__stat-label">{{ t(`stats.${card.key}`) }}</div>
        <div class="t-usage-dashboard__stat-value">
          {{ formatStat(card) }}
        </div>
      </div>
    </section>

    <div v-if="errors.length" class="t-usage-dashboard__errors" role="alert" data-test="error-banner">
      <strong>{{ t('errors.partial') }}</strong>
      <ul>
        <li v-for="(err, i) in errors" :key="i" :data-error-source="err.source">
          {{ err.source }}: {{ err.message }}
        </li>
      </ul>
    </div>

    <section class="t-usage-dashboard__chart-card" data-test="chart-trend">
      <h3>{{ t('charts.trend') }}</h3>
      <TChartPanel v-if="trend.length" :option="trendOption" :height="320" />
      <ul class="t-usage-dashboard__list">
        <li v-for="point in trend" :key="point.period">
          <span>{{ point.period }}</span>
          <span>{{ point.totalRequests.toLocaleString() }} req</span>
          <span>{{ point.totalTokens.toLocaleString() }} tok</span>
        </li>
      </ul>
      <p v-if="!trend.length" class="t-usage-dashboard__empty">—</p>
    </section>

    <section class="t-usage-dashboard__chart-card" data-test="chart-by-agent">
      <h3>{{ t('charts.byAgent') }}</h3>
      <TChartPanel v-if="topAgents.length" :option="agentOption" :height="280" />
      <ul class="t-usage-dashboard__list">
        <li v-for="row in topAgents" :key="row.agentId">
          <span>{{ row.agentName }}</span>
          <span>${{ row.totalEstimatedCostUsd.toFixed(4) }}</span>
          <span>{{ row.totalRequests.toLocaleString() }} req</span>
        </li>
      </ul>
      <p v-if="!topAgents.length" class="t-usage-dashboard__empty">—</p>
    </section>

    <section class="t-usage-dashboard__chart-card" data-test="chart-by-model">
      <h3>{{ t('charts.byModel') }}</h3>
      <TChartPanel v-if="topModels.length" :option="modelOption" :height="280" />
      <ul class="t-usage-dashboard__list">
        <li v-for="row in topModels" :key="`${row.provider}/${row.model}`">
          <span>{{ row.provider }}/{{ row.model }}</span>
          <span>${{ row.totalEstimatedCostUsd.toFixed(4) }}</span>
          <span>{{ row.totalRequests.toLocaleString() }} req</span>
        </li>
      </ul>
      <p v-if="!topModels.length" class="t-usage-dashboard__empty">—</p>
    </section>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, computed, onMounted } from 'vue'
import type { EChartsOption } from 'echarts'
import TChartPanel from '../../../components/display/TChartPanel.vue'
import { translatePageKey } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
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
  startTime: string
  endTime: string
  agentId?: string
  provider?: string
}

const initialRange = defaultDateRange()
const filters = reactive<DashboardFilters>({
  startTime: initialRange.startTime,
  endTime: initialRange.endTime,
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

// ---- ECharts option builders (Phase H follow-up: stat-cards → real charts) ---
// useEcharts handles theme reactivity globally, so option callers don't need to
// inject mode. Keep these as `computed` so resize / re-render fires on data
// mutation.

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
      itemStyle: { color: 'var(--tnzi-primary-color, #06B6D4)' },
    },
    {
      name: 'Tokens',
      type: 'line',
      smooth: true,
      yAxisIndex: 1,
      data: trend.value.map((p) => p.totalTokens),
      itemStyle: { color: 'var(--tnzi-success-color, #18A058)' },
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
      itemStyle: { color: 'var(--tnzi-warning-color, #F0A020)' },
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
      itemStyle: { color: 'var(--tnzi-error-color, #D03050)' },
    },
  ],
}))

function formatStat(card: StatCardDefinition): string {
  const value = card.read(summary.value)
  return card.format ? card.format(value) : String(value)
}

function buildQuery() {
  return {
    pageIndex: 1,
    pageSize: 1,
    sortField: '',
    sortOrder: 'asc' as const,
    searchText: '',
    filters: {
      startTime: filters.startTime,
      endTime: filters.endTime,
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

// ---- filter input handlers (typed in script, not template) ----------------

function toLocalInput(iso: string): string {
  // Convert ISO to "YYYY-MM-DDTHH:mm" for <input type=datetime-local>.
  try {
    const d = new Date(iso)
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
  } catch {
    return ''
  }
}

function fromLocalInput(local: string): string {
  if (!local) return new Date().toISOString()
  const d = new Date(local)
  return Number.isNaN(d.getTime()) ? new Date().toISOString() : d.toISOString()
}

function onStartChange(e: Event) {
  const v = (e.target as HTMLInputElement).value
  filters.startTime = fromLocalInput(v)
  void refresh()
}

function onEndChange(e: Event) {
  const v = (e.target as HTMLInputElement).value
  filters.endTime = fromLocalInput(v)
  void refresh()
}

function onAgentChange(e: Event) {
  const v = (e.target as HTMLInputElement).value
  filters.agentId = v.length ? v : undefined
}

function onProviderChange(e: Event) {
  const v = (e.target as HTMLInputElement).value
  filters.provider = v.length ? v : undefined
}

onMounted(() => {
  void refresh()
})

defineExpose({ refresh, filters, summary, agentRows, modelRows, trend, errors })
</script>

<style scoped>
.t-usage-dashboard {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.t-usage-dashboard__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.t-usage-dashboard__filters {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 0.75rem;
  padding: 0.75rem;
  border: 1px solid var(--t-border, #eee);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
}
.t-usage-dashboard__field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}
.t-usage-dashboard__stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.75rem;
}
.t-usage-dashboard__stat {
  padding: 1rem;
  border: 1px solid var(--t-border, #eee);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
  background: var(--t-surface, #fafafa);
}
.t-usage-dashboard__stat-label {
  font-size: 0.85rem;
  color: var(--t-muted, #666);
}
.t-usage-dashboard__stat-value {
  font-size: 1.5rem;
  font-weight: 600;
  margin-top: 0.25rem;
}
.t-usage-dashboard__chart-card {
  padding: 1rem;
  border: 1px solid var(--t-border, #eee);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
}
.t-usage-dashboard__placeholder {
  font-size: 0.8rem;
  color: var(--t-muted, #888);
  font-style: italic;
  margin: 0 0 0.5rem 0;
}
.t-usage-dashboard__list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.t-usage-dashboard__list li {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 1rem;
  padding: 0.35rem 0;
  border-bottom: 1px dashed var(--t-border, #f0f0f0);
}
.t-usage-dashboard__empty {
  color: var(--t-muted, #999);
}
.t-usage-dashboard__errors {
  padding: 0.75rem;
  border: 1px solid var(--t-danger, #c33);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
  color: var(--t-danger, #c33);
  background: rgba(204, 51, 51, 0.05);
}
.t-usage-dashboard__refresh {
  padding: 0.4rem 1rem;
  cursor: pointer;
}
.t-usage-dashboard__refresh:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}
</style>
