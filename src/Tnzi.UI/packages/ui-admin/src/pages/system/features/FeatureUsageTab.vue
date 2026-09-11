<template>
  <div class="fu-tab">
    <div class="fu-toolbar">
      <NDatePicker
        v-model:value="range"
        type="daterange"
        size="small"
        clearable
        class="fu-toolbar__range"
        @update:value="refresh"
      />
      <NSelect
        v-model:value="top"
        :options="topOptions"
        size="small"
        class="fu-toolbar__top"
        @update:value="refresh"
      />
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('usage.actions.refresh') }}
      </NButton>
      <NPopconfirm v-if="can('feature.delete')" @positive-click="cleanup">
        <template #trigger>
          <NButton size="small" type="warning" tertiary :loading="cleaning">
            <template #icon><TSvgIcon icon="mdi:delete-sweep-outline" :size="14" /></template>
            {{ t('usage.actions.cleanup') }}
          </NButton>
        </template>
        <div class="fu-cleanup">
          <span>{{ t('usage.cleanupConfirm') }}</span>
          <NInputNumber v-model:value="retentionDays" size="small" :min="1" :step="30" class="fu-cleanup__days">
            <template #suffix>{{ t('usage.days') }}</template>
          </NInputNumber>
        </div>
      </NPopconfirm>
    </div>

    <!--
      Ranking first: the question an operator arrives with is "which flags are
      actually being asked about". Picking a row loads that flag's totals and
      its trend below.
    -->
    <TResponsiveTable
      :columns="rankingColumns"
      :data="ranking"
      :loading="loading"
      :pagination="false"
      :bordered="false"
      :row-props="rankingRowProps"
      :row-class-name="rankingRowClass"
      size="small"
    />

    <div v-if="selected" class="fu-detail">
      <div class="fu-detail__head">
        <TSvgIcon icon="mdi:chart-timeline-variant" :size="16" />
        <span class="fu-detail__title">{{ selected }}</span>
        <NSelect
          v-model:value="period"
          :options="periodOptions"
          size="small"
          class="fu-detail__period"
          @update:value="loadTrend"
        />
      </div>

      <TKpiRow cols="1 s:2 m:5">
        <TKpiCard :label="t('usage.kpi.totalChecks')" :value="stats?.totalChecks ?? null" icon="mdi:counter" />
        <TKpiCard :label="t('usage.kpi.uniqueUsers')" :value="stats?.uniqueUsers ?? null" icon="mdi:account-group-outline" />
        <TKpiCard :label="t('usage.kpi.enableRate')" :value="formatRate(stats?.enableRate) || null" icon="mdi:toggle-switch-outline" tone="success" />
        <!-- Icon names must exist in the published icon manifest (IconManifestTests):
             an unlisted name renders as a blank square in offline consumers. -->
        <TKpiCard :label="t('usage.kpi.firstUsed')" :value="fmt(stats?.firstUsed)" icon="mdi:calendar-blank-outline" />
        <TKpiCard :label="t('usage.kpi.lastUsed')" :value="fmt(stats?.lastUsed)" icon="mdi:calendar-clock" />
      </TKpiRow>

      <NSpin :show="trendLoading">
        <TChartPanel v-if="trend.length" :option="trendOption" :height="260" />
        <NEmpty v-else :description="t('usage.noTrend')" class="fu-detail__empty" />
      </NSpin>
    </div>
    <NEmpty v-else-if="!loading" :description="t('usage.pickFeature')" class="fu-empty" />
  </div>
</template>

<script setup lang="ts">
/**
 * Feature usage - who is asking about which flags, and how the answer trends.
 *
 * Wraps the four `/admin/feature/usage` endpoints:
 *   most-used → the ranking table (date range + top N)
 *   stats     → the KPI row for the selected feature
 *   trend     → the stacked enabled/disabled bars, bucketed by period
 *   cleanup   → retention purge (`feature.delete`), confirmed inline with the
 *               retention window editable in the confirmation itself so the
 *               number an operator agrees to is the number that gets sent.
 *
 * Every `IFeatureChecker.IsEnabledAsync` call records one row, so the table
 * only grows; this tab is the one place that both reads those rows and offers
 * the purge. Nothing here is a write except cleanup.
 */
