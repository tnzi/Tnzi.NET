<script setup lang="ts">
/**
 * UsageDashboard — AI usage analytics page.
 *
 * Routed at /admin/ai/usage. Organised as NTabs:
 *   - Overview : KPI cards + trend line + by-agent pie (TDashboardPage) +
 *                a by-model cost breakdown card (chart + ranked list).
 *   - Cost     : total USD KPI + per-provider cost table (expandable byModel),
 *                fed by `bridge.usage.costSummary`.
 *   - Feedback : per-agent good-rate (progress bar) + rated counts + tag
 *                distribution chips, fed by `bridge.usage.feedbackStats`.
 *   - Logs     : paged usage-log detail table, fed by `bridge.usage.getLogs`.
 *
 * All four tabs share the same date-range filter card; refresh() fans out
 * every bridge call with Promise.allSettled so one failed source surfaces in
 * the error banner without dropping the others.
 */
import { reactive, ref, computed, onMounted, h } from 'vue'
import {
  NCard, NButton, NForm, NFormItem, NGrid, NGi,
  NDatePicker, NInput, NSpin, NTabs, NTabPane, NProgress, NTag,
  NStatistic, type DataTableColumns,
} from 'naive-ui'
import type { EChartsOption } from 'echarts'
import { formatDateTime } from '@tnzi/core'
import TChartPanel from '../../../components/display/TChartPanel.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
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
  EMPTY_COST_SUMMARY,
  sortProviderCosts,
  modelCostsForProvider,
  feedbackBarStatus,
  toPercent,
  tagDistributionEntries,
  sortFeedbackStats,
  type DashboardUsageSummary,
  type StatCardDefinition,
} from './usage-dashboard-config'
import type {
  AgentUsageDto,
  ModelUsageDto,
  UsageTrendPointDto,
  CostSummaryDto,
  ProviderCostDto,
  AgentFeedbackStatsDto,
  UsageLogDto,
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
const cost = ref<CostSummaryDto>(EMPTY_COST_SUMMARY)
const feedback = ref<AgentFeedbackStatsDto[]>([])
const logs = ref<UsageLogDto[]>([])
const logTotal = ref(0)
const logPage = ref(1)
const logPageSize = ref(20)

const activeTab = ref<'overview' | 'cost' | 'feedback' | 'logs'>('overview')

const loading = ref(false)
const logsLoading = ref(false)
type ErrorSource =
  | 'summary' | 'byAgent' | 'byModel' | 'byDay'
  | 'costSummary' | 'feedbackStats' | 'getLogs'
interface PartialError {
  source: ErrorSource
  message: string
}
const errors = ref<PartialError[]>([])

const topAgents = computed(() => topAgentsByCost(agentRows.value, 10))
const topModels = computed(() => topModelsByCost(modelRows.value, 10))

// ---- KPI cards (Overview) -------------------------------------------------
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
// The Overview trend line + by-agent pie are rendered by TDashboardPage
// (line-series / pie-data props); only the by-model bar needs its own option.

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

// ---- Cost tab -------------------------------------------------------------

const providerCosts = computed(() => sortProviderCosts(cost.value.byProvider ?? []))

/** Pie of provider spend share (USD). */
const costPieOption = computed<EChartsOption>(() => ({
  tooltip: { trigger: 'item', formatter: '{b}: ${c} ({d}%)' },
  legend: { type: 'scroll', bottom: 0 },
  series: [
    {
      type: 'pie',
      radius: ['40%', '70%'],
      avoidLabelOverlap: true,
      label: { show: false },
      data: providerCosts.value.map((p) => ({
        name: p.provider,
        value: Number(p.totalCostUsd.toFixed(4)),
      })),
    },
  ],
}))

const costColumns = computed<DataTableColumns<ProviderCostDto>>(() => [
  {
    type: 'expand',
    expandable: (row) => modelCostsForProvider(cost.value.byModel ?? [], row.provider).length > 0,
    renderExpand: (row) => {
      const models = modelCostsForProvider(cost.value.byModel ?? [], row.provider)
      return h(
        'ul',
        { class: 't-usage-dashboard__list' },
        models.map((m) =>
          h('li', { key: `${m.provider}/${m.model}` }, [
            h('span', `${m.provider}/${m.model}`),
            h('span', `$${m.totalCostUsd.toFixed(4)}`),
            h('span', `${m.totalRequests.toLocaleString()} req`),
          ]),
        ),
      )
    },
  },
  { key: 'provider', title: t('cost.provider'), minWidth: 140 },
  {
    key: 'totalCostUsd',
    title: t('cost.totalCost'),
    minWidth: 120,
    render: (row) => `$${row.totalCostUsd.toFixed(4)}`,
  },
  {
    key: 'costPercentage',
    title: t('cost.share'),
    minWidth: 120,
    render: (row) =>
      h(NProgress, {
        type: 'line',
        percentage: toPercent(row.costPercentage),
        height: 6,
        indicatorPlacement: 'inside',
      }),
  },
  {
    key: 'totalRequests',
    title: t('cost.requests'),
    minWidth: 110,
    render: (row) => row.totalRequests.toLocaleString(),
  },
])

// ---- Logs tab -------------------------------------------------------------

const logColumns = computed<DataTableColumns<UsageLogDto>>(() => [
  {
    key: 'creationTime',
    title: t('logs.time'),
    minWidth: 160,
    render: (row) => formatDateTime(row.creationTime),
  },
  { key: 'operationType', title: t('logs.operation'), minWidth: 120 },
  {
    key: 'model',
    title: t('logs.model'),
    minWidth: 180,
    render: (row) => `${row.provider}/${row.model}`,
  },
  {
    key: 'agentId',
    title: t('logs.agent'),
    minWidth: 120,
    render: (row) => row.agentId ?? '—',
  },
  {
    key: 'tokens',
    title: t('logs.tokens'),
    minWidth: 130,
    render: (row) =>
      `${row.inputTokens.toLocaleString()} / ${row.outputTokens.toLocaleString()}`,
  },
  {
    key: 'estimatedCostUsd',
    title: t('logs.cost'),
    minWidth: 100,
    render: (row) =>
      row.estimatedCostUsd != null ? `$${row.estimatedCostUsd.toFixed(4)}` : '—',
  },
  {
    key: 'durationMs',
    title: t('logs.duration'),
    minWidth: 100,
    render: (row) => `${row.durationMs.toLocaleString()} ms`,
  },
  {
    key: 'isSuccess',
    title: t('logs.result'),
    minWidth: 100,
    render: (row) =>
      h(
        NTag,
        { size: 'small', type: row.isSuccess ? 'success' : 'error', bordered: false },
        { default: () => (row.isSuccess ? t('logs.success') : t('logs.failed')) },
      ),
  },
])

const logPagination = computed(() => ({
  page: logPage.value,
  pageSize: logPageSize.value,
  itemCount: logTotal.value,
  onUpdatePage: (p: number) => {
    logPage.value = p
    void refreshLogs()
  },
  onUpdatePageSize: (s: number) => {
    logPageSize.value = s
    logPage.value = 1
    void refreshLogs()
  },
}))

function buildQuery(extra?: Record<string, unknown>) {
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
    ...extra,
  }
}

