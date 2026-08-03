/**
 * Default widget presets for the bundled Workbench page.
 *
 * The default `Dashboard.vue` calls `defaultWorkbenchWidgets()` when no
 * consumer config is provided. Consumers can also import these helpers
 * to build on top of the defaults:
 *
 * ```ts
 * defineAdminApp({
 *   dashboard: {
 *     widgets: [
 *       ...defaultWorkbenchWidgets(),
 *       { id: 'my-app', component: MyAppWidget, span: 24 },
 *     ],
 *     layout: 'draggable',
 *   },
 * })
 * ```
 */
import type { WidgetDef } from '@tnzi/ui'
import type { KpiCard } from '../pages/TDashboardPage.vue'
import type { TimelineItem } from '../dashboard/TProjectTimeline.vue'
import type { QuickAction } from './TWidgetQuickActions.vue'

import TWidgetKpiStrip from './TWidgetKpiStrip.vue'
import TWidgetQuickActions from './TWidgetQuickActions.vue'
import TWidgetSystemHealth from './TWidgetSystemHealth.vue'
import TWidgetIdentityStats from './TWidgetIdentityStats.vue'
import TWidgetAiUsage from './TWidgetAiUsage.vue'
import TWidgetStorageUsage from './TWidgetStorageUsage.vue'
import TWidgetNotificationStats from './TWidgetNotificationStats.vue'
import TWidgetChatStats from './TWidgetChatStats.vue'
import TWidgetAuditRecent from './TWidgetAuditRecent.vue'
import TWidgetActivityTimeline from './TWidgetActivityTimeline.vue'
import TWidgetOpsSnapshot from './TWidgetOpsSnapshot.vue'

/**
 * The canned 4-tile KPI strip the Workbench shipped with before the
 * widget refactor. **Mock placeholder data** - exposed only for consumers
 * who want to render a static demo / marketing screenshot without hitting
 * the admin API. The bundled Workbench deck no longer wires these in;
 * `TWidgetKpiStrip` auto-fetches real admin metrics (users, access logs,
 * AI requests, payment orders) when `kpis` is omitted.
 */
export function defaultKpiCards(): KpiCard[] {
  return [
    {
      key: 'visits',
      title: 'admin.modules.dashboard.kpi.visits',
      value: 5083,
      icon: 'mdi:chart-areaspline',
      gradient: { start: '#ec4786', end: '#b955a4' },
    },
    {
      key: 'sales',
      title: 'admin.modules.dashboard.kpi.sales',
      value: 537,
      unit: '$',
      icon: 'mdi:cash-multiple',
      gradient: { start: '#865ec0', end: '#5144b4' },
    },
    {
      key: 'downloads',
      title: 'admin.modules.dashboard.kpi.downloads',
      value: 507471,
      icon: 'mdi:download',
      gradient: { start: '#56cdf3', end: '#719de3' },
    },
    {
      key: 'transactions',
      title: 'admin.modules.dashboard.kpi.transactions',
      value: 4980,
      icon: 'mdi:cart-arrow-down',
      gradient: { start: '#fcbc25', end: '#f68057' },
    },
  ]
}

/**
 * Default 4-action grid pointing at the most common admin destinations.
 *
 * Targets are NAMED routes, never path literals: `defineAdminApp({ basePath })`
 * rewrites the prefix, so a hardcoded `/admin/identity/users` 404s for every
 * consumer that mounts the shell anywhere but the default `/admin` - and this
 * is the deck every consumer gets out of the box.
 */
export function defaultQuickActions(): QuickAction[] {
  return [
    {
      key: 'create-user',
      icon: 'mdi:account-plus-outline',
      label: 'admin.widgets.quickActions.createUser',
      to: { name: 'identity.users' },
      tone: 'primary',
      permission: 'user.view',
      module: 'identity',
    },
    {
      key: 'audit',
      icon: 'mdi:shield-check-outline',
      label: 'admin.widgets.quickActions.audit',
      to: { name: 'audit.logs' },
      tone: 'info',
      permission: 'audit.log.view',
      module: 'audit',
    },
    {
      key: 'agents',
      icon: 'mdi:robot-outline',
      label: 'admin.widgets.quickActions.agents',
      to: { name: 'ai.agents' },
      tone: 'success',
      permission: 'ai.agent.view',
      module: 'ai',
    },
    {
      key: 'settings',
      icon: 'mdi:cog-outline',
      label: 'admin.widgets.quickActions.settings',
      to: { name: 'settings' },
      tone: 'warning',
      // Settings Center is gated by system.parameter.view (Technical) - hidden
      // from business admins so the tile doesn't just bounce to /403.
      permission: 'system.parameter.view',
      module: 'system',
    },
  ]
}

