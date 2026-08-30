import { computed, provide, reactive, type ComputedRef } from 'vue'
import {
  routeLocationKey,
  routerKey,
  useRouter,
  type RouteLocationNormalizedLoaded,
  type RouteLocationRaw,
  type Router,
} from 'vue-router'
import { useAdminDesktopStore, type OpenWindowInput } from '../stores/useAdminDesktopStore'
import { provideDesktopWindowId } from './desktop-window-context'

/**
 * Give one desktop window its own `useRoute()` / `useRouter()`.
 *
 * ## Why this exists
 *
 * `<RouterView>` renders exactly one component because a router has exactly one
 * `currentRoute`. The desktop needs N pages mounted and visible at once, each
 * believing it is "the current page" - so the windows cannot go through
 * `<RouterView>`, and the pages inside them must not see the global route
 * either (all N would read the same `?detail=` key and fight over it).
 *
 * `routerKey` and `routeLocationKey` are public value exports of vue-router,
 * and `useRoute()`/`useRouter()` are literally `inject()` on them. Providing our
 * own pair in the window shell's setup shadows the global ones for that subtree
 * by ordinary provide/inject scoping - so **every page works unchanged**,
 * including the 24 that call `useRoute`/`useRouter` directly and the ~84 that
 * reach the URL through `useDetail` / `useQueryScope`.
 *
 * ## What the fake router has to implement
 *
 * Narrower than it looks: across the whole package the pages and headless
 * facilities only ever call `push` / `replace` / `resolve` / `back`. `resolve`
 * delegates straight to the real router (it is a pure computation with no
 * navigation side effect, and `RouterLink` wants the full resolved shape, not
 * just a path). Everything else is either delegated or a well-behaved no-op.
 *
 * ## Navigation semantics
 *
 * `push` navigates **inside this window** rather than opening a new one. A row
 * click in a list should turn that window into the detail page with a working
 * back button - not spawn a second window, and not (as it would without this)
 * navigate the whole SPA and blow the desktop away. "Open in new window" is a
 * separate, explicit command.
 */
export interface UseWindowRouteReturn {
  /** The synthetic route handed to the page in this window. */
  route: RouteLocationNormalizedLoaded
  /** Whether this window can go back - replaces `history.state.back`, which
   *  is global and cannot tell one window from another. */
  canGoBack: ComputedRef<boolean>
}

/** Route meta fields this module reads off a resolved record. */
interface ResolvedMeta {
  title?: string
  icon?: string
}