function recordError(source: ErrorSource, reason: unknown) {
  const err = reason as Error
  errors.value.push({ source, message: err?.message ?? 'failed' })
}

/** Paged-only refresh for the Logs table (pagination changes don't re-pull
 *  the whole dashboard). */
async function refreshLogs() {
  logsLoading.value = true
  errors.value = errors.value.filter((e) => e.source !== 'getLogs')
  const q = buildQuery({ pageIndex: logPage.value, pageSize: logPageSize.value })
  try {
    const page = await bridge.usage.getLogs(q)
    logs.value = page.items ?? []
    logTotal.value = page.totalCount ?? 0
  } catch (reason) {
    recordError('getLogs', reason)
    logs.value = []
    logTotal.value = 0
  } finally {
    logsLoading.value = false
  }
}

async function refresh() {
  loading.value = true
  logsLoading.value = true
  errors.value = []
  logPage.value = 1
  const q = buildQuery()
  const logQ = buildQuery({ pageIndex: logPage.value, pageSize: logPageSize.value })

  const results = await Promise.allSettled([
    bridge.usage.summary(q),
    bridge.usage.byAgent(q),
    bridge.usage.byModel(q),
    bridge.usage.byDay(q),
    bridge.usage.costSummary(q),
    bridge.usage.feedbackStats(20),
    bridge.usage.getLogs(logQ),
  ])

  const [
    rSummary, rAgent, rModel, rDay, rCost, rFeedback, rLogs,
  ] = results

  if (rSummary.status === 'fulfilled') summary.value = rSummary.value as DashboardUsageSummary
  else recordError('summary', rSummary.reason)

  if (rAgent.status === 'fulfilled') agentRows.value = rAgent.value as AgentUsageDto[]
  else recordError('byAgent', rAgent.reason)

  if (rModel.status === 'fulfilled') modelRows.value = rModel.value as ModelUsageDto[]
  else recordError('byModel', rModel.reason)

  if (rDay.status === 'fulfilled') trend.value = rDay.value as UsageTrendPointDto[]
  else recordError('byDay', rDay.reason)

  if (rCost.status === 'fulfilled') cost.value = (rCost.value as CostSummaryDto) ?? EMPTY_COST_SUMMARY
  else recordError('costSummary', rCost.reason)

  if (rFeedback.status === 'fulfilled') feedback.value = sortFeedbackStats(rFeedback.value as AgentFeedbackStatsDto[])
  else recordError('feedbackStats', rFeedback.reason)

  if (rLogs.status === 'fulfilled') {
    logs.value = rLogs.value.items ?? []
    logTotal.value = rLogs.value.totalCount ?? 0
  } else {
    recordError('getLogs', rLogs.reason)
  }

  loading.value = false
  logsLoading.value = false
}

