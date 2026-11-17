// Public component surface for `@tnzi/ui-admin/components`.
//
// Convention: every folder that owns a public surface has its own `index.ts`,
// and this file re-exports it wholesale. Do NOT hand-pick individual
// components out of a folder that has a barrel - that is exactly how
// `TItemCard` / `TEntityCard` / the whole `forms/` folder ended up documented
// as importable from the package root while being unreachable in `dist`.
// A component is public iff it is listed in its folder's barrel.
//
// Folders without a barrel below (chat, desktop, settings internals, utility,
// ...) are shell internals assembled by `defineAdminApp`; the handful of them
// that are public are listed explicitly at the bottom of this file.
//
// `desktop/` deliberately has no barrel and appears nowhere in this file. Its
// components are the `layoutMode: 'desktop'` shell, mounted lazily by
// `AdminShellRoot` - a consumer turns the mode on, it does not assemble the
// window manager by hand. Exporting them would also undo that laziness: a
// barrel entry makes the whole desktop statically reachable from this file and
// from the package root, which measured at +7.1 kB gzip on the `components`
// budget for a surface nobody imports. (The reusable half - the per-window
// route context and the move/resize engine - IS public, from `headless/`.)
export * from './crud'
export * from './data'
export * from './detail'
export * from './display'
export * from './forms'
export * from './layout'
export * from './overlay'
export * from './card'
export * from './navigation'
// Finance presentation primitives - accounting-convention money/date display,
// the shared reporting-period control, document-status vocabulary, the
// chart-of-accounts / party pickers and the Xero-style reconcile workspace.
// Public so consumer finance screens inherit the conventions instead of
// re-deriving them (see the finance UX plan under docs/superpowers/specs/).
export * from './finance'

// --- Folders without a barrel: individually-public components ---------------

export { default as TAdminLoginCard } from './auth/TAdminLoginCard.vue'
export type { DemoAccount, LoginPayload } from './auth/TAdminLoginCard.vue'
// Re-authentication prompt for `[RequireStepUp]` actions. Renders a core
// `StepUpPromptController`; the User Center mounts one and feeds
// `prompt.verify` to the identity bridge, and a consumer page that calls its
// own step-up-gated endpoint can do the same.
export { default as TStepUpModal } from './auth/TStepUpModal.vue'

export { default as TIconPicker } from './inputs/TIconPicker.vue'
export { default as TJsonEditor } from './inputs/TJsonEditor.vue'

// Settings center - schema-driven module settings page (side-nav shell) +
// the per-group auto-rendered form panel it composes. (TSettingsField is the
// panel's internal field renderer and stays private.)
export { default as TSettingsPage } from './settings/TSettingsPage.vue'
export { default as TSettingsGroupPanel } from './settings/TSettingsGroupPanel.vue'

// Utility components: the shell's small toolbar buttons and switches, and the
// brand logo.
export { default as TSystemLogo } from './utility/TSystemLogo.vue'
export type { TSystemLogoLayout } from './utility/TSystemLogo.vue'
export { default as TThemeSchemaSwitch } from './utility/TThemeSchemaSwitch.vue'
export type { ThemeSchema } from './utility/TThemeSchemaSwitch.vue'
export { default as TLangSwitch } from './utility/TLangSwitch.vue'
export type { LangOption } from './utility/TLangSwitch.vue'
export { default as TFullScreen } from './utility/TFullScreen.vue'
export { default as TReloadButton } from './utility/TReloadButton.vue'
export { default as TPinToggler } from './utility/TPinToggler.vue'
export { default as TMenuToggler } from './utility/TMenuToggler.vue'

// Built-in pages (drop-in route components)
export { default as TLoginPage } from './pages/TLoginPage.vue'
// Phase I.7.1: `TLoginPageVariant` (centered/split) was removed - TLoginPage
// is now a router-param driven shell with the single soybean layout.
// Login context types live in `../headless/useLoginContext` (root barrel).
export { default as TExceptionPage } from './pages/TExceptionPage.vue'
export { default as TDashboardPage } from './pages/TDashboardPage.vue'
export type { KpiCard, KpiCardGradient, ChartSeriesPoint } from './pages/TDashboardPage.vue'

// Dashboard composition components (Phase I.6.4)
export { default as THeaderBanner } from './dashboard/THeaderBanner.vue'
export { default as TProjectTimeline } from './dashboard/TProjectTimeline.vue'
export type { TimelineItem, TimelineTone } from './dashboard/TProjectTimeline.vue'

// Chat display atom - presence status dot (color-coded by UserPresenceStatus).
// Exported so consumer apps that surface presence (e.g. on TAvatar's `#badge`
// slot) reuse it instead of copying it. The richer chat widgets stay internal
// to the shell (mounted by `defineAdminApp({ chat })`).
export { default as TPresenceDot } from './chat/TPresenceDot.vue'
