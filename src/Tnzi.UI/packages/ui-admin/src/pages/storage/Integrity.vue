<template>
  <!--
    Integrity — file-integrity verification dashboard (TContentPage, no back).
    A "Verify" button triggers `integrity.batchVerify(maxFiles)`; results land
    in a KPI strip (TotalChecked / Healthy / Missing+Corrupted / Errors) and a
    problem-files table (the backend only returns problematic rows in
    `problems`). Single-file verify is reachable from the Files page actions.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NInputNumber
        v-model:value="maxFiles"
        size="small"
        :min="1"
        :max="10000"
        :step="100"
        class="w-130px"
      />
      <NButton size="small" type="primary" :loading="loading" @click="runVerify">
        <template #icon><TSvgIcon icon="mdi:shield-check-outline" :size="16" /></template>
        {{ t('actions.verify') }}
      </NButton>
    </template>

    <TKpiRow cols="1 s:2 m:4">
      <TKpiCard :label="t('kpi.totalChecked')" :value="result ? result.totalChecked : null" icon="mdi:file-search-outline" />
      <TKpiCard :label="t('kpi.healthy')" :value="result ? result.healthy : null" tone="success" icon="mdi:check-circle-outline" />
      <TKpiCard :label="t('kpi.problems')" :value="result ? problemCount : null" tone="error" icon="mdi:alert-circle-outline" />
      <TKpiCard :label="t('kpi.errors')" :value="result ? result.errors : null" tone="warning" icon="mdi:help-circle-outline" />
    </TKpiRow>

    <NCard size="small" :bordered="false" class="t-table-card" :title="t('problems.title')">
      <TResponsiveTable
        :columns="problemColumns"
        :data="result?.problems ?? []"
        :loading="loading"
        :row-key="(r: FileIntegrityResultDto) => r.fileId"
        :pagination="false"
        :flex-height="true"
        size="small"
        :empty-text="result ? t('problems.none') : t('problems.runFirst')"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton, NCard, NInputNumber } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildProblemColumns } from './integrity-config'
import type { BatchIntegrityResultDto, FileIntegrityResultDto } from '@tnzi/core/services/storage'

const t = (key: string) => translatePageKey('storage.integrity', key)
const message = useSafeMessage()
const bridge = createStorageBridge({ client: useAdminClient() })

const maxFiles = ref(100)
const loading = ref(false)
const result = ref<BatchIntegrityResultDto | null>(null)

const problemCount = computed(() =>
  result.value ? (result.value.missing ?? 0) + (result.value.corrupted ?? 0) : 0,
)

const problemColumns = buildProblemColumns(t)

async function runVerify(): Promise<void> {
  loading.value = true
  try {
    result.value = await bridge.integrity.batchVerify(maxFiles.value ?? 100)
    message.success(t('actions.verifyDone'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loading.value = false
  }
}
</script>
