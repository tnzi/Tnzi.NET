/**
 * Default widget presets for the bundled Workbench page.
 *
 * The default `Workbench.vue` calls `defaultWorkbenchWidgets()` when no
 * consumer config is provided. Consumers can also import these helpers
 * to build on top of the defaults:
 *
 * ```ts
 * defineAdminApp({
 *   workbench: {
 *     widgets: [
 *       ...defaultWorkbenchWidgets(),
 *       { id: 'my-app', component: MyAppWidget, span: 24 },
 *     ],
 *     layout: 'draggable',
 *   },
 * })
 * ```
 */
import type { WidgetDef } from './types'
import type { KpiCard } from '../components/pages/TDashboardPage.vue'
import type { TimelineItem } from '../components/dashboard/TProjectTimeline.vue'
import type { QuickAction } from './builtin/TWidgetQuickActions.vue'

import TWidgetKpiStrip from './builtin/TWidgetKpiStrip.vue'
import TWidgetQuickActions from './builtin/TWidgetQuickActions.vue'
import TWidgetSystemHealth from './builtin/TWidgetSystemHealth.vue'
import TWidgetIdentityStats from './builtin/TWidgetIdentityStats.vue'
import TWidgetAiUsage from './builtin/TWidgetAiUsage.vue'
import TWidgetStorageUsage from './builtin/TWidgetStorageUsage.vue'
import TWidgetNotificationStats from './builtin/TWidgetNotificationStats.vue'
import TWidgetChatStats from './builtin/TWidgetChatStats.vue'
import TWidgetAuditRecent from './builtin/TWidgetAuditRecent.vue'
import TWidgetActivityTimeline from './builtin/TWidgetActivityTimeline.vue'
import TWidgetTips from './builtin/TWidgetTips.vue'
import TWidgetOpsSnapshot from './builtin/TWidgetOpsSnapshot.vue'

/**
 * The canned 4-tile KPI strip the Workbench shipped with before the
 * widget refactor. **Mock placeholder data** — exposed only for consumers
 * who want to render a static demo / marketing screenshot without hitting
 * the admin API. The bundled Workbench deck no longer wires these in;
 * `TWidgetKpiStrip` auto-fetches real admin metrics (users, access logs,
 * AI requests, payment orders) when `kpis` is omitted.
 */
export function defaultKpiCards(): KpiCard[] {
  return [
    {
      key: 'visits',
      title: 'admin.modules.workbench.kpi.visits',
      value: 5083,
      icon: 'mdi:chart-areaspline',
      gradient: { start: '#ec4786', end: '#b955a4' },
    },
    {
      key: 'sales',
      title: 'admin.modules.workbench.kpi.sales',
      value: 537,
      unit: '$',
      icon: 'mdi:cash-multiple',
      gradient: { start: '#865ec0', end: '#5144b4' },
    },
    {
      key: 'downloads',
      title: 'admin.modules.workbench.kpi.downloads',
      value: 507471,
      icon: 'mdi:download',
      gradient: { start: '#56cdf3', end: '#719de3' },
    },
    {
      key: 'transactions',
      title: 'admin.modules.workbench.kpi.transactions',
      value: 4980,
      icon: 'mdi:cart-arrow-down',
      gradient: { start: '#fcbc25', end: '#f68057' },
    },
  ]
}

/** Default 4-action grid pointing at the most common admin destinations. */
export function defaultQuickActions(): QuickAction[] {
  return [
    {
      key: 'create-user',
      icon: 'mdi:account-plus-outline',
      label: 'admin.widgets.quickActions.createUser',
      to: '/admin/identity/users',
      tone: 'primary',
    },
    {
      key: 'audit',
      icon: 'mdi:shield-check-outline',
      label: 'admin.widgets.quickActions.audit',
      to: '/admin/audit/logs',
      tone: 'info',
    },
    {
      key: 'agents',
      icon: 'mdi:robot-outline',
      label: 'admin.widgets.quickActions.agents',
      to: '/admin/ai/agents',
      tone: 'success',
    },
    {
      key: 'settings',
      icon: 'mdi:cog-outline',
      label: 'admin.widgets.quickActions.settings',
      to: '/admin/system/parameters',
      tone: 'warning',
    },
  ]
}

