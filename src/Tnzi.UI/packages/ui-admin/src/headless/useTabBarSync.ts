/**
 * Keeps naive-ui's active-tab underline under the active tab when the tab strip
 * re-flows AFTER mount.
 *
 * naive re-measures the bar whenever the active tab changes, and it does watch
 * the nav with a ResizeObserver - but that handler is keyed on the nav's own
 * WIDTH (`_handleNavResize` returns early while `entry.contentRect.width` is
 * unchanged), and a full-width tab strip does not change width when one of the
 * tabs inside it grows. So a label that gains an asynchronously-fetched count or
 * status badge widens its own tab and shifts every tab after it, while the bar
 * stays where it was measured at mount.
 *
 * Clicking still looks fine (a value change re-syncs), so the symptom is
 * specific: deep-link straight into a tab that sits AFTER the one that grew and
 * the underline sits under the previous tab. The pane renders correctly - only
 * the underline lies, which is why it survives review.
 *
 * This observes the tab elements themselves, so the trigger is the rendered
 * outcome (a label grew, a label shrank, a webfont finished loading) rather than
 * the particular way the consumer wired its reactivity, and it re-measures
 * through naive's own public `TabsInst.syncBarPosition()` rather than writing to
 * the bar directly.
 *
 * Loop-free by construction: `syncBarPosition` only writes to `.n-tabs-bar`,
 * which is absolutely positioned, so a sync can never resize an observed tab and
 * feed itself.
 */
import { getCurrentScope, onMounted, onScopeDispose, watch, type Ref, type WatchSource } from 'vue'

/**
 * The outermost `*-tabs-nav`. Matched on a class substring so an app that sets a
 * custom `clsPrefix` on `NConfigProvider` still resolves. Scoping to the nav
 * matters: pane content can legitimately carry `data-name` of its own, and
 * observing pane internals would fire on every content reflow.
 */
const NAV_SELECTOR = '[class*="-tabs-nav"]'

/** naive stamps `data-name` on each tab element - it is what its own
 *  `getCurrentEl()` queries to find the tab to measure. */
const TAB_SELECTOR = '[data-name]'

/**
 * What this hook needs from the target. Structurally satisfied by a naive-ui
 * `TabsInst` template ref (`syncBarPosition` is its only member) plus the `$el`
 * every component public instance carries. Declared structurally rather than
 * importing naive's type so a test - or a different tab implementation - can
 * hand it a stand-in.
 */
export interface TabBarSyncTarget {
  /** Optional: a stubbed or swapped-out tab component simply cannot be measured. */
  syncBarPosition?: () => void
  $el?: unknown
}

export interface UseTabBarSyncReturn {
  /** Re-measure now. For the changes an observer cannot see - a caller that
   *  knows it just moved the strip itself. */
  sync: () => void
}

/**
 * @param target   Template ref to the `NTabs` instance.
 * @param resyncOn Optional watch source that changes when the tab SET changes
 *                 (names added / removed / reordered). Growing labels do not
 *                 need it - that is what the observer is for.
 */
export function useTabBarSync(
  target: Ref<TabBarSyncTarget | null | undefined>,
  resyncOn?: WatchSource<unknown>,
): UseTabBarSyncReturn {
  let observer: ResizeObserver | null = null

  function sync(): void {
    // Feature-detected, not assumed. This is a cosmetic enhancement layered on
    // someone else's component: a test that stubs `NTabs`, an app that swaps in
    // its own tab implementation, or a naive version that drops the method must
    // lose the underline correction - not the page.
    const inst = target.value
    if (typeof inst?.syncBarPosition === 'function') inst.syncBarPosition()
  }

  function attach(): void {
    observer?.disconnect()
    const ro = observer
    const root = target.value?.$el
    if (ro && root instanceof Element) {
      const nav = root.querySelector(NAV_SELECTOR) ?? root
      nav.querySelectorAll(TAB_SELECTOR).forEach((tab) => ro.observe(tab))
    }
    // Unconditional, and not just an observer side effect: adding or removing a
    // tab BEFORE the active one shifts the active tab without resizing anything
    // that already existed, so no resize is ever reported for it.
    sync()
  }

  onMounted(() => {
    // Guarded rather than assumed: SSR has no ResizeObserver and happy-dom ships
    // a no-op stub. Without one the hook still re-syncs on `resyncOn`, it just
    // cannot see a label change size.
    if (typeof ResizeObserver !== 'undefined') observer = new ResizeObserver(sync)
    attach()
  })

  // `post` so the DOM already holds the new tabs when we go looking for them.
  if (resyncOn) watch(resyncOn, attach, { flush: 'post' })

  if (getCurrentScope()) {
    onScopeDispose(() => {
      observer?.disconnect()
      observer = null
    })
  }

  return { sync }
}
