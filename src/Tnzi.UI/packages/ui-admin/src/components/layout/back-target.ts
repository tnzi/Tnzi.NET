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
 * Example (a file detail returning to the client's Files tab). Resolve the
 * fallback BY ROUTE NAME - `defineAdminApp({ basePath })` rewrites the shell
 * prefix, so a hardcoded `/admin/...` literal dangles under any custom prefix,
 * and the fallback branch is exactly the refresh / cold-deep-link case:
 * ```ts
 * const router = useRouter()
 * const back = computed(() => ({
 *   fallback: router.resolve({ name: 'clients.detail', params: { id: clientId.value } }).path + '?section=files',
 * }))
 * ```
 */
export type BackTarget = boolean | string | { fallback?: string }

/**
 * A router that carries its own back-availability probe. The desktop layout's
 * per-window router sets this: its pages navigate inside a window stack that
 * the browser history knows nothing about.
 */
interface RouterWithBackProbe {
  __tnziCanGoBack?: () => boolean
}

/**
 * True when there is a prior in-app entry to step back to.
 *
 * Normally that means vue-router's `history.state.back` (null at the first
 * entry, i.e. a fresh deep-load), which is exactly the "can I go back within
 * the app?" signal a smart back needs. Guarded for SSR / test environments
 * without a DOM.
 *
 * When the router carries its own probe, ask that instead. Browser history is
 * a single global stack, so inside a desktop window it answers a question
 * nobody asked: with any SPA history at all it reports "yes" for EVERY open
 * window, and the resulting `back()` steps the whole app rather than the window
 * whose button was clicked.
 */
export function hasInAppHistory(router?: Router): boolean {
  const probe = (router as RouterWithBackProbe | undefined)?.__tnziCanGoBack
  if (typeof probe === 'function') {
    try {
      return probe()
    } catch {
      return false
    }
  }
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
    if (hasInAppHistory(router)) {
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
