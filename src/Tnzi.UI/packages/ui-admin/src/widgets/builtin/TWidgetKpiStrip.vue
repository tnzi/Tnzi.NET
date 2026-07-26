<script setup lang="ts">
/**
 * `TWidgetKpiStrip` - KPI hero row widget.
 *
 * Renders a responsive grid of gradient KPI cards (mirrors the cards used
 * in TDashboardPage). Hybrid data model:
 *   - **Props mode** - consumer passes `kpis: KpiCard[]` and the widget
 *     renders that array as-is (the original behaviour).
 *   - **Auto-fetch mode** - consumer omits `kpis` (or passes empty array)
 *     and the widget pulls 4 admin-meaningful metrics in parallel via the
 *     bridge layer: total users, total access logs, AI request count, and
 *     payment order count. Bridges that fail (module not loaded, no
 *     backend endpoint, permission denied) silently degrade to `0` so
 *     one failed bridge doesn't blank the whole strip.
 */
import { computed, ref, watch } from 'vue'
import { NGrid, NGi, NCard } from 'naive-ui'
import { TCountTo } from '@tnzi/ui'
import { TSvgIcon } from '@tnzi/ui'
import { maybeTranslate } from '../../pages/_shared/translate'
import type { KpiCard } from '../../components/pages/TDashboardPage.vue'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useModuleAvailability } from '../../headless/useModuleAvailability'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { createAiBridge } from '../../services/bridges/ai-bridge'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useWidgetData } from '../shell/useWidgetData'

interface Props {
  /**
   * KPI cards to render. When omitted / empty, the widget auto-fetches
   * 4 default admin metrics (users / access logs / AI requests / orders).
   */
  kpis?: KpiCard[]
}

const props = withDefaults(defineProps<Props>(), { kpis: () => [] })

const dynamicKpis = ref<KpiCard[]>([])
const displayKpis = computed<KpiCard[]>(() => (props.kpis.length ? props.kpis : dynamicKpis.value))
const isAuto = computed(() => props.kpis.length === 0)

const kpiSpan = computed<string>(() => {
  const count = displayKpis.value.length
  if (count === 4) return '24 s:12 m:6'
  if (count === 3) return '24 s:24 m:8'
  if (count === 2) return '24 s:12 m:12'
  if (count >= 5) return '24 s:12 m:6'
  return '24'
})

// Bridges are constructed once outside the fetcher so re-fetches don't
// re-instantiate API factories on every refresh tick.
const client = useAdminClient()
const authStore = useAdminAuthStore()
const identityBridge = createIdentityBridge({ client })
const systemBridge = createSystemBridge({ client })
const aiBridge = createAiBridge({ client })
const paymentBridge = createPaymentBridge({ client })

/**
 * Fail-open + super-user bypass, same rule as the sidebar filter: don't hide a
 * KPI before permissions load, and let super admins see all four.
 */
function canSee(permission: string): boolean {
  return authStore.isSuperUser || authStore.userInfo === null || authStore.hasPermission(permission)
}

// Per-KPI module gate - each tile fetches from a DIFFERENT optional backend
// module, so the widget-level `WidgetDef.module` tag can't cover it. Uses
// `canActivate` (no super-user bypass, defers while the availability probe is
// in flight) so no tile ever fires a fetch at a module the host didn't load.
const moduleAvailability = useModuleAvailability()

interface KpiSpec {
  key: string
  /** Permission gating this KPI's endpoint - dropped when the user lacks it. */
  permission: string
  /** Backend module this KPI's endpoint lives in - dropped when not loaded. */
  module: string
  title: string
  icon: string
  gradient: { start: string; end: string }
  load: () => Promise<number>
}

