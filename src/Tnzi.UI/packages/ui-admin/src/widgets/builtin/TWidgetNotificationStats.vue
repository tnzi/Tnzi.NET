<script setup lang="ts">
/**
 * `TWidgetNotificationStats` — notification volume snapshot.
 *
 * Pulls `notification-bridge.messages.fetch({pageSize:1})` to get the
 * total notification count (totalCount header), plus the count of
 * failed messages via a quick filter pass. Backend gap: there's no
 * dedicated `/stats` endpoint, so the failed count uses a status
 * filter that may or may not be honoured by the API; falls back to em
 * dash silently.
 */
import { ref } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

const total = ref<number | null>(null)
const failed = ref<number | null>(null)
const bridge = createNotificationBridge({ client: useAdminClient() })

useWidgetData(async () => {
  const [allRes, failedRes] = await Promise.allSettled([
    bridge.messages.fetch({ pageIndex: 1, pageSize: 1, searchText: '', filters: {} }),
    bridge.messages.fetch({
      pageIndex: 1,
      pageSize: 1,
      searchText: '',
      filters: { status: 'Failed' } as Record<string, unknown>,
    }),
  ])
  if (allRes.status === 'fulfilled') total.value = allRes.value.totalCount ?? 0
  if (failedRes.status === 'fulfilled') failed.value = failedRes.value.totalCount ?? 0
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}

function fmt(n: number | null): string {
  return n === null ? '—' : n.toLocaleString()
}
</script>

<template>
  <div class="t-widget-notif">
    <div class="t-widget-notif__cell">
      <span class="t-widget-notif__icon" data-tone="primary">
        <TSvgIcon icon="mdi:email-multiple" :size="20" />
      </span>
      <div class="t-widget-notif__text">
        <span class="t-widget-notif__label">{{ t('admin.widgets.notifications.total', 'Total messages') }}</span>
        <span class="t-widget-notif__value">{{ fmt(total) }}</span>
      </div>
    </div>
    <div class="t-widget-notif__cell">
      <span class="t-widget-notif__icon" data-tone="error">
        <TSvgIcon icon="mdi:alert-circle-outline" :size="20" />
      </span>
      <div class="t-widget-notif__text">
        <span class="t-widget-notif__label">{{ t('admin.widgets.notifications.failed', 'Failed') }}</span>
        <span class="t-widget-notif__value">{{ fmt(failed) }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-widget-notif {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-widget-notif__cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-widget-notif__icon {
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
.t-widget-notif__icon[data-tone='error'] {
  background: rgb(var(--tnzi-error-rgb, 208 48 80) / 0.12);
  color: var(--tnzi-error);
}
.t-widget-notif__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-widget-notif__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-notif__value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
</style>
