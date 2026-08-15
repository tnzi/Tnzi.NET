import { watch, toValue, getCurrentScope, onScopeDispose, type MaybeRefOrGetter } from 'vue'
import { useRoute } from 'vue-router'
import { useAdminBreadcrumbStore, type BreadcrumbItem } from '../stores/useAdminBreadcrumbStore'
import { isMultiInstanceRoute, multiInstanceKey } from '../stores/useAdminTabStore'

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
 * Shared plumbing: resolve the current route key + store once at call time
 * (mirrors `useTabTitle`), run the caller's `apply`, and auto-clear the entry
 * when the calling scope disposes. No-op (swallowed) when there is no router /
 * pinia - e.g. isolated unit tests that mount a page without a shell.
 */
function useBreadcrumbContribution(
  apply: (store: ReturnType<typeof useAdminBreadcrumbStore>, key: string) => void,
): void {
  let key: string
  let store: ReturnType<typeof useAdminBreadcrumbStore>
  try {
    key = breadcrumbRouteKey(useRoute())
    store = useAdminBreadcrumbStore()
  } catch {
    return
  }
  apply(store, key)
  if (getCurrentScope()) onScopeDispose(() => store.clear(key))
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
  useBreadcrumbContribution((store, key) => {
    watch(
      () => toValue(source),
      (items) => {
        if (items && items.length) store.setTrail(key, items)
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
  useBreadcrumbContribution((store, key) => {
    watch(
      () => toValue(source),
      (label) => {
        if (label) store.setLeafLabel(key, label)
      },
      { immediate: true },
    )
  })
}
