<template>
  <!--
    LoginSecurity - surfaces /admin/login-security/{overview,frequent-failures}
    for the Tnzi.Identity module. Read-only dashboard: KPI strip + failure-rate
    bar + a table of users hitting the failure threshold. Built on TContentPage
    (white page-header + small toolbar in #actions); the failures table fills
    the residual height via the shared `t-table-card` + flex-height pattern.
  -->
  <TContentPage
    :title="t('title')"
    icon="mdi:shield-account-outline"
    :translate="t"
    scroll="fill"
  >
    <template #actions>
      <NSelect
        v-model:value="hours"
        :options="windowOptions"
        size="small"
        class="w-140px"
        @update:value="refresh"
      />
      <NInputNumber
        v-model:value="minFailures"
        :min="1"
        :max="50"
        size="small"
        class="w-150px"
        @update:value="refresh"
      >
        <template #prefix>{{ t('toolbar.minFailures') }}:</template>
      </NInputNumber>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <div class="t-sec-page">
      <TKpiRow class="t-sec-page__kpis" cols="1 s:2 m:4 l:7">
        <TKpiCard :label="t('kpi.attempts')" :value="overview?.totalLoginAttempts ?? null" />
        <TKpiCard
          :label="t('kpi.success')"
          :value="overview?.successfulLogins ?? null"
          icon="mdi:check-circle-outline"
          tone="success"
        />
        <TKpiCard
          :label="t('kpi.failures')"
          :value="overview?.failedLogins ?? null"
          icon="mdi:alert-circle-outline"
          tone="error"
        />
        <TKpiCard
          :label="t('kpi.failureRate')"
          :value="formatPercent(overview?.failureRate)"
          :tone="failureTone"
        />
        <TKpiCard :label="t('kpi.uniqueUsers')" :value="overview?.distinctUsers ?? null" />
        <TKpiCard :label="t('kpi.uniqueIps')" :value="overview?.distinctIpAddresses ?? null" />
        <TKpiCard
          :label="t('kpi.lockedOut')"
          :value="overview?.lockedOutUsers ?? null"
          :tone="(overview?.lockedOutUsers ?? 0) > 0 ? 'warning' : 'default'"
        >
          <template #extra>
            <NTag v-if="(overview?.lockedOutUsers ?? 0) > 0" size="tiny" type="warning" :bordered="false">
              {{ t('kpi.actionNeeded') }}
            </NTag>
          </template>
        </TKpiCard>
      </TKpiRow>

      <NCard :title="t('sections.failureRate')" size="small" :bordered="false">
        <div class="t-sec-page__progress-wrap">
          <NProgress
            type="line"
            :percentage="Math.min(100, Math.max(0, overview?.failureRate ?? 0))"
            :status="failureProgressStatus"
            :indicator-placement="'inside'"
            :height="22"
          />
          <div class="t-sec-page__progress-hint">
            {{ t('sections.failureRateHint', { hours: overview?.timeRangeHours ?? hours }) }}
          </div>
        </div>
      </NCard>

      <NCard
        :title="t('sections.frequentFailures')"
        size="small"
        :bordered="false"
        class="t-sec-page__table-card t-table-card"
      >
        <template #header-extra>
          <NTag :bordered="false" size="small">
            {{ t('sections.threshold', { n: minFailures }) }}
          </NTag>
        </template>
        <TResponsiveTable
          :columns="failuresColumns"
          :data="failures"
          :loading="loading"
          :pagination="failuresPagination"
          :row-actions="rowActions"
          :row-actions-title="t('cols.actions')"
          :translate="t"
          :flex-height="true"
          :bordered="false"
          size="small"
        />
      </NCard>
    </div>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NCard,
  NInputNumber,
  NProgress,
  NSelect,
  NTag,
} from 'naive-ui'
import TResponsiveTable, { type TResponsivePagination } from '../../components/data/TResponsiveTable.vue'
import { type RowAction } from '../../headless/rowActions'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import TContentPage from '../../components/layout/TContentPage.vue'
import { useAdminClient } from '../../plugin/client'
import {
  createLoginSecurityBridge,
  type SecurityOverviewDto,
  type UserFailedLoginSummaryDto,
} from '../../services/bridges/login-security-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useSafeMessage } from '../_shared/safeMessage'
import { makePageTranslator } from '../_shared/translate'

const client = useAdminClient()
const bridge = createLoginSecurityBridge({ client })
// Identity bridge is the source of truth for user lock/unlock - the
// LoginSecurity backend doesn't expose enforcement endpoints (it's a
// read-only diagnostic surface), but identity's user admin does, and
// the user IDs in the failed-login summary line up directly.
const identityBridge = createIdentityBridge({ client })
const message = useSafeMessage()
const router = useRouter()
const t = makePageTranslator('identity.loginSecurity')
const { can } = usePermissionGuard()

const loading = ref(false)
const overview = ref<SecurityOverviewDto | null>(null)
const failures = ref<UserFailedLoginSummaryDto[]>([])
const hours = ref(24)
const minFailures = ref(3)
const windowOptions = [
  { value: 1, label: t('window.1h') },
  { value: 6, label: t('window.6h') },
  { value: 24, label: t('window.24h') },
  { value: 72, label: t('window.72h') },
  { value: 168, label: t('window.7d') },
]