/** Default what's-new style timeline. Plain English — apps usually override. */
export function defaultTimelineItems(): TimelineItem[] {
  return [
    {
      key: '1',
      title: 'admin.modules.workbench.activity.item1Title',
      description: 'admin.modules.workbench.activity.item1Desc',
      time: 'admin.modules.workbench.activity.item1Time',
      tone: 'success',
    },
    {
      key: '2',
      title: 'admin.modules.workbench.activity.item2Title',
      description: 'admin.modules.workbench.activity.item2Desc',
      time: 'admin.modules.workbench.activity.item2Time',
      tone: 'info',
    },
    {
      key: '3',
      title: 'admin.modules.workbench.activity.item3Title',
      description: 'admin.modules.workbench.activity.item3Desc',
      time: 'admin.modules.workbench.activity.item3Time',
    },
    {
      key: '4',
      title: 'admin.modules.workbench.activity.item4Title',
      description: 'admin.modules.workbench.activity.item4Desc',
      time: 'admin.modules.workbench.activity.item4Time',
    },
  ]
}

/**
 * The bundled Workbench deck. Renders six rows:
 *   1. KPI strip (24, top hero)
 *   2. Quick actions (24)
 *   3. Four business stat cards (lg: 6 each, md: 12 each, xs: 24)
 *   4. System health + activity timeline (8 / 16 at lg)
 *   5. Recent audit + chat + tips
 */
export function defaultWorkbenchWidgets(): WidgetDef[] {
  return [
    {
      id: 'kpi',
      component: TWidgetKpiStrip,
      span: 24,
      bare: true,
      // KPI strip now auto-fetches 4 real admin metrics — no props needed.
      // Consumers wanting custom KPIs can override this widget entry with
      // `{ ...kpiWidget, props: { kpis: myCustomCards } }`.
      props: {},
    },
    {
      id: 'quick-actions',
      component: TWidgetQuickActions,
      title: 'admin.widgets.quickActions.title',
      icon: 'mdi:lightning-bolt-outline',
      span: 24,
      refreshable: false,
      props: { actions: defaultQuickActions() },
    },
    {
      id: 'identity-stats',
      component: TWidgetIdentityStats,
      title: 'admin.widgets.identityStats.title',
      icon: 'mdi:account-group-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
    },
    {
      id: 'ai-usage',
      component: TWidgetAiUsage,
      title: 'admin.widgets.aiUsage.title',
      icon: 'mdi:robot-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
    },
    {
      id: 'storage-usage',
      component: TWidgetStorageUsage,
      title: 'admin.widgets.storage.title',
      icon: 'mdi:harddisk',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
    },
    {
      id: 'notification-stats',
      component: TWidgetNotificationStats,
      title: 'admin.widgets.notifications.title',
      icon: 'mdi:email-multiple-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
    },
    {
      id: 'system-health',
      component: TWidgetSystemHealth,
      title: 'admin.widgets.systemHealth.title',
      icon: 'mdi:heart-pulse',
      span: { xs: 24, sm: 24, md: 24, lg: 8 },
    },
    {
      id: 'activity',
      component: TWidgetActivityTimeline,
      title: 'admin.modules.workbench.activity.title',
      icon: 'mdi:timeline-text-outline',
      span: { xs: 24, sm: 24, md: 24, lg: 16 },
      // Auto-fetches recent audit log entries — `refreshable: true` so
      // the timeline can be re-polled from the widget toolbar (the
      // hardcoded i18n placeholder list never benefited from refresh).
      props: { limit: 6 },
    },
    {
      id: 'audit-recent',
      component: TWidgetAuditRecent,
      title: 'admin.widgets.auditRecent.title',
      icon: 'mdi:shield-check-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 12 },
    },
    {
      id: 'ops-health',
      component: TWidgetOpsSnapshot,
      title: 'admin.widgets.opsHealth.title',
      icon: 'mdi:heart-pulse',
      span: { xs: 24, sm: 24, md: 12, lg: 12 },
    },
    {
      id: 'chat-stats',
      component: TWidgetChatStats,
      title: 'admin.widgets.chat.title',
      icon: 'mdi:forum-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
    },
    {
      id: 'tips',
      component: TWidgetTips,
      title: 'admin.modules.workbench.tips.title',
      icon: 'mdi:lightbulb-on-outline',
      span: { xs: 24, sm: 24, md: 24, lg: 6 },
      refreshable: false,
      props: {
        body: 'admin.modules.workbench.tips.body',
      },
    },
  ]
}