export function useWindowRoute(windowId: string): UseWindowRouteReturn {
  const desktop = useAdminDesktopStore()
  // The real router stays available to THIS composable (it was injected before
  // we shadow it) and is what we delegate resolution to.
  const realRouter = useRouter()

  /**
   * Anchor a PARTIAL location to this window's current page.
   *
   * `router.push({ query })` with no `path`/`name` means "same page, different
   * query" - the single most common navigation in this package, since it is how
   * `useQueryScope` drives `?detail=` and `?section=` (~84 call sites through
   * `useDetail` / `TTabsPage`). vue-router resolves such a location against its
   * OWN current route, and ours is the global one, which has nothing to do with
   * this window: the query would be grafted onto whatever page the address bar
   * happens to sit on and the window would jump there.
   *
   * Measured before the fix: clicking a row in a Users window navigated to the
   * detail page correctly, then the detail page's own `?section=` sync bounced
   * the window to Dashboard - because the global route was `/admin/dashboard`.
   */
  function anchor(to: RouteLocationRaw): RouteLocationRaw {
    if (typeof to === 'string') return to
    const named = 'name' in to && to.name != null
    const pathed = 'path' in to && to.path != null
    if (named || pathed) return to
    const current = desktop.locationOf(windowId)
    if (!current) return to
    return { name: current.name, params: { ...current.params }, ...to }
  }

  /**
   * Turn a `RouteLocationRaw` into the flat, serialisable shape the window
   * stack stores. Resolution goes through the real router so named routes,
   * params and path building all behave exactly as they do outside a window -
   * but only after {@link anchor} has made the target absolute.
   */
  function toInput(to: RouteLocationRaw): OpenWindowInput | null {
    const resolved = realRouter.resolve(anchor(to))
    const name = typeof resolved.name === 'string' ? resolved.name : null
    if (!name) return null
    const meta = (resolved.meta ?? {}) as ResolvedMeta
    const query: Record<string, string> = {}
    for (const [k, v] of Object.entries(resolved.query)) {
      if (typeof v === 'string') query[k] = v
      else if (Array.isArray(v) && typeof v[0] === 'string') query[k] = v[0]
    }
    const params: Record<string, string> = {}
    for (const [k, v] of Object.entries(resolved.params)) {
      if (typeof v === 'string') params[k] = v
      else if (Array.isArray(v) && typeof v[0] === 'string') params[k] = v[0]
    }
    return { name, path: resolved.path, title: meta.title ?? name, icon: meta.icon, params, query }
  }

  const location = computed(() => desktop.locationOf(windowId))

  /** Resolve the window's current location against the real route table to
   *  recover `matched` / `meta` / `fullPath` - the parts a synthetic object
   *  cannot invent but `TPageHeader`, `RouterLink` and the guards read. */
  const resolved = computed(() => {
    const loc = location.value
    if (!loc) return null
    try {
      return realRouter.resolve({ name: loc.name, params: loc.params, query: loc.query })
    } catch {
      // A window persisted against a route that no longer exists (renamed page,
      // revoked permission). Render nothing rather than throwing the desktop down.
      return null
    }
  })

  /**
   * The route object handed to the page.
   *
   * `reactive` over per-field `computed`, NOT `reactive` + `watchEffect`.
   * vue-router hands pages a reactive object (they write `route.params.id`,
   * never `route.value.params.id`), so the shape has to be a plain object -
   * but the fields must be derived SYNCHRONOUSLY.
   *
   * `watchEffect` flushes on the pre-queue, i.e. after the current tick. In
   * that window the page component for the new location has already mounted
   * and read `route.params.id` - off the PREVIOUS location. Measured: clicking
   * a row navigated the window to the user detail page, which then rendered
   * "Loading..." forever and threw reading `.email`, because its `params.id`
   * was still the list page's (empty) params. Per-field computed close that
   * gap: values are pulled at access time, so a component mounting in the same
   * tick as the navigation sees the location it was mounted for.
   */
  const route = reactive({
    name: computed(() => location.value?.name ?? ''),
    path: computed(() => resolved.value?.path ?? location.value?.path ?? ''),
    fullPath: computed(() => resolved.value?.fullPath ?? location.value?.path ?? ''),
    params: computed(() => ({ ...(location.value?.params ?? {}) })),
    query: computed(() => ({ ...(location.value?.query ?? {}) })),
    hash: '',
    matched: computed(() => resolved.value?.matched ?? []),
    meta: computed(() => resolved.value?.meta ?? {}),
    redirectedFrom: undefined,
  })

  const noopUnregister = (): void => undefined

  const windowRouter = {
    currentRoute: computed(() => route),
    options: realRouter.options,
    listening: false,

    push: (to: RouteLocationRaw) => {
      const input = toInput(to)
      if (input) desktop.navigate(windowId, input)
      return Promise.resolve()
    },
    replace: (to: RouteLocationRaw) => {
      const input = toInput(to)
      if (input) desktop.replace(windowId, input)
      return Promise.resolve()
    },
    back: () => desktop.back(windowId),
    forward: () => desktop.forward(windowId),
    go: (delta: number) => {
      const steps = Math.abs(delta)
      for (let i = 0; i < steps; i += 1) {
        if (delta < 0) desktop.back(windowId)
        else desktop.forward(windowId)
      }
    },

    // Pure resolution - no navigation happens, so delegating is both correct
    // and necessary (RouterLink reads `matched` off the result for its
    // active-class logic, not just `href`).
    resolve: (to: RouteLocationRaw) => realRouter.resolve(to),
    getRoutes: () => realRouter.getRoutes(),
    hasRoute: (name: Parameters<Router['hasRoute']>[0]) => realRouter.hasRoute(name),
    addRoute: (...args: Parameters<Router['addRoute']>) => realRouter.addRoute(...args),
    removeRoute: (name: Parameters<Router['removeRoute']>[0]) => realRouter.removeRoute(name),
    isReady: () => Promise.resolve(),

    // A page has no business installing global guards, and one that did would
    // be registering them once per window. Accept and ignore, returning the
    // unregister function the signature promises.
    beforeEach: () => noopUnregister,
    beforeResolve: () => noopUnregister,
    afterEach: () => noopUnregister,
    onError: () => noopUnregister,
    install: () => undefined,

    // Probe read by `runBack`'s smart-back branch. Without it that branch asks
    // `window.history.state.back`, which is global and says nothing about which
    // window is asking - every window would report "yes, I can go back" as soon
    // as the SPA had any history at all, and then step the WHOLE app back.
    __tnziCanGoBack: () => desktop.canGoBack(windowId),
  }

  provide(routeLocationKey, route as unknown as RouteLocationNormalizedLoaded)
  provide(routerKey, windowRouter as unknown as Router)
  // Lets the two facilities that write to app-global stores keyed by route
  // (`useTabTitle`, `useBreadcrumb*`) find out they are inside a window.
  provideDesktopWindowId(windowId)

  return {
    route: route as unknown as RouteLocationNormalizedLoaded,
    canGoBack: computed(() => desktop.canGoBack(windowId)),
  }
}
