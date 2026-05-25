<script setup lang="ts">
/**
 * `TWidgetStorageUsage` — total file count + aggregate size (best-effort).
 *
 * Backend gap: `storage-bridge` doesn't expose a `/admin/storage/stats`
 * endpoint yet, so we cheat — `files.fetch({ pageSize: 1 })` returns
 * `totalCount` for free. Aggregate size pulls the first page and sums
 * the visible rows, then projects a rough estimate; this is acknowledged
 * to be approximate (and labeled "≈" in the UI).
 */
import { ref, computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

const totalFiles = ref<number>(0)
const sampleSizeBytes = ref<number>(0)
const sampleCount = ref<number>(0)
const loaded = ref(false)
const bridge = createStorageBridge({ client: useAdminClient() })

useWidgetData(async () => {
  const result = await bridge.files.fetch({ pageIndex: 1, pageSize: 50, searchText: '', filters: {} })
  totalFiles.value = result.totalCount ?? 0
  const items = result.items ?? []
  sampleCount.value = items.length
  sampleSizeBytes.value = items.reduce<number>((sum, item) => {
    const size = (item as { size?: number; fileSize?: number }).size
      ?? (item as { fileSize?: number }).fileSize
      ?? 0
    return sum + (typeof size === 'number' ? size : 0)
  }, 0)
  loaded.value = true
})

const projectedSize = computed<number>(() => {
  if (sampleCount.value === 0 || totalFiles.value === 0) return 0
  const avg = sampleSizeBytes.value / sampleCount.value
  return Math.round(avg * totalFiles.value)
})

const isProjected = computed(() => totalFiles.value > sampleCount.value)

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
          {{ t('admin.widgets.storage.totalSize', 'Estimated size') }}
          <span v-if="isProjected" class="t-widget-storage__hint">≈</span>
        </span>
        <span class="t-widget-storage__value">
          {{ loaded ? fmtBytes(projectedSize) : '—' }}
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
.t-widget-storage__hint {
  margin-left: 4px;
  color: var(--tnzi-warning);
}
.t-widget-storage__value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
</style>
