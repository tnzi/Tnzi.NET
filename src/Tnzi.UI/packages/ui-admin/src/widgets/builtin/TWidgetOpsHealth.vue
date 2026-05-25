<script setup lang="ts">
/**
 * `TWidgetOpsHealth` — ops-data rollup tile for the Workbench.
 *
 * Aggregates five live signals into one card so admins see the platform
 * pulse without context-switching:
 *   • Exception count (last 60 min)            via /admin/diagnostics/exceptions/summary
 *   • Performance P95                          via /admin/performance/summary
 *   • SignalR online users                     via /admin/signalr/stats
 *   • AI.Channels module enabled / adapter count via /admin/channels/status
 *   • Login failure rate (last 24 h)           via /admin/login-security/overview
 *
 * Every probe is independent — failures (404 / 401 / module not loaded)
 * surface as "—" on the corresponding row so a single missing endpoint
 * never blanks out the whole widget.
 */
import { ref } from 'vue'
import { NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import {
  createDiagnosticsBridge,
} from '../../services/bridges/diagnostics-bridge'
import {
  createPerformanceBridge,
} from '../../services/bridges/performance-bridge'
import {
  createSignalRBridge,
} from '../../services/bridges/signalr-bridge'
import {
  createChannelsBridge,
} from '../../services/bridges/channels-bridge'
import {
  createLoginSecurityBridge,
} from '../../services/bridges/login-security-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

interface Snapshot {
  exceptions: number | null
  p95Ms: number | null
  onlineUsers: number | null
  channelsEnabled: boolean | null
  adapterCount: number | null
  failureRate: number | null
  lockedOut: number | null
}

const client = useAdminClient()
const diagnostics = createDiagnosticsBridge({ client })
const performance = createPerformanceBridge({ client })
const signalr = createSignalRBridge({ client })
const channels = createChannelsBridge({ client })
const loginSec = createLoginSecurityBridge({ client })

const data = ref<Snapshot>({
  exceptions: null,
  p95Ms: null,
  onlineUsers: null,
  channelsEnabled: null,
  adapterCount: null,
  failureRate: null,
  lockedOut: null,
})

async function safeFetch<T>(p: Promise<T>): Promise<T | null> {
  try {
    return await p
  } catch {
    return null
  }
}

useWidgetData(async () => {
  const [exc, perf, sigStats, chanStatus, secOverview] = await Promise.all([
    safeFetch(diagnostics.exceptions.getSummary(60)),
    safeFetch(performance.getSummary(60)),
    safeFetch(signalr.getStats()),
    safeFetch(channels.channels.getStatus()),
    safeFetch(loginSec.getOverview(24)),
  ])
  data.value = {
    exceptions: exc?.totalCount ?? null,
    p95Ms: perf?.p95 ?? null,
    onlineUsers: sigStats?.onlineUserCount ?? null,
    channelsEnabled: chanStatus?.enabled ?? null,
    adapterCount: chanStatus?.adapters?.length ?? null,
    failureRate: secOverview?.failureRate ?? null,
    lockedOut: secOverview?.lockedOutUsers ?? null,
  }
})

function exceptionTone(n: number | null): 'success' | 'warning' | 'error' | 'default' {
  if (n == null) return 'default'
  if (n === 0) return 'success'
  if (n < 10) return 'warning'
  return 'error'
}
function failureTone(rate: number | null): 'success' | 'warning' | 'error' | 'default' {
  if (rate == null) return 'default'
  if (rate >= 50) return 'error'
  if (rate >= 20) return 'warning'
  return 'success'
}
function formatMs(v: number | null): string {
  if (v == null) return '—'
  if (v >= 1000) return `${(v / 1000).toFixed(2)} s`
  return `${v.toFixed(0)} ms`
}
function formatPercent(n: number | null): string {
  if (n == null) return '—'
  return `${n.toFixed(1)}%`
}
function formatNumber(n: number | null): string {
  if (n == null) return '—'
  return n.toLocaleString()
}
function formatAdapters(n: number | null): string {
  const count = n ?? 0
  const template = t('admin.widgets.opsHealth.adapters', '{n} adapter(s)')
  return template.replace('{n}', String(count))
}

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}
</script>

<template>
  <div class="t-widget-ops">
    <div class="t-widget-ops__row">
      <span class="t-widget-ops__label">
        <TSvgIcon icon="mdi:alert-circle-outline" :size="14" />
        {{ t('admin.widgets.opsHealth.exceptions', 'Errors (60m)') }}
      </span>
      <NTag :type="exceptionTone(data.exceptions)" size="small" :bordered="false">
        {{ formatNumber(data.exceptions) }}
      </NTag>
    </div>
    <div class="t-widget-ops__row">
      <span class="t-widget-ops__label">
        <TSvgIcon icon="mdi:speedometer" :size="14" />
        {{ t('admin.widgets.opsHealth.p95', 'P95 latency') }}
      </span>
      <span class="t-widget-ops__value">{{ formatMs(data.p95Ms) }}</span>
    </div>
    <div class="t-widget-ops__row">
      <span class="t-widget-ops__label">
        <TSvgIcon icon="mdi:broadcast" :size="14" />
        {{ t('admin.widgets.opsHealth.online', 'SignalR online') }}
      </span>
      <span class="t-widget-ops__value">{{ formatNumber(data.onlineUsers) }}</span>
    </div>
    <div class="t-widget-ops__row">
      <span class="t-widget-ops__label">
        <TSvgIcon icon="mdi:connection" :size="14" />
        {{ t('admin.widgets.opsHealth.channels', 'Channels') }}
      </span>
      <span class="t-widget-ops__value">
        <NTag v-if="data.channelsEnabled === null" size="tiny" :bordered="false">—</NTag>
        <NTag v-else :type="data.channelsEnabled ? 'success' : 'default'" size="tiny" :bordered="false">
          {{ data.channelsEnabled ? formatAdapters(data.adapterCount) : t('admin.widgets.opsHealth.disabled', 'disabled') }}
        </NTag>
      </span>
    </div>
    <div class="t-widget-ops__row">
      <span class="t-widget-ops__label">
        <TSvgIcon icon="mdi:shield-account-outline" :size="14" />
        {{ t('admin.widgets.opsHealth.failureRate', 'Login failure rate (24h)') }}
      </span>
      <NTag :type="failureTone(data.failureRate)" size="small" :bordered="false">
        {{ formatPercent(data.failureRate) }}
      </NTag>
    </div>
    <div v-if="(data.lockedOut ?? 0) > 0" class="t-widget-ops__row">
      <span class="t-widget-ops__label">
        <TSvgIcon icon="mdi:lock-outline" :size="14" />
        {{ t('admin.widgets.opsHealth.lockedOut', 'Locked-out users') }}
      </span>
      <NTag type="warning" size="small" :bordered="false">
        {{ formatNumber(data.lockedOut) }}
      </NTag>
    </div>
  </div>
</template>

<style scoped>
.t-widget-ops {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-widget-ops__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
}
.t-widget-ops__label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-ops__value {
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}
</style>
