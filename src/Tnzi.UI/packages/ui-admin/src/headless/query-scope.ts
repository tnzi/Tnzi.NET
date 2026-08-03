import { ref, watch, toValue, type MaybeRefOrGetter, type Ref } from 'vue'
import {
  useRoute,
  useRouter,
  type LocationQueryRaw,
  type RouteLocationNormalizedLoaded,
  type Router,
} from 'vue-router'

/**
 * Low-level engine for "one piece of UI state living in ONE URL query key".
 *
 * Several independent UI states (a detail page's active section, a CRUD list's
 * open overlay, a custom drawer) each OWN one query key and read/write only
 * their own - e.g. `?section=tools&detail=view:42`. {@link useSectionRoute}
 * and `useDetail`'s overlay routing both build on this so they compose in a
 * single URL without colliding: namespacing is the query string's native
 * shape (one key per owner), no custom codec needed.
 *
 * Why the query (not the `#hash`): query params are the industry-standard
 * carrier for shareable UI state (tabs, open overlays, filters), and the hash
 * stays free for real anchors - a consumer's `scrollBehavior` that resolves
 * `to.hash` as an element selector keeps working. The multi-instance tab
 * system keys tabs on route `name`/`path`, so query changes never fork a tab
 * (routes opting into `meta.multiTab` are the one exception - do not combine
 * that flag with query-synced details/sections on the same route).
 */

export function tryRoute(): RouteLocationNormalizedLoaded | undefined {
  try {
    return useRoute() as RouteLocationNormalizedLoaded
  } catch {
    return undefined
  }
}

export function tryRouter(): Router | undefined {
  try {
    return useRouter() as Router
  } catch {
    return undefined
  }
}

export interface UseQueryScopeReturn {
  /**
   * Reactive value of THIS key as reflected in the URL (null = the key is
   * absent). Updated by Back / Forward / address-bar / deep-link navigations
   * AND immediately by {@link set}, so a consumer can `watch(value, …)` to
   * drive its UI off the URL.
   */
  value: Ref<string | null>
  /** Whether URL syncing is live (a router + route exist AND `enabled` is on). */
  active: () => boolean
  /** Read this key's CURRENT value straight from the URL (no reactivity). */
  read: () => string | null
  /**
   * Write this key's value into the query (or remove it when `value` is null),
   * preserving the route `path`, every sibling query key AND the `#hash`. The
   * caller picks the history strategy explicitly: `'push'` (default) adds a
   * history entry so Back undoes it (opening an overlay), `'replace'` does not
   * (closing it, or a landing default). A no-op write issues no navigation.
   */
  set: (value: string | null, mode?: 'push' | 'replace') => void
}

/**
 * Two-way bind ONE query key. The low-level primitive behind
 * {@link useSectionRoute} (sections) and `useDetail`'s overlay routing.
 * Degrades to an inert local ref when no router is present (unit tests) or
 * `enabled` is false - `set` then only updates `value` locally and `active()`
 * returns false, so a consumer can cleanly skip its URL reconcilers.
 */
export function useQueryScope(
  key: MaybeRefOrGetter<string>,
  options: { enabled?: MaybeRefOrGetter<boolean> } = {},
): UseQueryScopeReturn {
  const route = tryRoute()
  const router = tryRouter()

  const keyName = (): string => toValue(key)
  const active = (): boolean => (toValue(options.enabled) ?? true) && !!route && !!router

  function read(): string | null {
    // A mock route in unit tests may omit `query`; a real vue-router always
    // provides an object.
    const raw = route?.query?.[keyName()]
    const v = Array.isArray(raw) ? raw[0] : raw
    return typeof v === 'string' && v.length > 0 ? v : null
  }

  const value = ref<string | null>(read())

  let pending = 0

  function set(next: string | null, mode: 'push' | 'replace' = 'push'): void {
    if (!active() || !route || !router) {
      value.value = next
      return
    }
    const before = read()
    value.value = next
    if (next === before) return
    const query: LocationQueryRaw = { ...route.query }
    if (next == null) delete query[keyName()]
    else query[keyName()] = next
    const navFn = router[mode]
    if (typeof navFn !== 'function') return
    pending++
    // Partial location (no path) resolves against the current route, so the
    // path stays put; sibling query keys ride along verbatim and the hash is
    // forwarded explicitly (a partial location would otherwise clear it).
    void Promise.resolve(navFn.call(router, { query, hash: route.hash ?? '' } as never))
      .catch(() => undefined)
      .finally(() => {
        pending--
      })
  }

  if (route && router) {
    // URL → value (Back / Forward / address bar / deep-link). The `pending`
    // counter masks the navigations `set` itself issues so a mid-flight value
    // never snaps back.
    watch(
      () => (active() ? route.query?.[keyName()] : undefined),
      () => {
        if (!active() || pending > 0) return
        const next = read()
        if (next !== value.value) value.value = next
      },
    )
  }

  return { value, active, read, set }
}
