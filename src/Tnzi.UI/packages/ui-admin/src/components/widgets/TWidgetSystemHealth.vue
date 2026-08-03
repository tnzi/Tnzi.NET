<script setup lang="ts">
/**
 * `TWidgetSystemHealth` - system health snapshot.
 *
 * Polls `/admin/diagnostics/exceptions/summary?minutes=60` and derives a
 * coarse status from the error count returned by `ExceptionStatisticsService`:
 *   - 0 errors in last hour     → `ok`
 *   - 1-9 errors in last hour   → `degraded`
 *   - ≥ 10 errors in last hour  → `down`
 *
 * The diagnostics endpoint ships with every `HostingModule` app, so this
 * widget works on any stack that loads `Tnzi.AspNetCore` (i.e. all of them).
 * Environment + endpoint stay client-derived from `window.location` because
 * they're inherent to the page that already loaded - no extra round-trip
 * for data the browser already has.
 *
 * Mark this widget's `permission` as `'system.health.view'` if you need to
 * gate it.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { ref } from 'vue'
import { NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { useWidgetData } from '../../headless/useWidgetData'
import { translatePageKey } from '../../i18n/translate'

interface HealthSnapshot {
  status: 'ok' | 'degraded' | 'down'
  errorsLastHour: number
  version: string
  environment: string
  endpoint: string
}

interface ExceptionSummaryDto {
  totalCount?: number
  uniqueExceptionCount?: number
  windowMinutes?: number
}

const data = ref<HealthSnapshot | null>(null)
const client = useAdminClient()

function classifyStatus(errors: number): HealthSnapshot['status'] {
  if (errors === 0) return 'ok'
  if (errors < 10) return 'degraded'
  return 'down'
}

useWidgetData(async () => {
  // The diagnostics endpoint returns either a raw `ExceptionSummaryDto`
  // or an `ApiResult<ExceptionSummaryDto>` envelope depending on the
  // pipeline filter chain. Accept both shapes.
  let errors = 0
  try {
    const res = await client.get<ExceptionSummaryDto | { data?: ExceptionSummaryDto }>(
      '/admin/diagnostics/exceptions/summary?minutes=60',
    )
    const summary = (res && typeof res === 'object' && 'data' in res && res.data
      ? res.data
      : res) as ExceptionSummaryDto | undefined
    errors = summary?.totalCount ?? 0
  } catch {
    // Module not loaded or permission denied - fall back to "unknown",
    // which we encode as `degraded` (not `ok`, since we can't confirm
    // healthy state).
    errors = -1
  }
  data.value = {
    status: errors < 0 ? 'degraded' : classifyStatus(errors),
    errorsLastHour: Math.max(0, errors),
    version: 'dev',
    environment: typeof window !== 'undefined' && window.location.hostname.includes('localhost')
      ? 'Development'
      : 'Production',
    endpoint: typeof window !== 'undefined' ? window.location.host : EMPTY_DASH,
  }
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}
</script>

<template>
  <div v-if="data" class="t-widget-system-health">
    <div class="t-widget-system-health__row">
      <span class="t-widget-system-health__label">
        <TSvgIcon icon="mdi:heart-pulse" :size="16" />
        {{ t('admin.widgets.systemHealth.status', 'Status') }}
      </span>
      <NTag :type="data.status === 'ok' ? 'success' : data.status === 'degraded' ? 'warning' : 'error'" size="small">
        {{ t(`admin.shared.status.${data.status === 'ok' ? 'success' : data.status === 'degraded' ? 'pending' : 'failed'}`, data.status.toUpperCase()) }}
      </NTag>
    </div>
    <div class="t-widget-system-health__row">
      <span class="t-widget-system-health__label">
        <TSvgIcon icon="mdi:alert-circle-outline" :size="16" />
        {{ t('admin.widgets.systemHealth.errorsLastHour', 'Errors (1h)') }}
      </span>
      <span class="t-widget-system-health__value">{{ data.errorsLastHour.toLocaleString() }}</span>
    </div>
    <div class="t-widget-system-health__row">
      <span class="t-widget-system-health__label">
        <TSvgIcon icon="mdi:server-outline" :size="16" />
        {{ t('admin.widgets.systemHealth.environment', 'Environment') }}
      </span>
      <span class="t-widget-system-health__value">{{ data.environment }}</span>
    </div>
    <div class="t-widget-system-health__row">
      <span class="t-widget-system-health__label">
        <TSvgIcon icon="mdi:link-variant" :size="16" />
        {{ t('admin.widgets.systemHealth.endpoint', 'Endpoint') }}
      </span>
      <span class="t-widget-system-health__value" :title="data.endpoint">{{ data.endpoint }}</span>
    </div>
  </div>
</template>

<style scoped>
.t-widget-system-health {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-widget-system-health__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
}
.t-widget-system-health__label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-system-health__value {
  font-weight: 500;
  color: var(--tnzi-base-text);
  font-variant-numeric: tabular-nums;
  max-width: 60%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
