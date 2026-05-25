<template>
  <!--
    Performance — wraps /admin/performance/{summary,endpoints,slow-requests}.
    Layout: header (window selector + KPI row) → tabbed table region
    (endpoints | slow-requests). Tabs replaced the stacked two-card
    layout because vertical stacking forced the slow-requests card to
    either eat the viewport or scroll the entire page; with tabs each
    table fills the residual height and gets its own pagination at the
    bottom of the visible card. Read-mostly; one destructive button
    (clear) gated by NPopconfirm.
    -->
  <div class="t-perf-page t-stack-page">
    <div class="t-perf-page__header">
      <div class="t-perf-page__title">
        <TSvgIcon icon="mdi:speedometer" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-perf-page__toolbar">
        <NSelect
          v-model:value="windowMinutes"
          :options="windowOptions"
          size="medium"
          style="width: 160px"
          @update:value="refresh"
        />
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
        <NPopconfirm @positive-click="clearAll">
          <template #trigger>
            <NButton size="small" type="warning" tertiary>
              <template #icon><TSvgIcon icon="mdi:delete-sweep-outline" :size="14" /></template>
              {{ t('actions.clear') }}
            </NButton>
          </template>
          {{ t('clearConfirm') }}
        </NPopconfirm>
      </div>
    </div>

    <div class="t-perf-page__kpis">
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.p50')" :value="formatMs(summary?.p50)" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.p95')" :value="formatMs(summary?.p95)" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.p99')" :value="formatMs(summary?.p99)" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.avg')" :value="formatMs(summary?.average)" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.samples')" :value="summary?.sampleCount ?? 0" />
      </NCard>
    </div>

    <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
      <NTabPane name="endpoints" :tab="t('sections.endpoints')">
        <div class="t-table-tabs__pane">
          <div class="t-table-tabs__toolbar">
            <NText depth="3" style="font-size: 12px">
              {{ t('sections.endpointsHint', { minutes: windowMinutes }) }}
            </NText>
          </div>
          <NDataTable
            :columns="endpointColumns"
            :data="endpoints"
            :loading="loading"
            :pagination="{ pageSize: 15 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>

      <NTabPane name="slow" :tab="t('sections.slow')">
        <div class="t-table-tabs__pane">
          <div class="t-table-tabs__toolbar">
            <NText depth="3" style="font-size: 12px">
              {{ t('sections.slowHint') }}
            </NText>
          </div>
          <NDataTable
            :columns="slowColumns"
            :data="slow"
            :loading="loading"
            :pagination="{ pageSize: 10 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>
    </NTabs>
  </div>
</template>

<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NPopconfirm,
  NSelect,
  NStatistic,
  NTabs,
  NTabPane,
  NTag,
  NText,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import {
  createPerformanceBridge,
  type EndpointStatsDto,
  type PercentileResultDto,
  type SlowRequestRecordDto,
} from '../../services/bridges/performance-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'

const bridge = createPerformanceBridge({ client: useAdminClient() })

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('system.performance', key), params)

const loading = ref(false)
const summary = ref<PercentileResultDto | null>(null)
const endpoints = ref<EndpointStatsDto[]>([])
const slow = ref<SlowRequestRecordDto[]>([])
const windowMinutes = ref(60)
const activeTab = ref<'endpoints' | 'slow'>('endpoints')
const windowOptions = [
  { value: 15, label: '15 min' },
  { value: 60, label: '1 h' },
  { value: 360, label: '6 h' },
  { value: 1440, label: '24 h' },
]

function methodTone(m: string): 'success' | 'info' | 'warning' | 'error' | 'default' {
  switch ((m ?? '').toUpperCase()) {
    case 'GET': return 'info'
    case 'POST': return 'success'
    case 'PUT': return 'warning'
    case 'PATCH': return 'warning'
    case 'DELETE': return 'error'
    default: return 'default'
  }
}

function formatMs(v: number | undefined | null): string {
  if (v == null) return '—'
  if (v >= 1000) return `${(v / 1000).toFixed(2)} s`
  return `${v.toFixed(1)} ms`
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return ''
  try { return new Date(iso).toLocaleString() } catch { return iso }
}