// Client-side pagination with a "Total N" prefix so admins can see the full
// failure-row count at a glance (mirrors TListShell's pager prefix).
const failuresPagination: TResponsivePagination = {
  pageSize: 15,
  prefix: ({ itemCount }: { itemCount?: number }) => `${t('admin.crud.total')} ${itemCount ?? 0}`,
}

const failureTone = computed<'success' | 'warning' | 'error'>(() => {
  const rate = overview.value?.failureRate ?? 0
  if (rate >= 50) return 'error'
  if (rate >= 20) return 'warning'
  return 'success'
})
const failureProgressStatus = computed<'success' | 'warning' | 'error'>(() => {
  const rate = overview.value?.failureRate ?? 0
  if (rate >= 50) return 'error'
  if (rate >= 20) return 'warning'
  return 'success'
})

function formatPercent(n: number | undefined | null): string {
  if (n == null) return EMPTY_DASH
  return `${n.toFixed(2)}%`
}

// Track which rows are currently being acted on so we can disable the
// button per-row instead of locking the whole table while a single
// lock/unlock round-trip runs. Keyed by userId.
const pendingRows = ref(new Set<string>())

async function handleLock(row: UserFailedLoginSummaryDto): Promise<void> {
  const { userId } = row
  if (!userId || pendingRows.value.has(userId)) return
  pendingRows.value.add(userId)
  try {
    // `until=null` = permanent lock (per identity-bridge contract). Admin
    // can later unlock from this same table once the issue is resolved.
    await identityBridge.users.lock(userId, null, 'Locked from LoginSecurity dashboard')
    message.success(t('rowActions.lockSuccess'))
    await refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    pendingRows.value.delete(userId)
  }
}

function goToLoginLogs(row: UserFailedLoginSummaryDto): void {
  // Pre-filter the login-logs page by userId so admins can drill into the
  // raw events behind a row's failure count in one click.
  void router.push({ name: 'identity.loginLogs', query: { userId: row.userId } })
}

const failuresColumns = computed<DataTableColumns<UserFailedLoginSummaryDto>>(() => [
  {
    title: () => t('cols.failureCount'),
    key: 'failureCount',
    width: 110,
    align: 'right',
    sorter: (a, b) => a.failureCount - b.failureCount,
    defaultSortOrder: 'descend',
    render: (row) => h(NTag, { size: 'small', bordered: false, type: 'error' }, () => String(row.failureCount)),
  },
  {
    title: () => t('cols.userName'),
    key: 'userName',
    minWidth: 130,
    render: (row) => row.userName ?? EMPTY_DASH,
  },
  {
    title: () => t('cols.email'),
    key: 'email',
    minWidth: 180,
    render: (row) => row.email ?? EMPTY_DASH,
  },
  {
    title: () => t('cols.lastFailure'),
    key: 'lastFailureTime',
    width: 180,
    render: (row) => formatDate(row.lastFailureTime),
  },
  {
    title: () => t('cols.ipAddresses'),
    key: 'ipAddresses',
    render: (row) =>
      h(
        'div',
        { class: 'flex flex-wrap gap-4px' },
        (row.ipAddresses ?? []).slice(0, 8).map((ip: string) =>
          h(NTag, { size: 'tiny', bordered: false }, () => ip),
        ),
      ),
  },
])

// Declarative operation column: drill into the raw login logs (no confirm)
// + permanently lock the account (confirm gated, disabled while a lock/unlock
// round-trip for that row is in flight). Reuses the existing handlers.
const rowActions: RowAction<UserFailedLoginSummaryDto>[] = [
  {
    key: 'viewLogs',
    label: 'rowActions.viewLogs',
    icon: 'mdi:history',
    onClick: (row) => goToLoginLogs(row),
  },
  {
    key: 'lock',
    label: 'rowActions.lock',
    icon: 'mdi:lock-outline',
    type: 'warning',
    show: () => can('user.update'),
    confirm: (row) => t('rowActions.confirmLock', { user: row.userName ?? row.userId }),
    disabled: (row) => pendingRows.value.has(row.userId),
    onClick: (row) => void handleLock(row),
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [ov, fl] = await Promise.all([
      bridge.getOverview(hours.value),
      bridge.getFrequentFailures(hours.value, minFailures.value),
    ])
    overview.value = ov
    failures.value = fl
  } catch (e) {
    // Surface the error so "no data" (genuinely empty) is distinguishable
    // from a failing endpoint - previously this was swallowed silently.
    overview.value = null
    failures.value = []
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
.t-sec-page {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-sec-page__kpis {
  flex-shrink: 0;
}
/* The failures table card claims the residual height; `t-table-card`
   (polish.css) makes its NDataTable flex-fill so the built-in pager pins to
   the card bottom. */
.t-sec-page__table-card {
  flex: 1 1 auto;
  min-height: 0;
}
.t-sec-page__progress-wrap {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-sec-page__progress-hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
