// Public component surface for `@tnzi/ui-admin/components`.
export * from './crud'
export { default as TAdminLoginCard } from './auth/TAdminLoginCard.vue'
export type { DemoAccount, LoginPayload } from './auth/TAdminLoginCard.vue'
export { default as TIconPicker } from './inputs/TIconPicker.vue'
export { default as TJsonEditor } from './inputs/TJsonEditor.vue'

// Display primitives — TStatusBadge implementation was sunk to @tnzi/ui in
// 0.2.x; the local SFC is now a thin wrapper that injects admin i18n via
// translatePageKey. The other 8 are re-exported directly from @tnzi/ui.
export { default as TStatusBadge } from './display/TStatusBadge.vue'
export {
  TRelativeTime,
  TCountTo,
  TSvgIcon,
  TButtonIcon,
  TSourceBadge,
  TStatToCards,
  TWaveBg,
  TSkeleton,
} from '@tnzi/ui'
export type { SourceKind, StatCard, StatusType } from '@tnzi/ui'

// Layout primitives
export { default as TPageHeader } from './layout/TPageHeader.vue'
export { default as TContentPage } from './layout/TContentPage.vue'
export { default as TDarkModeContainer } from './layout/TDarkModeContainer.vue'

// Detail primitives — 3-mode (modal/drawer/page) detail host + skeleton.
export { default as TDetailLayout } from './detail/TDetailLayout.vue'
export { default as TDetailHost } from './detail/TDetailHost.vue'
export type { TDetailHostProps } from './detail/TDetailHost.vue'
export { default as TAdminRouterView } from './layout/TAdminRouterView.vue'
// 0.2.72+ (A1): one-shot wrapper that mounts the 5-provider naive-ui stack
// (Config / LoadingBar / Message / Notification / Dialog) and pipes the
// admin theme context through. Consumer App.vue becomes 3 lines.
export { default as TAdminAppRoot } from './layout/TAdminAppRoot.vue'
// Phase I.7.7: header companion components — auto-derived breadcrumb +
// user-avatar dropdown.
export { default as TAdminAutoBreadcrumb } from './layout/TAdminAutoBreadcrumb.vue'
export { default as TAdminUserAvatar } from './layout/TAdminUserAvatar.vue'

// Utility components — 6 sunk to @tnzi/ui in 0.2.71+ (generic UI patterns
// reusable beyond admin shells), re-exported here for backward compat.
// TSystemLogo stays in ui-admin (admin-specific brand surface).
export {
  TThemeSchemaSwitch,
  TLangSwitch,
  TFullScreen,
  TReloadButton,
  TPinToggler,
  TMenuToggler,
  type ThemeSchema,
  type LangOption,
} from '@tnzi/ui'
export { default as TSystemLogo } from './utility/TSystemLogo.vue'
export type { TSystemLogoLayout } from './utility/TSystemLogo.vue'

// Built-in pages (drop-in route components)
export { default as TLoginPage } from './pages/TLoginPage.vue'
// Phase I.7.1: `TLoginPageVariant` (centered/split) was removed — TLoginPage
// is now a router-param driven shell with the single soybean layout.
// Login context types live in `../pages/login/useLoginContext`.
export { default as TExceptionPage } from './pages/TExceptionPage.vue'
export { default as TDashboardPage } from './pages/TDashboardPage.vue'
export type { KpiCard, KpiCardGradient, ChartSeriesPoint } from './pages/TDashboardPage.vue'
// Phase J — declarative widget grid (Workbench scaffold).
export { default as TWorkbenchLayout } from './pages/TWorkbenchLayout.vue'

// Dashboard composition components (Phase I.6.4)
export { default as THeaderBanner } from './dashboard/THeaderBanner.vue'
export { default as TProjectTimeline } from './dashboard/TProjectTimeline.vue'
export type { TimelineItem, TimelineTone } from './dashboard/TProjectTimeline.vue'
