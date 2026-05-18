import { onMounted } from 'vue'
import type { Router } from 'vue-router'

/**
 * Tiny soybean-style top-of-page route loading bar — no nprogress dep.
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
 * Idempotent — calling twice on the same router is safe (the second `start`
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
    let pending = 0

    router.beforeEach((_to, _from, next) => {
      pending += 1
      root.dataset.tnziRouteLoading = 'on'
      next()
    })

    router.afterEach(() => {
      pending = Math.max(0, pending - 1)
      if (pending === 0) {
        // Two-frame delay lets the bar reach the "near done" position before
        // we fade it out, so the user perceives a real completion rather than
        // a flash.
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            delete root.dataset.tnziRouteLoading
          })
        })
      }
    })

    router.onError(() => {
      pending = Math.max(0, pending - 1)
      if (pending === 0) delete root.dataset.tnziRouteLoading
    })
  }

  // Allow call from setup() — if document is already there, attach
  // immediately; otherwise wait for mount.
  // (Bar is global and idempotent — no cleanup needed; multiple consumer SPAs
  // sharing one router won't break each other thanks to the ATTACHED guard.)
  if (typeof document !== 'undefined' && document.documentElement) {
    attach()
  } else {
    onMounted(attach)
  }
}
