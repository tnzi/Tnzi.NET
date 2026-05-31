// New C-end layout primitives (Phase 1)
export { default as TAppShell } from './TAppShell.vue'
export { default as TSiteHeader } from './TSiteHeader.vue'
export { default as TSiteFooter } from './TSiteFooter.vue'
export { default as TContainer } from './TContainer.vue'
export { default as TSection } from './TSection.vue'
export { default as TGrid } from './TGrid.vue'
export { default as TAuthLayout } from './TAuthLayout.vue'
export { default as TBlankLayout } from './TBlankLayout.vue'
export { default as TCenteredLayout } from './TCenteredLayout.vue'

// Existing primitives retained
export { default as TAppHeader } from './TAppHeader.vue'
export { default as TBreadcrumb } from './TBreadcrumb.vue'

// Widget system (sunk from @tnzi/ui-admin in 0.2.x)
export { default as TWidgetCard } from './TWidgetCard.vue'
export { default as TWorkbenchLayout } from './TWorkbenchLayout.vue'
export {
  WIDGET_CONTEXT_KEY,
  type WidgetContext,
  type WidgetDef,
  type WorkbenchConfig,
  type SpanValue,
} from './widget-types'