onMounted(() => {
  void refresh()
})

defineExpose({
  refresh, refreshLogs, filters, summary, agentRows, modelRows, trend,
  cost, feedback, logs, logTotal, errors, activeTab,
})
</script>

<template>
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
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
      <!-- Shared filter card — drives every tab. -->
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

      <!-- Error banner for partial failures across the parallel calls. -->
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

        <NTabs
          v-model:value="activeTab"
          type="line"
          animated
          data-test="usage-tabs"
          class="t-table-tabs t-usage-dashboard__tabs"
        >
          <!-- ===== Overview ===== -->
          <NTabPane name="overview" :tab="t('tabs.overview')" display-directive="show">
            <div class="t-usage-dashboard__tab">
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

              <!-- "Usage trend" + "By agent" are owned by TDashboardPage above
                   (line chart + cost pie) — only the by-model breakdown is an
                   extra block, so it gets the full row. -->
              <NSpin :show="loading">
                <NCard
                  :bordered="false"
                  size="small"
                  :title="t('charts.byModel')"
                  class="t-usage-dashboard__card"
                  data-test="chart-by-model"
                >
                  <TChartPanel v-if="topModels.length" :option="modelOption" :height="240" />
                  <ul class="t-usage-dashboard__list">
                    <li v-for="row in topModels" :key="`${row.provider}/${row.model}`">
                      <span>{{ row.provider }}/{{ row.model }}</span>
                      <span>${{ row.totalEstimatedCostUsd.toFixed(4) }}</span>
                      <span>{{ row.totalRequests.toLocaleString() }} req</span>
                    </li>
                    <li v-if="!topModels.length" class="t-usage-dashboard__empty">—</li>
                  </ul>
                </NCard>
              </NSpin>
            </div>
          </NTabPane>

          <!-- ===== Cost breakdown ===== -->
          <NTabPane name="cost" :tab="t('tabs.cost')" display-directive="show">
            <div class="t-usage-dashboard__tab" data-test="cost-tab">
              <NSpin :show="loading">
                <NGrid :x-gap="16" :y-gap="16" responsive="screen" item-responsive cols="24">
                  <NGi span="24 m:8">
                    <NCard :bordered="false" size="small" class="t-usage-dashboard__card">
                      <NStatistic :label="t('cost.totalCost')">
                        <span class="text-success font-600" data-test="cost-total">
                          ${{ cost.totalCostUsd.toFixed(4) }}
                        </span>
                      </NStatistic>
                      <TChartPanel
                        v-if="providerCosts.length"
                        :option="costPieOption"
                        :height="220"
                      />
                    </NCard>
                  </NGi>
                  <NGi span="24 m:16">
                    <NCard
                      :bordered="false"
                      size="small"
                      :title="t('cost.byProvider')"
                      class="t-usage-dashboard__card"
                    >
                      <TResponsiveTable
                        :columns="costColumns"
                        :data="providerCosts"
                        :row-key="(r: ProviderCostDto) => r.provider"
                        :pagination="false"
                        :loading="loading"
                        mobile="scroll"
                        size="small"
                        :empty-text="t('cost.empty')"
                      />
                    </NCard>
                  </NGi>
                </NGrid>
              </NSpin>
            </div>
          </NTabPane>

          <!-- ===== Agent feedback ===== -->
          <NTabPane name="feedback" :tab="t('tabs.feedback')" display-directive="show">
            <div class="t-usage-dashboard__tab" data-test="feedback-tab">
              <NSpin :show="loading">
                <ul class="t-usage-dashboard__feedback">
                  <li
                    v-for="row in feedback"
                    :key="row.agentId"
                    class="t-usage-dashboard__feedback-row"
                    :data-feedback-agent="row.agentId"
                  >
                    <div class="t-usage-dashboard__feedback-head">
                      <span class="font-600">{{ row.agentName }}</span>
                      <span class="text-muted text-12px">
                        {{ t('feedback.totalRated') }}: {{ row.totalRated.toLocaleString() }}
                      </span>
                    </div>
                    <NProgress
                      type="line"
                      :percentage="toPercent(row.positiveRate)"
                      :status="feedbackBarStatus(row.positiveRate)"
                      :height="8"
                    />
                    <div v-if="tagDistributionEntries(row.tagDistribution).length" class="t-usage-dashboard__tags">
                      <NTag
                        v-for="entry in tagDistributionEntries(row.tagDistribution)"
                        :key="entry.tag"
                        size="small"
                        :bordered="false"
                      >
                        {{ entry.tag }} · {{ entry.count }}
                      </NTag>
                    </div>
                  </li>
                  <li v-if="!feedback.length" class="t-usage-dashboard__empty">{{ t('feedback.empty') }}</li>
                </ul>
              </NSpin>
            </div>
          </NTabPane>

          <!-- ===== Usage logs ===== -->
          <NTabPane name="logs" :tab="t('tabs.logs')" display-directive="show">
            <div class="t-usage-dashboard__tab" data-test="logs-tab">
              <TResponsiveTable
                :columns="logColumns"
                :data="logs"
                :row-key="(r: UsageLogDto) => r.id"
                :loading="logsLoading"
                :pagination="logPagination"
                size="small"
                :empty-text="t('logs.empty')"
              />
            </div>
          </NTabPane>
        </NTabs>

      <!-- Hidden stat-cards mirror — labels + formatted values so integration
           tests can find them via [data-stat] without depending on the
           TDashboardPage TCountTo animation completing under jsdom. -->
      <div data-test="stat-cards" class="t-usage-dashboard__sr-only">
        <div
          v-for="card in STAT_CARDS"
          :key="card.key"
          :data-stat="card.key"
        >
          {{ t(`stats.${card.key}`) }}: {{ card.format ? card.format(card.read(summary)) : card.read(summary) }}
        </div>
        <!-- Trend mirror — the trend now renders only as the TDashboardPage
             echarts line (no DOM text under jsdom), so the raw points are
             mirrored here for the same reason as the stat cards above. -->
        <div data-test="trend-mirror">
          <div v-for="point in trend" :key="point.period">
            {{ point.period }}: {{ point.totalRequests.toLocaleString() }} req / {{ point.totalTokens.toLocaleString() }} tok
          </div>
        </div>
      </div>
    </div>
  </TContentPage>
</template>

<style scoped>
/* Fill the page height: the dashboard column fills the (scroll="fill") body and
   the tabs widget claims the residual height; each tab pane scrolls internally
   so the container never leaves a background gap. */
.t-usage-dashboard {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-usage-dashboard__card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
/* The filter + error cards keep their natural height; the tabs fill the rest. */
.t-usage-dashboard > .t-usage-dashboard__card {
  flex-shrink: 0;
}
/* Each tab pane owns its own scroll (the t-table-tabs nav stays pinned). */
.t-usage-dashboard__tab {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 16px;
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
.t-usage-dashboard__feedback {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-usage-dashboard__feedback-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-bottom: 12px;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-usage-dashboard__feedback-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.t-usage-dashboard__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.t-usage-dashboard__sr-only {
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