const { reload } = useWidgetData(async () => {
  if (!isAuto.value) return // consumer-supplied kpis - skip
  const emptyQuery = { pageIndex: 1, pageSize: 1, searchText: '', filters: {} }
  // AI usage summary backend (`UsageAnalyticsService.GetSummaryAsync`)
  // validates startTime <= endTime up-front and 400s on empty/zero
  // strings; pass a 30-day window so the widget gets a real number
  // instead of permanently rendering 0 because the bridge swallowed
  // a validation error.
  const now = new Date()
  const thirtyDaysAgo = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000)
  const aiUsageQuery = {
    ...emptyQuery,
    filters: { startTime: thirtyDaysAgo.toISOString(), endTime: now.toISOString() },
  }

  // Each KPI declares the permission its endpoint enforces. A business admin who
  // lacks it (e.g. access-logs → system.accessLog.view is Technical) DROPS the
  // tile entirely instead of rendering a misleading 0 from a swallowed 403.
  const specs: KpiSpec[] = [
    {
      key: 'users', permission: 'user.view', module: 'identity',
      title: 'admin.modules.dashboard.kpi.users', icon: 'mdi:account-group',
      gradient: { start: '#ec4786', end: '#b955a4' },
      load: async () => (await identityBridge.users.fetch(emptyQuery)).totalCount ?? 0,
    },
    {
      key: 'access-logs', permission: 'system.accessLog.view', module: 'system',
      title: 'admin.modules.dashboard.kpi.accessLogs', icon: 'mdi:chart-areaspline',
      gradient: { start: '#865ec0', end: '#5144b4' },
      load: async () => (await systemBridge.accessLogs.fetch(emptyQuery)).totalCount ?? 0,
    },
    {
      key: 'ai-requests', permission: 'ai.usage.view', module: 'ai',
      title: 'admin.modules.dashboard.kpi.aiRequests', icon: 'mdi:robot-outline',
      gradient: { start: '#56cdf3', end: '#719de3' },
      load: async () => (await aiBridge.usage.summary(aiUsageQuery)).requestCount ?? 0,
    },
    {
      key: 'orders', permission: 'payment.order.view', module: 'payment',
      title: 'admin.modules.dashboard.kpi.orders', icon: 'mdi:cart-arrow-down',
      gradient: { start: '#fcbc25', end: '#f68057' },
      load: async () => (await paymentBridge.orders.fetch(emptyQuery)).totalCount ?? 0,
    },
  ]
  const visible = specs.filter(
    (s) => canSee(s.permission) && moduleAvailability.canActivate(s.module),
  )
  const results = await Promise.allSettled(visible.map((s) => s.load()))
  dynamicKpis.value = visible.map((s, i) => {
    const r = results[i]
    return {
      key: s.key,
      title: s.title,
      value: r && r.status === 'fulfilled' ? r.value : 0,
      icon: s.icon,
      gradient: s.gradient,
    }
  })
})

// Re-run the auto-fetch when the module-availability signal settles or
// refreshes: the mount-time run may have executed while the probe was still
// in flight (module-tagged KPIs deferred, zero requests fired), and a
// post-login refresh can change the loaded set.
watch(
  () => [moduleAvailability.pending.value, moduleAvailability.modules.value] as const,
  () => {
    if (isAuto.value) void reload()
  },
)
</script>

