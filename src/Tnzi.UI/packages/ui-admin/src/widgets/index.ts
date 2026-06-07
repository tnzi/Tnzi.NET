/**
 * Public surface for the Workbench widget system.
 *
 * Consumers import the descriptor types, the shell helpers (`useWidget` /
 * `useWidgetData`), and the bundled built-in widgets. Build their own
 * widgets by following the same pattern — any Vue component referenced
 * from a `WidgetDef.component` plugs in.
 */

// --- Types -----------------------------------------------------------------
export type {
  SpanValue,
  WidgetContext,
  WidgetDef,
  WorkbenchConfig,
} from './types'

// --- Shell helpers ---------------------------------------------------------
export { useWidget, WIDGET_CONTEXT_KEY } from './shell/useWidget'
export { useWidgetData } from './shell/useWidgetData'
export { default as TWidgetCard } from './shell/TWidgetCard.vue'

// --- Built-in generic widgets (props-driven, no business deps) -------------
export { default as TWidgetHeaderBanner } from './builtin/TWidgetHeaderBanner.vue'
export { default as TWidgetKpiStrip } from './builtin/TWidgetKpiStrip.vue'
export { default as TWidgetActivityTimeline } from './builtin/TWidgetActivityTimeline.vue'
export { default as TWidgetLineChart } from './builtin/TWidgetLineChart.vue'
export { default as TWidgetPieChart } from './builtin/TWidgetPieChart.vue'
export { default as TWidgetTips } from './builtin/TWidgetTips.vue'
export { default as TWidgetQuickActions } from './builtin/TWidgetQuickActions.vue'
export type { QuickAction } from './builtin/TWidgetQuickActions.vue'

// --- Built-in business widgets (call admin bridges internally) -------------
export { default as TWidgetSystemHealth } from './builtin/TWidgetSystemHealth.vue'
export { default as TWidgetIdentityStats } from './builtin/TWidgetIdentityStats.vue'
export { default as TWidgetAuditRecent } from './builtin/TWidgetAuditRecent.vue'
export { default as TWidgetAiUsage } from './builtin/TWidgetAiUsage.vue'
export { default as TWidgetStorageUsage } from './builtin/TWidgetStorageUsage.vue'
export { default as TWidgetNotificationStats } from './builtin/TWidgetNotificationStats.vue'
export { default as TWidgetChatStats } from './builtin/TWidgetChatStats.vue'
export { default as TWidgetOpsSnapshot } from './builtin/TWidgetOpsSnapshot.vue'

// --- Presets ---------------------------------------------------------------
export {
  defaultWorkbenchWidgets,
  defaultQuickActions,
  defaultTimelineItems,
  defaultKpiCards,
} from './presets'
