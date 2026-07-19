// Public component surface for `@tnzi/ui-admin/components`.
export * from './crud'
// Overlay chrome primitives (shared NModal / NDrawer shells).
export * from './overlay'
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
export type { BackTarget } from './layout/backTarget'
export { default as TContentPage } from './layout/TContentPage.vue'
// Batteries-included container for tabbed content pages — declare `:sections`,
// drop each tab's content in a same-named slot; NTabs chrome, `t-table-tabs`
// surface, per-pane wrapper, `?section=` deep-linking and card-in-a-card
// flattening are all owned internally (no boilerplate at the call site).
export { default as TTabsPage } from './layout/TTabsPage.vue'
export type { TabSection } from './layout/TTabsPage.vue'
// Master-detail layout primitive — list/tree pane + detail pane with a
// built-in responsive grid + fill-height chain + mobile stacking. Replaces the
// hand-rolled `grid Npx 1fr` + @media 767 master-detail CSS in the built-in
// Organizations / Permissions / RoleFunctions pages.
export { default as TMasterDetailLayout } from './layout/TMasterDetailLayout.vue'
export { default as TDarkModeContainer } from './layout/TDarkModeContainer.vue'
// Header notification bell — unread badge + popover list + load-more + empty.
// Mount via `defineAdminApp({ login: { headerNotification } })`.
export { default as THeaderBell } from './layout/THeaderBell.vue'

// Data primitives — responsive table (auto card-stacking on phones) and the
// card-list primitive underneath it. Public per the content-page standard
// (docs/coding-standards/ui-content-page.md §5.5): consumers replacing a raw
// NDataTable are told to import TResponsiveTable from the package root.
export { default as TResponsiveTable } from './data/TResponsiveTable.vue'
export type { TResponsiveTableProps, TResponsivePagination, TResponsiveSummaryRow } from './data/TResponsiveTable.vue'
// Financial-report table — money/total columns + auto totals row + drill-down.
export { default as TReportTable } from './data/TReportTable.vue'
export type { ReportColumn } from './data/TReportTable.vue'
export { default as TDataCardList } from './data/TDataCardList.vue'
export type { CardColumn } from './data/TDataCardList.vue'
// KPI primitives — unified KPI card + responsive KPI strip (one per page,
// rendered between the page header and the list/content per the content-page
// standard). TEmpty is the unified empty-state visual used by the card
// renderers and available to bespoke pages.
// TKpiCard was renamed from TStatCard in the 2026-06 audit to avoid colliding
// with @tnzi/ui's globally-registered <TStatCard>; a deprecated TStatCard
// alias is kept for back-compat.
export { default as TKpiCard } from './data/TKpiCard.vue'
export type { TKpiCardProps, TKpiCardTone } from './data/TKpiCard.vue'
/** @deprecated use TKpiCard. */
export { default as TStatCard } from './data/TKpiCard.vue'
/** @deprecated use TKpiCardProps / TKpiCardTone. */
export type { TKpiCardProps as TStatCardProps, TKpiCardTone as TStatCardTone } from './data/TKpiCard.vue'
export { default as TKpiRow } from './data/TKpiRow.vue'
export { default as TEmpty } from './data/TEmpty.vue'
export type { TEmptyProps, TEmptySize } from './data/TEmpty.vue'

// Detail primitives — 3-mode (modal/drawer/page) detail host + skeleton +
// per-section container. TDetailSection is the standard chrome for ONE section
// inside a TDetailLayout side/tabs panel (fixed header bar + scrolling body +
// optional savebar); exported so consumer detail pages compose it instead of
// copying it (the built-in module detail pages use it internally).
export { default as TDetailLayout } from './detail/TDetailLayout.vue'
export { default as TDetailHost } from './detail/TDetailHost.vue'
export type { TDetailHostProps } from './detail/TDetailHost.vue'
export { default as TDetailSection } from './detail/TDetailSection.vue'

// Settings center — schema-driven module settings page (side-nav shell) +
// the per-group auto-rendered form panel it composes.
export { default as TSettingsPage } from './settings/TSettingsPage.vue'
export { default as TSettingsGroupPanel } from './settings/TSettingsGroupPanel.vue'
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

// Chat display atom — presence status dot (color-coded by UserPresenceStatus).
// Exported so consumer apps that surface presence (e.g. on TAvatar's `#badge`
// slot) reuse it instead of copying it. The richer chat widgets stay internal
// to the shell (mounted by `defineAdminApp({ chat })`).
export { default as TPresenceDot } from './chat/TPresenceDot.vue'