<template>
  <NGrid
    v-if="displayKpis.length"
    :x-gap="12"
    :y-gap="12"
    responsive="screen"
    item-responsive
    cols="24"
  >
    <NGi v-for="kpi in displayKpis" :key="kpi.key" :span="kpiSpan">
      <NCard
        size="small"
        :class="[
          't-widget-kpi__card',
          { 't-widget-kpi__card--gradient': !!kpi.gradient },
        ]"
        :content-style="
          kpi.gradient
            ? {
                backgroundImage: `linear-gradient(135deg, ${kpi.gradient.start} 0%, ${kpi.gradient.end} 100%)`,
              }
            : undefined
        "
      >
        <div class="t-widget-kpi__row">
          <div class="t-widget-kpi__text">
            <div class="t-widget-kpi__title">{{ maybeTranslate(kpi.title) }}</div>
            <div class="t-widget-kpi__value">
              <span v-if="kpi.unit" class="t-widget-kpi__unit">{{ kpi.unit }}</span>
              <TCountTo
                :end-value="kpi.value"
                :decimals="kpi.decimals ?? 0"
                :duration="1200"
              />
            </div>
            <div
              v-if="kpi.delta"
              class="t-widget-kpi__delta"
              :data-trend="kpi.deltaTrend ?? 'flat'"
            >
              {{ kpi.delta }}
            </div>
          </div>
          <div
            class="t-widget-kpi__icon"
            :data-tone="kpi.tone ?? 'primary'"
          >
            <TSvgIcon :icon="kpi.icon" :size="kpi.gradient ? 28 : 22" />
          </div>
        </div>
      </NCard>
    </NGi>
  </NGrid>
</template>

<style scoped>
.t-widget-kpi__card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  /* Clip the inner gradient/content so the parent border-radius actually
     rounds the visible corners. Without this, NCard's `.n-card__content`
     paints its background-image right up to the rectangular bounding box
     and the 8px radius is invisible on gradient tiles. */
  overflow: hidden;
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  transition:
    transform var(--tnzi-admin-motion-duration-fast, 0.15s) ease,
    box-shadow var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}
.t-widget-kpi__card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-widget-kpi__card--gradient :deep(.n-card-header) {
  display: none;
}
.t-widget-kpi__card--gradient :deep(.n-card__content) {
  padding: 16px 18px;
  color: #fff;
  /* Match the outer card's border radius so the gradient hugs the
     rounded corners instead of bleeding past them. `overflow: hidden`
     on the parent handles the visual clip, but inheriting the radius
     here also prevents subpixel seams on high-DPI displays. */
  border-radius: inherit;
}
.t-widget-kpi__card--gradient .t-widget-kpi__title,
.t-widget-kpi__card--gradient .t-widget-kpi__value,
.t-widget-kpi__card--gradient .t-widget-kpi__delta {
  color: rgba(255, 255, 255, 0.96);
}
.t-widget-kpi__card--gradient .t-widget-kpi__title {
  color: rgba(255, 255, 255, 0.86);
}
.t-widget-kpi__card--gradient .t-widget-kpi__icon {
  background-color: rgba(255, 255, 255, 0.16);
  color: rgba(255, 255, 255, 0.96);
}
.t-widget-kpi__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.t-widget-kpi__text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.t-widget-kpi__title {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-widget-kpi__value {
  font-size: 26px;
  font-weight: 600;
  line-height: 1.2;
  color: var(--tnzi-base-text);
}
.t-widget-kpi__unit {
  font-size: 18px;
  font-weight: 500;
  margin-right: 2px;
}
.t-widget-kpi__delta {
  font-size: 12px;
  font-weight: 500;
}
.t-widget-kpi__delta[data-trend='up'] {
  color: var(--tnzi-success);
}
.t-widget-kpi__delta[data-trend='down'] {
  color: var(--tnzi-error);
}
.t-widget-kpi__delta[data-trend='flat'] {
  color: var(--tnzi-base-text-muted);
}
.t-widget-kpi__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  flex-shrink: 0;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
}
.t-widget-kpi__icon[data-tone='info'] {
  background-color: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-widget-kpi__icon[data-tone='success'] {
  background-color: rgb(var(--tnzi-success-rgb, 24 160 88) / 0.12);
  color: var(--tnzi-success);
}
.t-widget-kpi__icon[data-tone='warning'] {
  background-color: rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.12);
  color: var(--tnzi-warning);
}
.t-widget-kpi__icon[data-tone='error'] {
  background-color: rgb(var(--tnzi-error-rgb, 208 48 80) / 0.12);
  color: var(--tnzi-error);
}
</style>
