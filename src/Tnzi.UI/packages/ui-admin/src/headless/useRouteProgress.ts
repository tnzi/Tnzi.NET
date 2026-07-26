import { onMounted } from 'vue'
import type { Router } from 'vue-router'

/**
 * Tiny soybean-style top-of-page route loading bar - no nprogress dep.
 *
 * Toggles `<html data-tnzi-route-loading>` between empty and `"on"` around
 * each navigation; the matching CSS rule in `styles/transition.css` drives a
 * pure-CSS bar across the viewport top. ~30 lines of CSS, zero JS animation
 * loop, zero dep weight.
 *
 * Usage (typically called once in `createTnziUiAdmin` install):
 * ```ts
 * import { useRouteProgress } from '@tnzi/ui-admin/headless'
 * useRouteProgress(router)
 * ```
 *
 * Idempotent - calling twice on the same router is safe (the second `start`
 * just resets the bar to its initial state, and dedupe is enforced via a
 * symbol flag attached to the router instance).
 */
const ATTACHED = Symbol('tnzi-route-progress-attached')

export function useRouteProgress(router: Router): void {
  function attach(): void {
    type Flagged = Router & { [ATTACHED]?: boolean }
    const flagged = router as Flagged
    if (flagged[ATTACHED]) return
    flagged[ATTACHED] = true

    const root = document.documentElement

    // A monotonically increasing navigation sequence - NOT a balanced +1/-1
    // pending counter. A `pending` counter assumes `beforeEach` and `afterEach`
    // pair up one-to-one, but a redirect (e.g. the auth guard bouncing an
    // unauthenticated visit to `/login`, or `/` → `/dashboard`) fires
    // `beforeEach` for the aborted navigation without a matching landing
    // `afterEach`, so the counter never returns to zero and the bar sticks at
    // ~80% forever. Instead we bump `seq` on every navigation start and let each
    // settlement (`afterEach` fires for success, redirect AND abort; `onError`
    // for a thrown guard) try to finish - but only the newest navigation's
    // deferred cleanup actually removes the bar, so a redirect chain's
    // intermediate settlements are skipped (no flicker) while the final landing
    // navigation always clears it.
    let seq = 0

    function scheduleFinish(mine: number): void {
      // Two-frame delay lets the bar reach the "near done" position before we
      // fade it out, so the user perceives a real completion rather than a
      // flash. Bail if a newer navigation started in the meantime - its own
      // settlement owns the cleanup.
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          if (mine === seq) delete root.dataset.tnziRouteLoading
        })
      })
    }

    router.beforeEach((_to, _from, next) => {
      seq += 1
      root.dataset.tnziRouteLoading = 'on'
      next()
    })

    router.afterEach(() => scheduleFinish(seq))

    router.onError(() => scheduleFinish(seq))
  }

  // Allow call from setup() - if document is already there, attach
  // immediately; otherwise wait for mount.
  // (Bar is global and idempotent - no cleanup needed; multiple consumer SPAs
  // sharing one router won't break each other thanks to the ATTACHED guard.)
  if (typeof document !== 'undefined' && document.documentElement) {
    attach()
  } else {
    onMounted(attach)
  }
}
