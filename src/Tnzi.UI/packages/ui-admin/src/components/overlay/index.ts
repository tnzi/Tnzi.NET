/**
 * Overlay chrome primitives.
 *
 * **Sunk to `@tnzi/ui` on 2026-08-05** - this module now re-exports them so the
 * ~30 call sites in this package (and any consuming app importing from
 * `@tnzi/ui-admin`) keep working unchanged. They were never admin-specific:
 * measured zero coupling to admin stores / routes / permissions, while the
 * ecosystem carried three parallel implementations of the same chrome.
 *
 * Prefer importing from `@tnzi/ui` in new code.
 */
export { TModalShell, TDrawerShell, TOverlayTheme } from '@tnzi/ui'
