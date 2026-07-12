<script setup lang="ts">
/**
 * `TWidgetAiUsage` — token / request / cost summary for AI agents.
 *
 * Calls `ai-bridge.usage.summary({})` (last-N days, server defaults) and
 * surfaces the three headline metrics: total tokens, total cost (USD),
 * request count. Use alongside `TWidgetUsageDashboard` (full charts) on
 * larger workbenches.
 */
import { ref } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { TCountTo } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createAiBridge } from '../../services/bridges/ai-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

const tokens = ref<number>(0)
const cost = ref<number>(0)
const requests = ref<number>(0)
const loaded = ref(false)
const bridge = createAiBridge({ client: useAdminClient() })

useWidgetData(async () => {
  // Backend `UsageAnalyticsService.GetSummaryAsync` 400s on empty
  // startTime/endTime strings — provide a 30-day window so the widget
  // shows real activity instead of falling through to zero on a
  // validation failure.
  const now = new Date()
  const thirtyDaysAgo = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000)
  const summary = await bridge.usage.summary({
    pageIndex: 1,
    pageSize: 1,
    searchText: '',
    filters: { startTime: thirtyDaysAgo.toISOString(), endTime: now.toISOString() },
  })
  // A 403/failed envelope can resolve to undefined - keep the placeholders.
  if (!summary) return
  tokens.value = summary.totalTokens ?? 0
  cost.value = summary.totalCostUsd ?? 0
  requests.value = summary.requestCount ?? 0
  loaded.value = true
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}
</script>

<template>
  <div class="t-widget-ai-usage">
    <div class="t-widget-ai-usage__cell">
      <span class="t-widget-ai-usage__icon" data-tone="primary">
        <TSvgIcon icon="mdi:chip" :size="20" />
      </span>
      <div class="t-widget-ai-usage__text">
        <span class="t-widget-ai-usage__label">{{ t('admin.widgets.aiUsage.tokens', 'Tokens') }}</span>
        <span class="t-widget-ai-usage__value">
          <TCountTo v-if="loaded" :end-value="tokens" :duration="1000" />
          <template v-else>—</template>
        </span>
      </div>
    </div>
    <div class="t-widget-ai-usage__cell">
      <span class="t-widget-ai-usage__icon" data-tone="success">
        <TSvgIcon icon="mdi:currency-usd" :size="20" />
      </span>
      <div class="t-widget-ai-usage__text">
        <span class="t-widget-ai-usage__label">{{ t('admin.widgets.aiUsage.cost', 'Cost (USD)') }}</span>
        <span class="t-widget-ai-usage__value">
          <TCountTo v-if="loaded" :end-value="cost" :decimals="2" :duration="1000" />
          <template v-else>—</template>
        </span>
      </div>
    </div>
    <div class="t-widget-ai-usage__cell">
      <span class="t-widget-ai-usage__icon" data-tone="info">
        <TSvgIcon icon="mdi:message-text" :size="20" />
      </span>
      <div class="t-widget-ai-usage__text">
        <span class="t-widget-ai-usage__label">{{ t('admin.widgets.aiUsage.requests', 'Requests') }}</span>
        <span class="t-widget-ai-usage__value">
          <TCountTo v-if="loaded" :end-value="requests" :duration="1000" />
          <template v-else>—</template>
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-widget-ai-usage {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-widget-ai-usage__cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-widget-ai-usage__icon {
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
.t-widget-ai-usage__icon[data-tone='info'] {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-widget-ai-usage__icon[data-tone='success'] {
  background: rgb(var(--tnzi-success-rgb, 24 160 88) / 0.12);
  color: var(--tnzi-success);
}
.t-widget-ai-usage__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-widget-ai-usage__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-ai-usage__value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
</style>
