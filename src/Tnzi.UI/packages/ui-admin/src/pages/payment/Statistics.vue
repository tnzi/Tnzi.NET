<template>
  <!--
    Statistics — revenue overview + trend + subscription metrics
    backed by /admin/payment-statistics/*. Header KPI strip → revenue
    trend chart (line) → side-by-side channel distribution + subscription
    metrics card → plan distribution table.
  -->
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NSelect
        v-model:value="window"
        :options="windowOptions"
        size="small"
        class="w-150px"
        @update:value="refresh"
      />
      <NSelect
        v-model:value="granularity"
        :options="granularityOptions"
        size="small"
        class="w-130px"
        @update:value="refresh"
      />
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <div class="t-pay-stats-page">
    <div class="t-pay-stats-page__kpis">
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.revenue')" :value="formatMoney(overview?.totalRevenue)">
          <template #suffix><TSvgIcon icon="mdi:cash" :size="14" /></template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.transactions')" :value="overview?.totalTransactions ?? 0">
          <template #suffix>
            <NTag size="tiny" :bordered="false" type="success">
              {{ t('kpi.successCount', { n: overview?.successfulTransactions ?? 0 }) }}
            </NTag>
          </template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.refundRate')" :value="formatPercent(overview?.refundRate)">
          <template #suffix>
            <NTag size="tiny" :bordered="false" type="error">
              {{ formatMoney(overview?.totalRefunds) }}
            </NTag>
          </template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.mrr')" :value="formatMoney(metrics?.monthlyRecurringRevenue)">
          <template #suffix><TSvgIcon icon="mdi:repeat" :size="14" /></template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.activeSubs')" :value="metrics?.activeSubscriptions ?? overview?.activeSubscriptions ?? 0" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.churn')" :value="formatPercent(metrics?.churnRate)" />
      </NCard>
    </div>

    <NCard :title="t('sections.revenueTrend')" size="small" :bordered="false">
      <TWidgetLineChart
        v-if="trend.length > 0"
        :categories="trendCategories"
        :series="trendSeries"
        :height="280"
      />
      <NEmpty v-else size="small" :description="t('empty.trend')" />
    </NCard>

    <div class="t-pay-stats-page__split">
      <NCard :title="t('sections.channels')" size="small" :bordered="false">
        <TResponsiveTable
          :columns="channelColumns"
          :data="overview?.channelDistribution ?? []"
          :pagination="false"
          :bordered="false"
          size="small"
        />
      </NCard>
      <NCard :title="t('sections.subMetrics')" size="small" :bordered="false">
        <div class="t-pay-stats-page__sub-list">
          <div class="t-pay-stats-page__sub-row">
            <span>{{ t('sub.trial') }}</span>
            <span>{{ metrics?.trialSubscriptions ?? 0 }}</span>
          </div>
          <div class="t-pay-stats-page__sub-row">
            <span>{{ t('sub.newThisMonth') }}</span>
            <span>{{ metrics?.newSubscriptionsThisMonth ?? 0 }}</span>
          </div>
          <div class="t-pay-stats-page__sub-row">
            <span>{{ t('sub.cancelledThisMonth') }}</span>
            <span>{{ metrics?.cancelledThisMonth ?? 0 }}</span>
          </div>
          <div class="t-pay-stats-page__sub-row">
            <span>{{ t('sub.arpu') }}</span>
            <span>{{ formatMoney(metrics?.averageRevenuePerUser) }}</span>
          </div>
        </div>
      </NCard>
    </div>

    <NCard v-if="(metrics?.planDistribution ?? []).length > 0" :title="t('sections.plans')" size="small" :bordered="false">
      <TResponsiveTable
        :columns="planColumns"
        :data="metrics!.planDistribution"
        :pagination="false"
        :bordered="false"
        size="small"
      />
    </NCard>
  </div>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import {
  NButton,
  NCard,
  NEmpty,
  NSelect,
  NStatistic,
  NTag,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly as formatDate } from '@tnzi/core'
import TWidgetLineChart from '../../widgets/builtin/TWidgetLineChart.vue'
import TContentPage from '../../components/layout/TContentPage.vue'
import { useAdminClient } from '../../plugin/client'
import {
  createPaymentStatisticsBridge,
  type ChannelStatisticsDto,
  type PaymentStatisticsDto,
  type PlanDistributionDto,
  type RevenueTrendPointDto,
  type SubscriptionMetricsDto,
  type TrendGranularity,
} from '../../services/bridges/payment-statistics-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'

const bridge = createPaymentStatisticsBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('payment.statistics', key), params)

const loading = ref(false)
const overview = ref<PaymentStatisticsDto | null>(null)
const metrics = ref<SubscriptionMetricsDto | null>(null)
const trend = ref<RevenueTrendPointDto[]>([])

const window = ref<number>(30)
const windowOptions = [
  { value: 7, label: t('window.7') },
  { value: 30, label: t('window.30') },
  { value: 90, label: t('window.90') },
  { value: 365, label: t('window.365') },
]
const granularity = ref<TrendGranularity>(1)
const granularityOptions = [
  { value: 1 as const, label: t('granularity.day') },
  { value: 2 as const, label: t('granularity.week') },
  { value: 3 as const, label: t('granularity.month') },
]

function formatMoney(n: number | null | undefined): string {
  if (n == null) return '—'
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: 'USD',
      maximumFractionDigits: 2,
    }).format(n)
  } catch {
    return n.toFixed(2)
  }
}
function formatPercent(n: number | null | undefined): string {
  if (n == null) return '—'
  return `${(n * 100).toFixed(2)}%`
}

const trendCategories = computed(() => trend.value.map((p) => formatDate(p.date)))
const trendSeries = computed(() => [
  { name: t('trend.revenue'), data: trend.value.map((p) => Number(p.revenue.toFixed(2))) },
  { name: t('trend.netRevenue'), data: trend.value.map((p) => Number(p.netRevenue.toFixed(2))) },
])

const channelColumns: DataTableColumns<ChannelStatisticsDto> = [
  { title: () => t('cols.channel'), key: 'channelCode' },
  {
    title: () => t('cols.revenue'),
    key: 'revenue',
    align: 'right',
    render: (row) => formatMoney(row.revenue),
  },
  { title: () => t('cols.transactions'), key: 'transactionCount', align: 'right' },
  {
    title: () => t('cols.share'),
    key: 'percentage',
    align: 'right',
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => `${row.percentage.toFixed(1)}%`),
  },
]

// Columns mirror the backend `PlanDistributionDto` (planName /
// subscriptionCount / revenue). There is no `planCode` server-side, so it is
// not surfaced.
const planColumns: DataTableColumns<PlanDistributionDto> = [
  { title: () => t('cols.planName'), key: 'planName' },
  { title: () => t('cols.subscribers'), key: 'subscriptionCount', align: 'right' },
  {
    title: () => t('cols.monthlyRevenue'),
    key: 'revenue',
    align: 'right',
    render: (row) => formatMoney(row.revenue),
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const end = new Date()
    const start = new Date(end.getTime() - window.value * 24 * 60 * 60 * 1000)
    const startIso = start.toISOString()
    const endIso = end.toISOString()
    const [ov, met, tr] = await Promise.all([
      bridge.getOverview(startIso, endIso),
      bridge.getSubscriptionMetrics(),
      bridge.getRevenueTrend(startIso, endIso, granularity.value),
    ])
    overview.value = ov
    metrics.value = met
    trend.value = tr
  } catch {
    overview.value = null
    metrics.value = null
    trend.value = []
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
.t-pay-stats-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 0;
}
.t-pay-stats-page__kpis {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;
}
@media (max-width: 1200px) {
  .t-pay-stats-page__kpis { grid-template-columns: repeat(3, 1fr); }
}
@media (max-width: 640px) {
  .t-pay-stats-page__kpis { grid-template-columns: repeat(2, 1fr); }
}
.t-pay-stats-page__split {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
@media (max-width: 900px) {
  .t-pay-stats-page__split { grid-template-columns: 1fr; }
}
.t-pay-stats-page__sub-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.t-pay-stats-page__sub-row {
  display: flex;
  justify-content: space-between;
  font-size: 14px;
  padding-bottom: 6px;
  border-bottom: 1px dashed var(--tnzi-border, #eee);
}
.t-pay-stats-page__sub-row:last-child {
  border-bottom: none;
}
</style>
