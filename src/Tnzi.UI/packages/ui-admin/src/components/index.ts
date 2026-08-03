// Public component surface for `@tnzi/ui-admin/components`.
//
// Convention: every folder that owns a public surface has its own `index.ts`,
// and this file re-exports it wholesale. Do NOT hand-pick individual
// components out of a folder that has a barrel - that is exactly how
// `TItemCard` / `TEntityCard` / the whole `forms/` folder ended up documented
// as importable from the package root while being unreachable in `dist`.
// A component is public iff it is listed in its folder's barrel.
//
// Folders without a barrel below (chat, settings internals, utility, ...) are
// shell internals assembled by `defineAdminApp`; the handful of them that are
// public are listed explicitly at the bottom of this file.
export * from './crud'
export * from './data'
export * from './detail'
export * from './display'
export * from './forms'
export * from './layout'
export * from './overlay'
// Finance presentation primitives - accounting-convention money/date display,
// the shared reporting-period control, document-status vocabulary, the
// chart-of-accounts / party pickers and the Xero-style reconcile workspace.
// Public so consumer finance screens inherit the conventions instead of
// re-deriving them (see the finance UX plan under docs/superpowers/specs/).
export * from './finance'

// --- Folders without a barrel: individually-public components ---------------

export { default as TAdminLoginCard } from './auth/TAdminLoginCard.vue'
export type { DemoAccount, LoginPayload } from './auth/TAdminLoginCard.vue'

export { default as TIconPicker } from './inputs/TIconPicker.vue'
export { default as TJsonEditor } from './inputs/TJsonEditor.vue'

// Settings center - schema-driven module settings page (side-nav shell) +
// the per-group auto-rendered form panel it composes. (TSettingsField is the
// panel's internal field renderer and stays private.)
export { default as TSettingsPage } from './settings/TSettingsPage.vue'
export { default as TSettingsGroupPanel } from './settings/TSettingsGroupPanel.vue'

// Utility components - 6 sunk to @tnzi/ui in 0.2.71+ (generic UI patterns
// reusable beyond admin shells), re-exported here for backward compat.
// TSystemLogo stays in ui-admin (admin-specific brand surface).
export { default as TSystemLogo } from './utility/TSystemLogo.vue'
export type { TSystemLogoLayout } from './utility/TSystemLogo.vue'

// Built-in pages (drop-in route components)
export { default as TLoginPage } from './pages/TLoginPage.vue'
// Phase I.7.1: `TLoginPageVariant` (centered/split) was removed - TLoginPage
// is now a router-param driven shell with the single soybean layout.
// Login context types live in `../headless/useLoginContext` (root barrel).
export { default as TExceptionPage } from './pages/TExceptionPage.vue'
export { default as TDashboardPage } from './pages/TDashboardPage.vue'
export type { KpiCard, KpiCardGradient, ChartSeriesPoint } from './pages/TDashboardPage.vue'
// Phase J - declarative widget grid (Workbench scaffold).
export { default as TWorkbenchLayout } from './pages/TWorkbenchLayout.vue'

// Dashboard composition components (Phase I.6.4)
export { default as THeaderBanner } from './dashboard/THeaderBanner.vue'
export { default as TProjectTimeline } from './dashboard/TProjectTimeline.vue'
export type { TimelineItem, TimelineTone } from './dashboard/TProjectTimeline.vue'

// Chat display atom - presence status dot (color-coded by UserPresenceStatus).
// Exported so consumer apps that surface presence (e.g. on TAvatar's `#badge`
// slot) reuse it instead of copying it. The richer chat widgets stay internal
// to the shell (mounted by `defineAdminApp({ chat })`).
export { default as TPresenceDot } from './chat/TPresenceDot.vue'

// --- Re-exports from @tnzi/ui ----------------------------------------------
// Generic primitives that used to live here and were sunk into @tnzi/ui;
// re-exported so existing `@tnzi/ui-admin` imports keep resolving.
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