import { computed, h, onMounted, ref } from 'vue'
import { NButton, NDatePicker, NEmpty, NInputNumber, NPopconfirm, NSelect, NSpin } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TChartPanel from '../../../components/display/TChartPanel.vue'
import { TKpiCard, TKpiRow } from '../../../components/data'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import {
  createFeatureBridge,
  type FeaturePopularityDto,
  type FeatureUsagePeriod,
  type FeatureUsageStatsDto,
  type FeatureUsageTrendDto,
} from '../../../services/bridges/feature-bridge'
import { useAdminClient } from '../../../plugin/client'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import {
  USAGE_PERIOD_OPTIONS,
  USAGE_TOP_OPTIONS,
  buildTrendOption,
  defaultUsageRange,
  formatRate,
  sortPopularity,
  toRangeQuery,
} from './feature-usage-config'

const bridge = createFeatureBridge({ client: useAdminClient() })
const t = makePageTranslator('system.features')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const range = ref<[number, number] | null>(defaultUsageRange())
const top = ref<number>(10)
const period = ref<FeatureUsagePeriod>('daily')
const retentionDays = ref(90)

const ranking = ref<FeaturePopularityDto[]>([])
const selected = ref<string | null>(null)
const stats = ref<FeatureUsageStatsDto | null>(null)
const trend = ref<FeatureUsageTrendDto[]>([])

const loading = ref(false)
const trendLoading = ref(false)
const cleaning = ref(false)

const topOptions = USAGE_TOP_OPTIONS.map((n) => ({ label: t('usage.topN', { n }), value: n }))
const periodOptions = USAGE_PERIOD_OPTIONS.map((p) => ({ label: t(p.labelKey), value: p.value }))

const fmt = (v?: string | null) => (v ? formatDateTime(v) : EMPTY_DASH)

const rankingColumns = computed<DataTableColumns<FeaturePopularityDto>>(() => [
  {
    key: 'featureName',
    title: t('usage.columns.feature'),
    minWidth: 200,
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.featureName),
  },
  { key: 'checkCount', title: t('usage.columns.checks'), width: 120, align: 'right' },
  { key: 'uniqueUsers', title: t('usage.columns.uniqueUsers'), width: 130, align: 'right' },
  {
    key: 'enableRate',
    title: t('usage.columns.enableRate'),
    width: 120,
    align: 'right',
    render: (row) => formatRate(row.enableRate) || EMPTY_DASH,
  },
])

const rankingRowProps = (row: FeaturePopularityDto) => ({
  style: 'cursor: pointer',
  onClick: () => void select(row.featureName),
})
const rankingRowClass = (row: FeaturePopularityDto) => (row.featureName === selected.value ? 'fu-row--selected' : '')

const trendOption = computed(() =>
  buildTrendOption(trend.value, { enabled: t('usage.series.enabled'), disabled: t('usage.series.disabled') }),
)

async function refresh() {
  loading.value = true
  try {
    ranking.value = sortPopularity(await bridge.usage.mostUsed(top.value, toRangeQuery(range.value)))
    // Keep the selection if it survived the new window; otherwise fall to the top row.
    const keep = selected.value && ranking.value.some((r) => r.featureName === selected.value)
    const next = keep ? selected.value : (ranking.value[0]?.featureName ?? null)
    if (next) await select(next)
    else {
      selected.value = null
      stats.value = null
      trend.value = []
    }
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    loading.value = false
  }
}

async function select(featureName: string) {
  selected.value = featureName
  trendLoading.value = true
  try {
    const query = toRangeQuery(range.value)
    const [s, tr] = await Promise.all([
      bridge.usage.stats(featureName, query),
      bridge.usage.trend(featureName, period.value, query),
    ])
    stats.value = s
    trend.value = tr
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    trendLoading.value = false
  }
}

async function loadTrend() {
  if (!selected.value) return
  trendLoading.value = true
  try {
    trend.value = await bridge.usage.trend(selected.value, period.value, toRangeQuery(range.value))
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    trendLoading.value = false
  }
}

async function cleanup() {
  cleaning.value = true
  try {
    const deleted = await bridge.usage.cleanup(retentionDays.value)
    message.success(t('usage.cleaned', { count: deleted }))
    await refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    cleaning.value = false
  }
}

onMounted(() => {
  void refresh()
})
</script>

<style scoped>
.fu-tab {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.fu-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.fu-toolbar__range {
  width: 280px;
}
.fu-toolbar__top {
  width: 120px;
}
.fu-cleanup {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: 280px;
}
.fu-cleanup__days {
  width: 160px;
}
.fu-detail {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-bg-card, #fff);
}
.fu-detail__head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.fu-detail__title {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-weight: 600;
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.fu-detail__period {
  width: 130px;
}
.fu-detail__empty,
.fu-empty {
  padding: 24px 0;
}
:deep(.fu-row--selected td) {
  background: rgb(var(--tnzi-primary-rgb, 24 160 88) / 0.08);
}
</style>
