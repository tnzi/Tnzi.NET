// Public component surface for `@tnzi/ui-admin/components`.
export * from './crud'
export { default as TAdminLoginCard } from './auth/TAdminLoginCard.vue'
export type { DemoAccount, LoginPayload } from './auth/TAdminLoginCard.vue'
export { default as TIconPicker } from './inputs/TIconPicker.vue'
export { default as TJsonEditor } from './inputs/TJsonEditor.vue'

// Display primitives
export { default as TRelativeTime } from './display/TRelativeTime.vue'
export { default as TStatusBadge } from './display/TStatusBadge.vue'
export type { StatusType } from './display/TStatusBadge.vue'
export { default as TStatToCards } from './display/TStatToCards.vue'
export type { StatCard } from './display/TStatToCards.vue'
export { default as TSvgIcon } from './display/TSvgIcon.vue'
export { default as TButtonIcon } from './display/TButtonIcon.vue'
export { default as TCountTo } from './display/TCountTo.vue'
export { default as TWaveBg } from './display/TWaveBg.vue'
export { default as TSkeleton } from './display/TSkeleton.vue'

// Layout primitives
export { default as TDarkModeContainer } from './layout/TDarkModeContainer.vue'
export { default as TAdminRouterView } from './layout/TAdminRouterView.vue'
// Phase I.7.7: header companion components — auto-derived breadcrumb +
// user-avatar dropdown.
export { default as TAdminAutoBreadcrumb } from './layout/TAdminAutoBreadcrumb.vue'
export { default as TAdminUserAvatar } from './layout/TAdminUserAvatar.vue'

// Utility components (Phase I.6.3) — header / toolbar / brand-area widgets
export { default as TThemeSchemaSwitch } from './utility/TThemeSchemaSwitch.vue'
export type { ThemeSchema } from './utility/TThemeSchemaSwitch.vue'
export { default as TLangSwitch } from './utility/TLangSwitch.vue'
export type { LangOption } from './utility/TLangSwitch.vue'
export { default as TFullScreen } from './utility/TFullScreen.vue'
export { default as TReloadButton } from './utility/TReloadButton.vue'
export { default as TPinToggler } from './utility/TPinToggler.vue'
export { default as TMenuToggler } from './utility/TMenuToggler.vue'
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

// Dashboard composition components (Phase I.6.4)
export { default as THeaderBanner } from './dashboard/THeaderBanner.vue'
export { default as TProjectTimeline } from './dashboard/TProjectTimeline.vue'
export type { TimelineItem, TimelineTone } from './dashboard/TProjectTimeline.vue'
