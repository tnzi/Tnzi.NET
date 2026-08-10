<template>
  <TCrudPage
    :search-fields="recordAccessSearchFields"
    :state="crud"
    :all-columns="recordAccessColumns"
    :title="t('title')"
    :translate="t"
    :show-create="false"
    :show-batch="false"
    :detail-width="620"
  >
    <template #toolbarRight>
      <NButton size="small" secondary :loading="verifying" @click="verifyChain">
        <template #icon><TSvgIcon icon="mdi:shield-check-outline" :size="14" /></template>
        {{ t('actions.verify') }}
      </NButton>
      <NButton size="small" secondary :loading="loadingStats" @click="openStatistics">
        <template #icon><TSvgIcon icon="mdi:chart-bar" :size="14" /></template>
        {{ t('actions.statistics') }}
      </NButton>
    </template>

    <template #detail="{ data }">
      <TDescriptions v-if="data" :items="detailItems(data as RecordAccessDto)" />
    </template>
  </TCrudPage>

  <TModalShell v-model:show="showStats" :title="t('statistics.title')" size="medium">
    <p class="record-access-stats__hint">{{ t('statistics.hint') }}</p>
    <TEmpty v-if="!stats.length && !loadingStats" :text="t('statistics.empty')" />
    <NDataTable
      v-else
      :columns="statColumns"
      :data="stats"
      :loading="loadingStats"
      :bordered="false"
      size="small"
    />
  </TModalShell>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton, NDataTable } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import { EMPTY_DASH, TDescriptions, TEmpty, TModalShell, TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useSafeMessage } from '../_shared/safe-message'
import { makePageTranslator } from '../../i18n/translate'
import { useAdminClient } from '../../plugin/client'
import {
  createAuditBridge,
  type RecordAccessDto,
  type RecordAccessUserStatDto,
} from '../../services/bridges/audit-bridge'
import { recordAccessColumns, recordAccessSearchFields } from './record-access-config'

const bridge = createAuditBridge({ client: useAdminClient() })
const message = useSafeMessage()
const t = makePageTranslator('audit.recordAccess')

const crud = useCrudPage<RecordAccessDto>({
  pageId: 'audit.record-access',
  columns: recordAccessColumns,
  rowKey: (r) => String(r.id ?? ''),
  permission: 'audit.recordAccess',
  // The bridge flattens `filters` onto the request body itself, so the query
  // goes through as-is.
  fetchData: (query) => bridge.recordAccess.fetch(query),
})

const verifying = ref(false)
const showStats = ref(false)
const loadingStats = ref(false)
const stats = ref<RecordAccessUserStatDto[]>([])

/**
 * Chain verification.
 *
 * Verifies the chain of the row currently open (or the whole anonymous chain
 * when nothing is selected): chains are per user, so there is no single
 * "verify everything" call to make.
 */
async function verifyChain() {
  verifying.value = true
  try {
    const selected = crud.formModal.formData.value as RecordAccessDto | null
    await bridge.recordAccess.verify(selected?.userId ?? undefined)
    message.success(t('verify.ok'))
  } catch (error) {
    // A broken chain is a security finding, not a transient glitch - surface
    // the backend's message (it names the first bad sequence) rather than a
    // generic failure toast.
    message.error(error instanceof Error ? error.message : t('verify.failed'))
  } finally {
    verifying.value = false
  }
}

async function openStatistics() {
  showStats.value = true
  loadingStats.value = true
  try {
    stats.value = await bridge.recordAccess.userStatistics()
  } catch {
    stats.value = []
  } finally {
    loadingStats.value = false
  }
}

const statColumns = computed(() => [
  { title: t('columns.userName'), key: 'userName', render: (r: RecordAccessUserStatDto) => r.userName || t('anonymous') },
  { title: t('statistics.accessCount'), key: 'accessCount', width: 110 },
  { title: t('statistics.distinctRecordCount'), key: 'distinctRecordCount', width: 130 },
  {
    title: t('statistics.lastAccessTime'),
    key: 'lastAccessTime',
    width: 170,
    render: (r: RecordAccessUserStatDto) => formatDateTime(r.lastAccessTime),
  },
])

function detailItems(row: RecordAccessDto) {
  return [
    { label: t('columns.creationTime'), value: formatDateTime(row.creationTime) },
    { label: t('columns.userName'), value: row.userName || t('anonymous') },
    { label: t('columns.resourceType'), value: row.resourceType },
    { label: t('columns.resourceId'), value: row.resourceId },
    { label: t('columns.purpose'), value: row.purpose || EMPTY_DASH },
    { label: t('columns.sequence'), value: String(row.sequence) },
    { label: t('detail.hash'), value: row.hash },
  ]
}
</script>

<style scoped>
.record-access-stats__hint {
  margin: 0 0 12px;
  font-size: 12px;
  color: var(--t-text-color-3, #999);
  line-height: 1.6;
}
</style>
