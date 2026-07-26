// Overlay chrome primitives - the shared NModal / NDrawer shells that back the
// higher-level overlay components (TFormModal, TDetailHost's modal/drawer
// branches, TListShell's read-only view drawer). They own ONLY the chrome
// (card preset + width cap + auto-fullscreen + long-body scroll for the modal;
// drawer-content + closable for the drawer); the callers own the body + footer.
export { default as TModalShell } from './TModalShell.vue'
export { default as TDrawerShell } from './TDrawerShell.vue'
// Renderless theme-reset wrapper for hand-rolled NModal / NDrawer overlays that
// don't go through the shells above - keeps every page overlay on the global
// light/dark mode instead of the content area's per-surface card theme.
export { default as TOverlayTheme } from './TOverlayTheme.vue'
