// Public component surface for `components/layout`.
//
// The package root re-exports this barrel wholesale. Note this is deliberately
// NOT every SFC in the folder: the admin shell internals (TAdminShell,
// TAdminSidebar, TAdminHeader, TAdminTabs, TThemeDrawer, TGlobalSearch, ...)
// are assembled by `defineAdminApp` and are not part of the public surface.
// What ships here is what a consumer page or a custom shell composes directly.

// Page chrome - the white header card every in-frame page gets, plus the two
// page containers (single content surface / tabbed sections).
export { default as TPageHeader } from './TPageHeader.vue'
export type { BackTarget } from './back-target'
export { default as TContentPage } from './TContentPage.vue'
// Batteries-included container for tabbed content pages - declare `:sections`,
// drop each tab's content in a same-named slot; NTabs chrome, `t-table-tabs`
// surface, per-pane wrapper, `?section=` deep-linking and card-in-a-card
// flattening are all owned internally (no boilerplate at the call site).
export { default as TTabsPage } from './TTabsPage.vue'
export type { TabSection } from './TTabsPage.vue'
// Master-detail layout primitive - list/tree pane + detail pane with a
// built-in responsive grid + fill-height chain + mobile stacking. Replaces the
// hand-rolled `grid Npx 1fr` + @media 767 master-detail CSS in the built-in
// Organizations / Permissions / RoleFunctions pages.
export { default as TMasterDetailLayout } from './TMasterDetailLayout.vue'
export { default as TDarkModeContainer } from './TDarkModeContainer.vue'

// Shell companions a consumer mounts explicitly (via defineAdminApp options or
// its own header slots) rather than getting for free from the shell.
// Header notification bell - unread badge + popover list + load-more + empty.
// Mount via `defineAdminApp({ login: { headerNotification } })`.
export { default as THeaderBell } from './THeaderBell.vue'
export { default as TAdminRouterView } from './TAdminRouterView.vue'
// 0.2.72+ (A1): one-shot wrapper that mounts the 5-provider naive-ui stack
// (Config / LoadingBar / Message / Notification / Dialog) and pipes the
// admin theme context through. Consumer App.vue becomes 3 lines.
export { default as TAdminAppRoot } from './TAdminAppRoot.vue'
// Phase I.7.7: header companion components - auto-derived breadcrumb +
// user-avatar dropdown.
export { default as TAdminAutoBreadcrumb } from './TAdminAutoBreadcrumb.vue'
export { default as TAdminUserAvatar } from './TAdminUserAvatar.vue'
