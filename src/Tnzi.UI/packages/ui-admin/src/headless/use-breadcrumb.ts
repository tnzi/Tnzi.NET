import { watch, toValue, getCurrentScope, onScopeDispose, type MaybeRefOrGetter } from 'vue'
import { useRoute } from 'vue-router'
import { useAdminBreadcrumbStore, type BreadcrumbItem } from '../stores/useAdminBreadcrumbStore'
import { isMultiInstanceRoute, multiInstanceKey } from '../stores/useAdminTabStore'
import { useDesktopWindowId } from './desktop-window-context'

/**
 * Per-instance breadcrumb key for a route - identical logic to the tab store's
 * tab id (param routes → `route.path`, query-agnostic; multiTab+query →
 * `fullPath`; otherwise route name). Guarantees a contribution written by
 * {@link useBreadcrumbTrail} / {@link useBreadcrumbLabel} targets the SAME record
 * instance the breadcrumb component reads back - so two KeepAlive detail tabs
 * (customer A vs B) keep distinct trails.
 */
export function breadcrumbRouteKey(route: {
  name?: unknown
  path: string
  fullPath: string
  params?: object | null
  query?: object | null
  meta?: Record<string, unknown> | null
}): string {
  return isMultiInstanceRoute(route)
    ? multiInstanceKey(route)
    : typeof route.name === 'string'
      ? route.name
      : route.fullPath
}

/**
 * How long a held leaf may stay a placeholder before the breadcrumb gives up
 * and falls back to the route-derived title.
 *
 * The hold exists for the gap between "the page declared it owns the leaf" and
 * "the record arrived". A request that fails, or a record the user has no
 * access to, never closes that gap - and a placeholder pulsing forever reads as
 * a broken page, which is worse than the slightly wrong static title it
 * replaced. Generous on purpose: it is a failsafe, not a loading budget.
 */
export const BREADCRUMB_PENDING_TIMEOUT_MS = 8000

/**
 * Holds the leaf as "loading" until the page's first real value arrives.
 *
 * Contributed to the store rather than kept locally because the component that
 * RENDERS the breadcrumb is the shell header, not the page - the two only meet
 * through the store.
 */
function createPendingHold(store: ReturnType<typeof useAdminBreadcrumbStore>, key: string) {
  let timer: ReturnType<typeof setTimeout> | null = null
  let settled = false
  function stopTimer(): void {
    if (timer === null) return
    clearTimeout(timer)
    timer = null
  }
  return {
    /** No value yet - render a placeholder instead of the inherited list title. */
    hold(): void {
      if (settled || timer !== null) return
      store.markPending(key)
      timer = setTimeout(() => {
        timer = null
        store.resolvePending(key)
      }, BREADCRUMB_PENDING_TIMEOUT_MS)
    },
    /** A real value arrived (the store clears the hold itself on write). */
    settle(): void {
      settled = true
      stopTimer()
    },
    dispose(): void {
      stopTimer()
    },
  }
}

/**
 * Shared plumbing: resolve the current route key + store once at call time
 * (mirrors `useTabTitle`), run the caller's `apply`, and auto-clear the entry
 * when the calling scope disposes. No-op (swallowed) when there is no router /
 * pinia - e.g. isolated unit tests that mount a page without a shell.
 */
function useBreadcrumbContribution(
  apply: (
    store: ReturnType<typeof useAdminBreadcrumbStore>,
    key: string,
    hold: ReturnType<typeof createPendingHold>,
  ) => void,
): void {
  // The `desktop` layout renders no breadcrumb: there is no single "current
  // page" for the header to describe, so location belongs to each window's own
  // title bar. Contributing here would write to a global store nothing reads,
  // and two windows on the same route would collide on one key while doing it.
  if (useDesktopWindowId()) return

  let key: string
  let store: ReturnType<typeof useAdminBreadcrumbStore>
  try {
    key = breadcrumbRouteKey(useRoute())
    store = useAdminBreadcrumbStore()
  } catch {
    return
  }
  const hold = createPendingHold(store, key)
  apply(store, key, hold)
  if (getCurrentScope()) {
    onScopeDispose(() => {
      hold.dispose()
      store.clear(key)
    })
  }
}

/**
 * Declare the CURRENT detail page's full breadcrumb trail from a reactive
 * source - the escape hatch for cross-entity drill-downs the static route tree
 * can't express.
 *
 * The route tree is flat (`clients/:id` and `matters/:id` are siblings), so a
 * file reached THROUGH a client has no static ancestry linking the two. Calling
 * this lets the page state the real path, record names and all:
 *
 * `to` is a resolved path string, so build it from the ROUTE NAME - a
 * hardcoded `/admin/...` literal dangles under `defineAdminApp({ basePath })`:
 *
 * ```ts
 * const router = useRouter()
 * const clientsPath = router.resolve({ name: 'clients' }).path
 * useBreadcrumbTrail(() => matter.value ? [
 *   { label: 'admin.modules.crm.clients.title', to: clientsPath },
 *   { label: matter.value.clientName, to: `${clientsPath}/${matter.value.clientId}?section=files` },
 *   { label: matter.value.matterNumber },  // leaf, non-navigable (no `to`)
 * ] : [])
 * ```
 *
 * Each item's `label` is rendered verbatim when it is a human string (record
 * name) or resolved through i18n when it is a dotted key. Falsy / empty arrays
 * are ignored (the breadcrumb keeps its route-derived fallback until the record
 * loads). Overrides {@link useBreadcrumbLabel} when both are set.
 *
 * @param source ref / getter producing the trail; re-runs as the record loads.
 */
export function useBreadcrumbTrail(source: MaybeRefOrGetter<BreadcrumbItem[] | null | undefined>): void {
  useBreadcrumbContribution((store, key, hold) => {
    watch(
      () => toValue(source),
      (items) => {
        if (items && items.length) {
          hold.settle()
          store.setTrail(key, items)
        } else {
          // Empty is not "nothing to say" - it is "not yet". Holding the leaf
          // keeps the breadcrumb from spending the load showing the list route's
          // own title as if it were this record's name.
          hold.hold()
        }
      },
      { immediate: true, deep: true },
    )
  })
}

/**
 * Override just the trailing (leaf) breadcrumb crumb's label from a reactive
 * source - the breadcrumb twin of `useTabTitle`.
 *
 * A detail route inherits the static list `meta.title` (e.g. "Clients"), so the
 * breadcrumb leaf reads "Clients" instead of the record. Calling
 * `useBreadcrumbLabel(() => client.value?.name)` makes the leaf show the record
 * name (→ `Clients / John Smith`) while keeping the route-derived parent chain.
 * For a chain that also needs synthetic/renamed PARENTS, use
 * {@link useBreadcrumbTrail} instead.
 *
 * @param source ref / getter producing the leaf label; falsy values are ignored.
 */
export function useBreadcrumbLabel(source: MaybeRefOrGetter<string | null | undefined>): void {
  useBreadcrumbContribution((store, key, hold) => {
    watch(
      () => toValue(source),
      (label) => {
        if (label) {
          hold.settle()
          store.setLeafLabel(key, label)
        } else {
          hold.hold()
        }
      },
      { immediate: true },
    )
  })
}