/** Default what's-new style timeline. Plain English - apps usually override. */
export function defaultTimelineItems(): TimelineItem[] {
  return [
    {
      key: '1',
      title: 'admin.modules.dashboard.activity.item1Title',
      description: 'admin.modules.dashboard.activity.item1Desc',
      time: 'admin.modules.dashboard.activity.item1Time',
      tone: 'success',
    },
    {
      key: '2',
      title: 'admin.modules.dashboard.activity.item2Title',
      description: 'admin.modules.dashboard.activity.item2Desc',
      time: 'admin.modules.dashboard.activity.item2Time',
      tone: 'info',
    },
    {
      key: '3',
      title: 'admin.modules.dashboard.activity.item3Title',
      description: 'admin.modules.dashboard.activity.item3Desc',
      time: 'admin.modules.dashboard.activity.item3Time',
    },
    {
      key: '4',
      title: 'admin.modules.dashboard.activity.item4Title',
      description: 'admin.modules.dashboard.activity.item4Desc',
      time: 'admin.modules.dashboard.activity.item4Time',
    },
  ]
}

/**
 * The bundled Workbench deck. Renders six rows:
 *   1. KPI strip (24, top hero)
 *   2. Quick actions (24)
 *   3. Four business stat cards (lg: 6 each, md: 12 each, xs: 24)
 *   4. System health + activity timeline (8 / 16 at lg)
 *   5. Recent audit + ops health + chat
 */
export function defaultWorkbenchWidgets(): WidgetDef[] {
  return [
    {
      id: 'kpi',
      component: TWidgetKpiStrip,
      span: 24,
      bare: true,
      // KPI strip now auto-fetches 4 real admin metrics - no props needed.
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
    // Data widgets carry the permission code of the surface they fetch from,
    // so a user without the grant never mounts the widget (no doomed 403
    // fetches, no dead cards on a zero-permission "empty shell" dashboard).
    // Dashboard.vue fails open for super admins / the pre-permission window.
    // They ALSO carry the backend module their data lives in (`module`), so a
    // host that never loaded that module drops the widget for everyone -
    // including super admins - instead of firing fetches that can only fail.
    {
      id: 'identity-stats',
      component: TWidgetIdentityStats,
      title: 'admin.widgets.identityStats.title',
      icon: 'mdi:account-group-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
      permission: 'user.view',
      module: 'identity',
    },
    {
      id: 'ai-usage',
      component: TWidgetAiUsage,
      title: 'admin.widgets.aiUsage.title',
      icon: 'mdi:robot-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
      permission: 'ai.usage.view',
      module: 'ai',
    },
    {
      id: 'storage-usage',
      component: TWidgetStorageUsage,
      title: 'admin.widgets.storage.title',
      icon: 'mdi:harddisk',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
      permission: 'storage.file.view',
      module: 'storage',
    },
    {
      id: 'notification-stats',
      component: TWidgetNotificationStats,
      title: 'admin.widgets.notifications.title',
      icon: 'mdi:email-multiple-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
      permission: 'notification.message.view',
      module: 'notification',
    },
    {
      id: 'system-health',
      component: TWidgetSystemHealth,
      title: 'admin.widgets.systemHealth.title',
      icon: 'mdi:heart-pulse',
      span: { xs: 24, sm: 24, md: 24, lg: 8 },
      // Technical/ops surface - hidden from business admins (they lack
      // system.health.view). Super admins and the pre-permission window still
      // see it (Dashboard.vue fail-open).
      permission: 'system.health.view',
    },
    {
      id: 'activity',
      component: TWidgetActivityTimeline,
      title: 'admin.modules.dashboard.activity.title',
      icon: 'mdi:timeline-text-outline',
      span: { xs: 24, sm: 24, md: 24, lg: 16 },
      // Auto-fetches recent audit log entries - `refreshable: true` so
      // the timeline can be re-polled from the widget toolbar (the
      // hardcoded i18n placeholder list never benefited from refresh).
      props: { limit: 6 },
      permission: 'audit.log.view',
      module: 'audit',
    },
    {
      id: 'audit-recent',
      component: TWidgetAuditRecent,
      title: 'admin.widgets.auditRecent.title',
      icon: 'mdi:shield-check-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 12 },
      permission: 'audit.log.view',
      module: 'audit',
    },
    {
      id: 'ops-health',
      component: TWidgetOpsSnapshot,
      title: 'admin.widgets.opsHealth.title',
      icon: 'mdi:heart-pulse',
      span: { xs: 24, sm: 24, md: 12, lg: 12 },
      // Technical/ops rollup (exceptions / P95 / SignalR / channels) - its
      // probes are all Technical-gated, so it renders all-"-" for a business
      // admin. Gate it away entirely instead (they lack system.performance.view).
      permission: 'system.performance.view',
    },
    {
      id: 'chat-stats',
      component: TWidgetChatStats,
      title: 'admin.widgets.chat.title',
      icon: 'mdi:forum-outline',
      span: { xs: 24, sm: 24, md: 12, lg: 6 },
      // Static quick-link to chat.conversations - hide when the target route
      // is out of reach so the tile doesn't just bounce to /403.
      permission: 'chat.session.view',
      module: 'chat',
    },
  ]
}
