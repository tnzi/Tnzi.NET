<script setup lang="ts">
/**
 * `TWidgetChatStats` — chat session count snapshot.
 *
 * Pulls `chat-bridge.sessions.fetch({pageSize:1})` for the total session
 * count, then a second call with a 24-h filter for "today's sessions".
 * The 24-h filter follows the standard CRUD filter contract; backends
 * that ignore unknown filters fall back to total = today (harmless).
 */
import { ref } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createChatBridge } from '../../services/bridges/chat-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

const total = ref<number | null>(null)
const today = ref<number | null>(null)
const bridge = createChatBridge({ client: useAdminClient() })

useWidgetData(async () => {
  const since = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString()
  const [allRes, todayRes] = await Promise.allSettled([
    bridge.sessions.fetch({ pageIndex: 1, pageSize: 1, searchText: '', filters: {} }),
    bridge.sessions.fetch({
      pageIndex: 1,
      pageSize: 1,
      searchText: '',
      filters: { createdSince: since } as Record<string, unknown>,
    }),
  ])
  if (allRes.status === 'fulfilled') total.value = allRes.value.totalCount ?? 0
  if (todayRes.status === 'fulfilled') today.value = todayRes.value.totalCount ?? 0
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}

function fmt(n: number | null): string {
  return n === null ? '—' : n.toLocaleString()
}
</script>

<template>
  <div class="t-widget-chat">
    <div class="t-widget-chat__cell">
      <span class="t-widget-chat__icon" data-tone="primary">
        <TSvgIcon icon="mdi:forum" :size="20" />
      </span>
      <div class="t-widget-chat__text">
        <span class="t-widget-chat__label">{{ t('admin.widgets.chat.totalSessions', 'Total sessions') }}</span>
        <span class="t-widget-chat__value">{{ fmt(total) }}</span>
      </div>
    </div>
    <div class="t-widget-chat__cell">
      <span class="t-widget-chat__icon" data-tone="info">
        <TSvgIcon icon="mdi:clock-fast" :size="20" />
      </span>
      <div class="t-widget-chat__text">
        <span class="t-widget-chat__label">{{ t('admin.widgets.chat.last24h', 'Last 24h') }}</span>
        <span class="t-widget-chat__value">{{ fmt(today) }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-widget-chat {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-widget-chat__cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-widget-chat__icon {
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
.t-widget-chat__icon[data-tone='info'] {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-widget-chat__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-widget-chat__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-chat__value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
</style>
