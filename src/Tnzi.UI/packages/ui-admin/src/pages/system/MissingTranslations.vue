<template>
  <!--
    MissingTranslations — surfaces /admin/localization/missing{,/summary,/export}.
    Two zones: summary header (totals + per-culture breakdown) and a
    filterable table of every missing key. The right-side toolbar exposes
    a "Download JSON stubs" action that turns the tracker output into
    resource-file scaffolding for translators.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NSelect
        v-model:value="cultureFilter"
        :options="cultureOptions"
        :placeholder="t('filter.culture')"
        clearable
        size="small"
        class="w-180px"
        @update:value="refresh"
      />
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
      <NButton size="small" type="primary" tertiary :disabled="missing.length === 0" @click="downloadStubs">
        <template #icon><TSvgIcon icon="mdi:download" :size="14" /></template>
        {{ t('actions.export') }}
      </NButton>
      <NPopconfirm @positive-click="clearAll">
        <template #trigger>
          <NButton size="small" type="warning" tertiary :disabled="missing.length === 0">
            <template #icon><TSvgIcon icon="mdi:delete-sweep-outline" :size="14" /></template>
            {{ t('actions.clear') }}
          </NButton>
        </template>
        {{ t('clearConfirm') }}
      </NPopconfirm>
    </template>

    <div class="t-i18n-page__kpis">
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.totalKeys')" :value="summary?.totalMissingKeys ?? 0" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.totalAccess')" :value="summary?.totalAccessCount ?? 0" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.cultures')" :value="summary?.affectedCultureCount ?? 0" />
      </NCard>
      <NCard size="small" :bordered="false">
        <div class="t-i18n-page__breakdown">
          <div class="t-i18n-page__breakdown-label">{{ t('kpi.breakdown') }}</div>
          <div v-if="summary?.cultureBreakdown?.length" class="t-i18n-page__chips">
            <NTag
              v-for="row in summary.cultureBreakdown"
              :key="row.culture"
              size="small"
              :bordered="false"
              :type="cultureFilter === row.culture ? 'primary' : 'default'"
              class="cursor-pointer"
              @click="cultureFilter = cultureFilter === row.culture ? null : row.culture; refresh()"
            >
              {{ row.culture }} · {{ row.missingKeyCount }}
            </NTag>
          </div>
          <div v-else class="t-i18n-page__empty">{{ t('empty.summary') }}</div>
        </div>
      </NCard>
    </div>

    <NCard :title="t('sections.keys')" size="small" :bordered="false" class="t-table-card">
      <template #header-extra>
        <NInput
          v-model:value="keyFilter"
          :placeholder="t('filter.key')"
          clearable
          size="small"
          class="w-240px"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
      </template>
      <TResponsiveTable
        :columns="columns"
        :data="filteredMissing"
        :loading="loading"
        :pagination="{ pageSize: 20 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import {
  NButton,
  NCard,
  NInput,
  NPopconfirm,
  NSelect,
  NStatistic,
  NTag,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createLocalizationBridge,
  type MissingTranslationDto,
  type MissingTranslationSummaryDto,
} from '../../services/bridges/localization-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'
import TContentPage from '../../components/layout/TContentPage.vue'

const bridge = createLocalizationBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('system.localization', key), params)

const loading = ref(false)
const summary = ref<MissingTranslationSummaryDto | null>(null)
const missing = ref<MissingTranslationDto[]>([])
const cultureFilter = ref<string | null>(null)
const keyFilter = ref('')

const cultureOptions = computed(() =>
  (summary.value?.cultureBreakdown ?? []).map((c) => ({
    value: c.culture,
    label: `${c.culture} (${c.missingKeyCount})`,
  })),
)

const filteredMissing = computed(() => {
  const q = keyFilter.value.trim().toLowerCase()
  if (!q) return missing.value
  return missing.value.filter(
    (m) => m.key.toLowerCase().includes(q) || m.culture.toLowerCase().includes(q),
  )
})

const columns: DataTableColumns<MissingTranslationDto> = [
  {
    title: () => t('cols.culture'),
    key: 'culture',
    width: 120,
    sorter: 'default',
  },
  {
    title: () => t('cols.key'),
    key: 'key',
    ellipsis: { tooltip: true },
  },
  {
    title: () => t('cols.access'),
    key: 'accessCount',
    width: 100,
    align: 'right',
    sorter: (a, b) => a.accessCount - b.accessCount,
    defaultSortOrder: 'descend',
  },
  {
    title: () => t('cols.firstSeen'),
    key: 'firstAccessTime',
    width: 170,
    render: (row) => formatDate(row.firstAccessTime),
  },
  {
    title: () => t('cols.lastSeen'),
    key: 'lastAccessTime',
    width: 170,
    render: (row) => formatDate(row.lastAccessTime),
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [s, m] = await Promise.all([
      bridge.getSummary(),
      bridge.getMissing(cultureFilter.value ?? undefined),
    ])
    summary.value = s
    missing.value = m
  } catch {
    summary.value = null
    missing.value = []
  } finally {
    loading.value = false
  }
}

async function downloadStubs(): Promise<void> {
  try {
    const stubs = await bridge.exportMissing(cultureFilter.value ?? undefined)
    const json = JSON.stringify(stubs, null, 2)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = cultureFilter.value
      ? `missing-translations-${cultureFilter.value}.json`
      : 'missing-translations.json'
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  } catch { /* bridge swallows */ }
}

async function clearAll(): Promise<void> {
  try {
    await bridge.clearMissing()
    await refresh()
  } catch { /* bridge swallows */ }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
/* Layout shell from TContentPage (scroll="fill") + shared `.t-table-card` utilities. */
.t-i18n-page__kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
@media (max-width: 900px) {
  .t-i18n-page__kpis { grid-template-columns: repeat(2, 1fr); }
}
.t-i18n-page__breakdown {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-i18n-page__breakdown-label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-i18n-page__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.t-i18n-page__empty {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
