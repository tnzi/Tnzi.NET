/**
 * Overlay chrome primitives - the shared NModal / NDrawer shells.
 *
 * They own ONLY the chrome (card preset + width cap + auto-fullscreen +
 * long-body scroll for the modal; drawer-content + closable for the drawer);
 * the caller owns the body and footer.
 *
 * Sunk here from `@tnzi/ui-admin` on 2026-08-05. They were never admin-specific
 * - measured zero coupling to admin stores, routes or permissions - while the
 * ecosystem had grown three parallel implementations of the same chrome (these,
 * a hand-rolled `TDrawer` here that nothing consumed, and four hand-rolled
 * overlays in `@tnzi/ui-ai`). Chrome is also where the theme guarantee is
 * enforced, so having one implementation is worth more than the layering purity
 * of keeping them in the package that happened to need them first.
 */
export { default as TModalShell } from './TModalShell.vue'
export { default as TDrawerShell } from './TDrawerShell.vue'
/**
 * Renderless theme-reset wrapper for hand-rolled `NModal` / `NDrawer` overlays
 * that do not go through the shells above - keeps a page overlay on the global
 * light/dark mode instead of the content area's per-surface card theme.
 */
export { default as TOverlayTheme } from './TOverlayTheme.vue'
