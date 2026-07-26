import type { Router } from 'vue-router'

/**
 * Back-affordance target for `TPageHeader` / `TDetailLayout` / `TDetailHost`.
 *
 *  - `true`   → `router.back()` (browser history). Restores the origin page with
 *               its own deep-link state, but strands the user on a refresh /
 *               deep-link (no history entry).
 *  - `string` → `router.push(path)` (a static parent). Refresh-safe, but cannot
 *               carry the origin's sub-state (e.g. a `?section=files` tab).
 *  - object   → SMART back: use in-app history when it exists (best of both -
 *               restores the origin WITH its `?section=…` deep-link on normal
 *               in-app navigation), otherwise fall back to `fallback`. This is
 *               the recommended form for a drilled-into detail page.
 *
 * Example (a file detail returning to the client's Files tab):
 * ```ts
 * const back = computed(() => ({ fallback: `/admin/clients/${clientId.value}?section=files` }))
 * ```
 */
export type BackTarget = boolean | string | { fallback?: string }

/**
 * True when the SPA has a prior in-app history entry to step back to. vue-router
 * stores the previous location in `history.state.back` (null at the first entry,
 * i.e. a fresh deep-load), which is exactly the "can I go back within the app?"
 * signal a smart back needs. Guarded for SSR / test environments without a DOM.
 */
export function hasInAppHistory(): boolean {
  try {
    return typeof window !== 'undefined' && window.history?.state?.back != null
  } catch {
    return false
  }
}

/** Execute a {@link BackTarget} against the router (no-op without a router). */
export function runBack(target: BackTarget | undefined, router: Router | undefined): void {
  if (!router) return
  if (typeof target === 'string') {
    void router.push(target)
    return
  }
  if (target && typeof target === 'object') {
    // Smart: prefer the in-app history (keeps the origin's deep-link state),
    // else the declared fallback, else a plain back() as a last resort.
    if (hasInAppHistory()) {
      router.back()
      return
    }
    if (target.fallback) {
      void router.push(target.fallback)
      return
    }
    router.back()
    return
  }
  router.back()
}
