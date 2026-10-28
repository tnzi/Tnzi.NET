/**
 * The pieces `TAdminRouterView` needs to keep tab pages alive.
 *
 * ## Why a holder component per page
 *
 * `<KeepAlive :include>` matches COMPONENT names, while everything that decides
 * what to cache (route `meta.keepAlive`, the open tabs, the theme's "Cache tab
 * pages" switch) speaks in route names and tab ids. A page component's name is
 * its file name (`Users`, `Invoices`, several `Settings`), unrelated to its
 * route, and two routes may share one component. So every page renders inside
 * a holder whose component name is minted for its cache key (the tab id); the
 * include list is then just "the names of the keys that may stay alive".
 *
 * ## Why the holder scopes the route
 *
 * `useRoute()` hands every page the ONE global route. A page kept alive in the
 * background would go on reacting to it: its `?detail=` / `?section=` sync,
 * its `route.params.id` watcher and its hand-off readers would fire for
 * whatever page is now on screen (opening its own overlay with another page's
 * record id, rewriting that page's query). The holder provides the page's own
 * view of the route instead: it follows the global route while the global
 * route is this page's (same cache key) and stands still otherwise. When the
 * page is shown again it catches up in one step, exactly like a navigation.
 * `routerViewLocationKey` is scoped the same way, so a `<RouterView>` nested in
 * a kept layout does not render other pages into the hidden cache.
 */
import {
  computed,
  defineComponent,
  provide,
  shallowReactive,
  shallowRef,
  watch,
  type Component,
  type PropType,
  type VNode,
} from 'vue'
import {
  START_LOCATION,
  routeLocationKey,
  routerViewLocationKey,
  useRouter,
  type RouteLocationNormalizedLoaded,
} from 'vue-router'
import { isMultiInstanceRoute, multiInstanceKey } from '../../stores/useAdminTabStore'

/**
 * Cache key of a route, in lockstep with the tab store's tab id. Single-instance
 * routes key by name, so a query-only change on a list reuses its instance.
 * Multi-instance routes (dynamic params, or `meta.multiTab` with a query) key per
 * instance, so record A's detail and record B's detail are separate instances.
 */
export function pageKeyOf(route: RouteLocationNormalizedLoaded): string {
  if (isMultiInstanceRoute(route)) return multiInstanceKey(route)
  return (typeof route.name === 'string' ? route.name : undefined) ?? route.fullPath
}

/**
 * A holder for one cache key: renders the page vnode it is given and scopes
 * the route for everything below it (see the module comment).
 */
export function defineKeptPage(name: string, key: string): Component {
  return defineComponent({
    name,
    props: {
      /** The page vnode `<RouterView>` produced for this key. */
      page: { type: Object as PropType<VNode>, required: true },
    },
    setup(props) {
      const router = useRouter()
      const shown = shallowRef<RouteLocationNormalizedLoaded>(router.currentRoute.value)
      // Synchronous on purpose: a page that is leaving must not see even one
      // flush of the next page's route, and a page arriving must see its own
      // route before any of its watchers run.
      watch(
        router.currentRoute,
        (current) => {
          if (pageKeyOf(current) === key) shown.value = current
        },
        { flush: 'sync' },
      )

      const route = {} as Record<string, unknown>
      for (const field of Object.keys(START_LOCATION)) {
        Object.defineProperty(route, field, {
          get: () => (shown.value as unknown as Record<string, unknown>)[field],
          enumerable: true,
        })
      }
      provide(routeLocationKey, shallowReactive(route) as unknown as RouteLocationNormalizedLoaded)
      provide(routerViewLocationKey, computed(() => shown.value))

      return () => props.page
    },
  })
}
