/**
 * Public surface for the Workbench widget system.
 *
 * Consumers import the descriptor types, the shell helpers (`useWidget` /
 * `useWidgetData`), and the bundled built-in widgets. Build their own
 * widgets by following the same pattern - any Vue component referenced
 * from a `WidgetDef.component` plugs in.
 */

// --- Types -----------------------------------------------------------------
export type {
  SpanValue,
  WidgetContext,
  WidgetDef,
  WorkbenchConfig,
} from '@tnzi/ui'

// --- Shell helpers ---------------------------------------------------------
export { useWidget, WIDGET_CONTEXT_KEY } from '@tnzi/ui'
export { useWidgetData } from '../../headless/useWidgetData'
export { default as TWidgetCard } from './TWidgetCard.vue'

// --- Built-in generic widgets (props-driven, no business deps) -------------
export { default as TWidgetHeaderBanner } from './TWidgetHeaderBanner.vue'
export { default as TWidgetKpiStrip } from './TWidgetKpiStrip.vue'
export { default as TWidgetActivityTimeline } from './TWidgetActivityTimeline.vue'
export { default as TWidgetLineChart } from './TWidgetLineChart.vue'
export { default as TWidgetPieChart } from './TWidgetPieChart.vue'
export type {
  PieLegendPosition,
  PieSliceClickEvent,
  PieLegendClickEvent,
} from './TWidgetPieChart.vue'
export { default as TWidgetList } from './TWidgetList.vue'
export type { WidgetListTone } from './TWidgetList.vue'
export { default as TWidgetQuickActions } from './TWidgetQuickActions.vue'
export type { QuickAction } from './TWidgetQuickActions.vue'

// --- Built-in business widgets (call admin bridges internally) -------------
export { default as TWidgetSystemHealth } from './TWidgetSystemHealth.vue'
export { default as TWidgetIdentityStats } from './TWidgetIdentityStats.vue'
export { default as TWidgetAuditRecent } from './TWidgetAuditRecent.vue'
export { default as TWidgetAiUsage } from './TWidgetAiUsage.vue'
export { default as TWidgetStorageUsage } from './TWidgetStorageUsage.vue'
export { default as TWidgetNotificationStats } from './TWidgetNotificationStats.vue'
export { default as TWidgetChatStats } from './TWidgetChatStats.vue'
export { default as TWidgetOpsSnapshot } from './TWidgetOpsSnapshot.vue'

// --- Presets ---------------------------------------------------------------
export {
  defaultWorkbenchWidgets,
  defaultQuickActions,
  defaultTimelineItems,
  defaultKpiCards,
} from './presets'
