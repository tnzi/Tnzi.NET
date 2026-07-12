<script setup lang="ts">
/**
 * `TWidgetIdentityStats` — active sessions + online users + total users.
 *
 * Three values pulled from `identity-bridge`:
 *   - `sessions.statistics()` → active session count + online user count
 *   - `users.fetch({ pageSize: 1 })` → total user count (via totalCount header)
 *
 * Renders a compact stat row so it fits in a 1/3-width grid cell. Falls
 * back to em-dash on bridge errors so a missing endpoint doesn't tank
 * the entire workbench.
 */
import { ref } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'

const totalUsers = ref<number | null>(null)
const activeSessions = ref<number | null>(null)
const onlineUsers = ref<number | null>(null)

const bridge = createIdentityBridge({ client: useAdminClient() })
const authStore = useAdminAuthStore()

useWidgetData(async () => {
  // The widget itself is gated by user.view (preset `permission`), but
  // `sessions.statistics()` sits behind session.view (Technical) - probe it
  // only when the code is held (super-user bypass + fail-open pre-load, same
  // semantics as the sidebar), so a business admin's dashboard doesn't fire a
  // doomed request that lands as console 403 noise. Skipped → em-dash rows.
  const canSeeSessions =
    authStore.isSuperUser || authStore.userInfo === null || authStore.hasPermission('session.view')

  // Parallel fetch — neither depends on the other.
  const [stats, users] = await Promise.allSettled([
    canSeeSessions ? bridge.sessions.statistics() : Promise.resolve(undefined),
    bridge.users.fetch({ pageIndex: 1, pageSize: 1, searchText: '', filters: {} }),
  ])
  // Even a permitted call can fulfil with an undefined/empty body (failed
  // envelope resolves instead of rejecting), so guard `stats.value` before
  // reading it (else "Cannot read properties of undefined" tanks the whole
  // widget). The session rows then stay em-dash while total users renders.
  if (stats.status === 'fulfilled' && stats.value) {
    activeSessions.value = stats.value.activeSessionCount ?? 0
    onlineUsers.value = stats.value.onlineUserCount ?? 0
  }
  if (users.status === 'fulfilled' && users.value) {
    totalUsers.value = users.value.totalCount ?? 0
  }
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}

function fmt(n: number | null): string {
  return n === null ? '—' : n.toLocaleString()
}
</script>

<template>
  <div class="t-widget-id-stats">
    <div class="t-widget-id-stats__cell">
      <span class="t-widget-id-stats__icon" data-tone="primary">
        <TSvgIcon icon="mdi:account-group" :size="20" />
      </span>
      <div class="t-widget-id-stats__text">
        <span class="t-widget-id-stats__label">{{ t('admin.widgets.identityStats.totalUsers', 'Total users') }}</span>
        <span class="t-widget-id-stats__value">{{ fmt(totalUsers) }}</span>
      </div>
    </div>
    <div class="t-widget-id-stats__cell">
      <span class="t-widget-id-stats__icon" data-tone="success">
        <TSvgIcon icon="mdi:account-clock" :size="20" />
      </span>
      <div class="t-widget-id-stats__text">
        <span class="t-widget-id-stats__label">{{ t('admin.widgets.identityStats.onlineUsers', 'Online users') }}</span>
        <span class="t-widget-id-stats__value">{{ fmt(onlineUsers) }}</span>
      </div>
    </div>
    <div class="t-widget-id-stats__cell">
      <span class="t-widget-id-stats__icon" data-tone="info">
        <TSvgIcon icon="mdi:devices" :size="20" />
      </span>
      <div class="t-widget-id-stats__text">
        <span class="t-widget-id-stats__label">{{ t('admin.widgets.identityStats.activeSessions', 'Active sessions') }}</span>
        <span class="t-widget-id-stats__value">{{ fmt(activeSessions) }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-widget-id-stats {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-widget-id-stats__cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-widget-id-stats__icon {
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
.t-widget-id-stats__icon[data-tone='info'] {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-widget-id-stats__icon[data-tone='success'] {
  background: rgb(var(--tnzi-success-rgb, 24 160 88) / 0.12);
  color: var(--tnzi-success);
}
.t-widget-id-stats__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-widget-id-stats__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-id-stats__value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
</style>