const endpointColumns: DataTableColumns<EndpointStatsDto> = [
  {
    title: () => t('cols.method'),
    key: 'method',
    width: 90,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: methodTone(row.method) }, () => row.method),
  },
  {
    title: () => t('cols.path'),
    key: 'path',
    ellipsis: { tooltip: true },
    render: (row) => h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.path),
  },
  {
    title: () => t('cols.count'),
    key: 'requestCount',
    width: 90,
    align: 'right',
    sorter: (a, b) => a.requestCount - b.requestCount,
  },
  {
    title: () => t('cols.avg'),
    key: 'averageDurationMs',
    width: 100,
    align: 'right',
    sorter: (a, b) => a.averageDurationMs - b.averageDurationMs,
    render: (row) => formatMs(row.averageDurationMs),
  },
  {
    title: () => t('cols.p95'),
    key: 'p95DurationMs',
    width: 100,
    align: 'right',
    sorter: (a, b) => a.p95DurationMs - b.p95DurationMs,
    render: (row) => formatMs(row.p95DurationMs),
  },
  {
    title: () => t('cols.max'),
    key: 'maxDurationMs',
    width: 100,
    align: 'right',
    render: (row) => formatMs(row.maxDurationMs),
  },
  {
    title: () => t('cols.errors'),
    key: 'errorCount',
    width: 90,
    align: 'right',
    render: (row) =>
      row.errorCount > 0
        ? h(NTag, { size: 'tiny', bordered: false, type: 'error' }, () => String(row.errorCount))
        : '0',
  },
  {
    title: () => t('cols.lastSeen'),
    key: 'lastRequestTime',
    width: 170,
    render: (row) => formatDate(row.lastRequestTime),
  },
]

const slowColumns: DataTableColumns<SlowRequestRecordDto> = [
  {
    title: () => t('cols.timestamp'),
    key: 'timestamp',
    width: 170,
    render: (row) => formatDate(row.timestamp),
  },
  {
    title: () => t('cols.method'),
    key: 'method',
    width: 90,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: methodTone(row.method) }, () => row.method),
  },
  {
    title: () => t('cols.path'),
    key: 'path',
    ellipsis: { tooltip: true },
    render: (row) => h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.path),
  },
  {
    title: () => t('cols.status'),
    key: 'statusCode',
    width: 90,
    align: 'center',
    render: (row) =>
      h(
        NTag,
        {
          size: 'tiny',
          bordered: false,
          type: row.statusCode >= 500 ? 'error' : row.statusCode >= 400 ? 'warning' : 'success',
        },
        () => String(row.statusCode),
      ),
  },
  {
    title: () => t('cols.duration'),
    key: 'durationMs',
    width: 110,
    align: 'right',
    sorter: (a, b) => a.durationMs - b.durationMs,
    render: (row) => formatMs(row.durationMs),
  },
  {
    title: () => t('cols.userId'),
    key: 'userId',
    width: 200,
    ellipsis: { tooltip: true },
    render: (row) => row.userId ?? '—',
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [s, eps, sl] = await Promise.all([
      bridge.getSummary(windowMinutes.value),
      bridge.getEndpoints(windowMinutes.value, 0),
      bridge.getSlowRequests(50),
    ])
    summary.value = s
    // Defensive: NDataTable's `createTreeMate` throws `rawNodes.forEach is
    // not a function` when handed a non-array (e.g. empty response wrapped
    // as `{}` or `null`). The bridge already falls back to `[]` for
    // null/undefined, but envelope shapes that bypass `?? []` would still
    // explode here without an explicit isArray gate.
    endpoints.value = Array.isArray(eps) ? eps : []
    slow.value = Array.isArray(sl) ? sl : []
  } catch {
    summary.value = null
    endpoints.value = []
    slow.value = []
  } finally {
    loading.value = false
  }
}

async function clearAll(): Promise<void> {
  try {
    await bridge.clear()
    await refresh()
  } catch { /* bridge swallows */ }
}

onMounted(() => {
  void refresh()
})
</script>

<style scoped>
/* Page-specific decorations only — layout flex shell + table-card flex
   sizing comes from the shared `.t-stack-page` + `.t-table-tabs`
   utilities (styles/polish.css). */
.t-perf-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-perf-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-perf-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-perf-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-perf-page__kpis {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
}
@media (max-width: 1200px) {
  .t-perf-page__kpis {
    grid-template-columns: repeat(3, 1fr);
  }
}
@media (max-width: 640px) {
  .t-perf-page__kpis {
    grid-template-columns: repeat(2, 1fr);
  }
}
</style>
