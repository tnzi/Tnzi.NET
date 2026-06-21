<script setup lang="ts">
/**
 * `TWidgetStorageUsage` — total file count + aggregate size.
 *
 * Wired to `storage-bridge.statistics.get()` (GET /admin/files/statistics →
 * FileStorageStatistics) which returns the exact `totalFiles` / `totalSize`
 * aggregates. This replaces the previous best-effort sampling estimate
 * (`files.fetch({ pageSize: 50 })` → average × count), so the size is no
 * longer approximate and the "≈" hint is gone.
 */
import { ref } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

const totalFiles = ref<number>(0)
const totalSizeBytes = ref<number>(0)
const loaded = ref(false)
const bridge = createStorageBridge({ client: useAdminClient() })

useWidgetData(async () => {
  const stats = await bridge.statistics.get()
  totalFiles.value = stats.totalFiles ?? 0
  totalSizeBytes.value = stats.totalSize ?? 0
  loaded.value = true
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}

function fmtBytes(bytes: number): string {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const exp = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  const value = bytes / Math.pow(1024, exp)
  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[exp]}`
}
</script>

<template>
  <div class="t-widget-storage">
    <div class="t-widget-storage__cell">
      <span class="t-widget-storage__icon" data-tone="primary">
        <TSvgIcon icon="mdi:file-multiple" :size="20" />
      </span>
      <div class="t-widget-storage__text">
        <span class="t-widget-storage__label">{{ t('admin.widgets.storage.totalFiles', 'Total files') }}</span>
        <span class="t-widget-storage__value">
          {{ loaded ? totalFiles.toLocaleString() : '—' }}
        </span>
      </div>
    </div>
    <div class="t-widget-storage__cell">
      <span class="t-widget-storage__icon" data-tone="info">
        <TSvgIcon icon="mdi:harddisk" :size="20" />
      </span>
      <div class="t-widget-storage__text">
        <span class="t-widget-storage__label">
          {{ t('admin.widgets.storage.totalSize', 'Total size') }}
        </span>
        <span class="t-widget-storage__value">
          {{ loaded ? fmtBytes(totalSizeBytes) : '—' }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-widget-storage {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-widget-storage__cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-widget-storage__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  flex-shrink: 0;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
}
.t-widget-storage__icon[data-tone='info'] {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-widget-storage__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-widget-storage__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-storage__value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
</style>
