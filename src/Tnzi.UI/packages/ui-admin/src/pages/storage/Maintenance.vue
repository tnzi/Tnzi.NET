<template>
  <!--
    Maintenance - storage housekeeping (TContentPage, no back). Lists
    unreferenced temporary files older than N hours (`cleanup.temporaryFiles`)
    and exposes a Cleanup trigger (`cleanup.trigger`) that physically deletes
    them. The age threshold drives both the listing and the cleanup.

    Also hosts the public-flag backfill: files referenced from a field the
    backend declares `[FileField(Public = true)]` (avatars) should be publicly
    readable, but that declaration only applies to references written after it
    landed. This repairs the ones stored earlier, which would otherwise render
    as a broken image.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <div class="flex items-center gap-6px">
        <span class="text-12px text-muted">{{ t('olderThan') }}</span>
        <NInputNumber v-model:value="olderThanHours" size="small" :min="0" :max="8760" :step="1" class="w-110px" />
        <span class="text-12px text-muted">{{ t('hours') }}</span>
      </div>
      <NButton size="small" :loading="loading" @click="loadTemporary">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
        {{ t('actions.refresh') }}
      </NButton>
      <NPopconfirm v-if="can('storage.file.delete')" @positive-click="runCleanup">
        <template #trigger>
          <NButton size="small" type="error" ghost :loading="cleaning">
            <template #icon><TSvgIcon icon="mdi:broom" :size="16" /></template>
            {{ t('actions.cleanup') }}
          </NButton>
        </template>
        {{ t('actions.confirmCleanup') }}
      </NPopconfirm>
    </template>

    <TKpiRow cols="1 s:2">
      <TKpiCard :label="t('kpi.count')" :value="files.length" icon="mdi:file-clock-outline" tone="warning" />
      <TKpiCard :label="t('kpi.totalSize')" :value="totalSizeLabel" icon="mdi:harddisk" />
    </TKpiRow>

    <NCard
      v-if="can('storage.file.update')"
      size="small"
      :bordered="false"
      class="t-table-card"
      :title="t('publicFlags.title')"
    >
      <div class="flex flex-wrap items-center justify-between gap-12px">
        <span class="text-13px text-muted max-w-640px">{{ t('publicFlags.hint') }}</span>
        <NButton size="small" secondary :loading="syncing" @click="runPublicFlagSync">
          <template #icon><TSvgIcon icon="mdi:earth-arrow-right" :size="16" /></template>
          {{ t('publicFlags.action') }}
        </NButton>
      </div>
    </NCard>

    <NCard size="small" :bordered="false" class="t-table-card" :title="t('list.title')">
      <TResponsiveTable
        :columns="temporaryColumns"
        :data="files"
        :loading="loading"
        :row-key="(r: FileRecordDto) => r.id"
        :pagination="false"
        :flex-height="true"
        size="small"
        :empty-text="t('list.empty')"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { NButton, NCard, NInputNumber, NPopconfirm } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { buildTemporaryColumns } from './maintenance-config'
import { formatFileSize } from '@tnzi/core'
import type { FileRecordDto } from '@tnzi/core/services/storage'

const t = makePageTranslator('storage.maintenance')
const message = useSafeMessage()
const bridge = createStorageBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()

const olderThanHours = ref(24)
const loading = ref(false)
const cleaning = ref(false)
const syncing = ref(false)
const files = ref<FileRecordDto[]>([])

const temporaryColumns = buildTemporaryColumns(t)

const totalSizeLabel = computed(() =>
  formatFileSize(files.value.reduce((sum, f) => sum + (f.size ?? 0), 0)),
)

async function loadTemporary(): Promise<void> {
  loading.value = true
  try {
    files.value = await bridge.cleanup.temporaryFiles(olderThanHours.value ?? undefined)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    files.value = []
  } finally {
    loading.value = false
  }
}

async function runCleanup(): Promise<void> {
  if (!can('storage.file.delete')) return
  cleaning.value = true
  try {
    const count = await bridge.cleanup.trigger(olderThanHours.value ?? undefined)
    message.success(t('actions.cleanupDone', { n: count }))
    await loadTemporary()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    cleaning.value = false
  }
}

async function runPublicFlagSync(): Promise<void> {
  if (!can('storage.file.update')) return
  syncing.value = true
  try {
    const count = await bridge.visibility.syncFromDeclarations()
    message.success(t('publicFlags.done', { n: count }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    syncing.value = false
  }
}

onMounted(() => {
  loadTemporary().catch(() => undefined)
})
</script>
