import { ref, watch, toValue, type MaybeRefOrGetter, type Ref } from 'vue'
import { useQueryScope } from './queryScope'
import { tryInjectDeepLinkConfig } from '../plugin/deepLinkConfig'

/**
 * Options for {@link useSectionRoute}. Every field accepts a ref / getter so a
 * component can forward its (reactive) props straight through.
 */
export interface UseSectionRouteOptions {
  /**
   * Valid section keys — used to reject a stale / hand-typed `?section=garbage`
   * so it falls back to the default instead of rendering a blank panel. Accepts
   * the `DetailSection[]` shape (`{ key }`) or plain strings. An empty/omitted
   * list disables validation (any non-empty value is accepted).
   */
  sections?: MaybeRefOrGetter<ReadonlyArray<{ key: string } | string> | undefined>
  /** Section to land on when the URL carries none (or an invalid one). */
  defaultSection?: MaybeRefOrGetter<string | null | undefined>
  /**
   * The query key this nav OWNS. Lets several independent section navs coexist
   * in ONE URL without clobbering each other: a page detail's side menu uses
   * `'section'` (the default), and a modal/drawer detail opened ON TOP of it
   * uses its own key (e.g. `'edit'`) so the URL becomes
   * `?section=tools&edit=perms` — each instance reads/writes only its own key
   * and leaves the others (and any business query params) intact.
   */
  key?: MaybeRefOrGetter<string>
  /**
   * History strategy for user-driven section switches once the key exists.
   * `'push'` (default) makes the browser Back button step back through sections;
   * `'replace'` keeps them out of history. The *first* write (key absent →
   * present, i.e. writing the resolved default) always uses `replace` so the
   * landing section never adds a spurious history entry.
   */
  history?: MaybeRefOrGetter<'push' | 'replace'>
  /**
   * Master switch. Default `true`. When `false` the returned ref behaves like a
   * plain `ref` (no router reads or writes) — lets a component pass a reactive
   * flag to keep its controlled-mode behaviour untouched (e.g. a transient
   * drawer that should not deep-link).
   */
  enabled?: MaybeRefOrGetter<boolean>
}

function keySet(list: ReadonlyArray<{ key: string } | string> | undefined): Set<string> {
  const set = new Set<string>()
  for (const item of list ?? []) set.add(typeof item === 'string' ? item : item.key)
  return set
}

/**
 * Two-way bind an "active section" string to a **URL query key** so detail
 * sections (a `TDetailLayout` `tabs` / `side` nav, a settings page, a
 * modal/drawer detail) become **deep-linkable** AND the browser
 * **Back / Forward** buttons step through them — uniformly across page, modal
 * and drawer presentation.
 *
 * Returns a writable `ref`:
 * - assign to it to switch section → the URL updates;
 * - a Back / Forward / address-bar / deep-link navigation writes back into it →
 *   the UI follows.
 *
 * Built on {@link useQueryScope} (one owner per query key), so several navs
 * plus overlay open-states compose in one URL (`?section=tools&edit=perms`)
 * without touching the route `path`, sibling query params or the `#hash`.
 *
 * Design notes:
 * - The first write (key absent) uses `replace` so the resolved default
 *   section never adds a history entry; later switches honour `history` (push),
 *   so Back returns to the previously viewed section within the same tab.
 * - Both directions are guarded by an idempotent value-compare (write only when
 *   the other side actually differs), which dissolves the classic ref⇄route
 *   feedback loop without a fragile time-based flag.
 * - Degrades to a plain ref when no router is present (unit tests) or when
 *   `enabled` is false (overlay modes where URL syncing is intentionally off).
 */
// A static non-null `defaultSection` guarantees the ref always resolves to a
// section, so callers (e.g. an `NTabs` v-model) get `Ref<string>` without casts.
export function useSectionRoute(options: UseSectionRouteOptions & { defaultSection: string }): Ref<string>
export function useSectionRoute(options?: UseSectionRouteOptions): Ref<string | null>
export function useSectionRoute(options: UseSectionRouteOptions = {}): Ref<string | null> {
  // App-wide section-channel kill switch (`defineAdminApp({ deepLink })`) is
  // enforced HERE so every consumer — useDetail's section nav AND pages that
  // bind an NTabs directly — honours it without per-page wiring. Global
  // disable > per-call `enabled` > default on.
  const deepLink = tryInjectDeepLinkConfig()
  const isOn = (): boolean => deepLink.section && (toValue(options.enabled) ?? true)
  const validKeys = (): Set<string> => keySet(toValue(options.sections))

  // The underlying scope stays permanently active (router permitting): THIS
  // composable gates every read/write on `isOn()` itself, because deactivation
  // needs one LAST write (dropping the key from the URL) after `enabled` has
  // already flipped false — an enabled-gated scope would swallow it.
  const scope = useQueryScope(() => toValue(options.key) ?? 'section')

  function fallback(): string | null {
    const explicit = toValue(options.defaultSection)
    if (explicit != null) return explicit
    // An explicitly-provided `defaultSection` that currently resolves to nothing
    // (e.g. an async section list not yet loaded) means "defer": return null and
    // let the re-resolve-on-sections-change watch seed the first section once they
    // arrive, instead of locking onto whatever single placeholder exists now.
    if ('defaultSection' in options) return null
    const list = toValue(options.sections) ?? []
    const first = list[0]
    if (first == null) return null
    return typeof first === 'string' ? first : first.key
  }

  function accept(value: string | null): value is string {
    if (value == null) return false
    const keys = validKeys()
    return keys.size === 0 || keys.has(value)
  }

  function resolveInitial(): string | null {
    if (isOn()) {
      const fromUrl = scope.read()
      if (accept(fromUrl)) return fromUrl
    }
    return fallback()
  }

  const current = ref<string | null>(resolveInitial())

  // Re-resolve when the (possibly async-loaded) section list changes: adopt a
  // now-valid deep-linked URL value, else seed the resolved default once the
  // current choice is missing/invalid. Lets async section lists (e.g. a settings
  // page whose groups load over the wire) deep-link + default cleanly without the
  // consumer hand-seeding after load. Runs with OR without a router (no router →
  // pure default seeding). Static section lists never fire this.
  watch(
    () => toValue(options.sections),
    () => {
      if (isOn()) {
        const fromUrl = scope.read()
        if (accept(fromUrl)) {
          if (fromUrl !== current.value) current.value = fromUrl
          return
        }
      }
      if (!accept(current.value)) {
        const next = fallback()
        if (next !== current.value) current.value = next
      }
    },
  )

  const navigate = (key: string | null): void => {
    // First write (key absent from the URL) → replace, so the landing/default
    // section doesn't push a history entry; an existing key → honour `history`.
    const mode: 'push' | 'replace' =
      scope.read() != null ? toValue(options.history) ?? 'push' : 'replace'
    scope.set(key, mode)
  }

  /**
   * Activate this nav: adopt an existing (valid) deep-linked value if the URL
   * already carries one — e.g. a shared `?section=tools&edit=perms` link
   * reopened — otherwise make the URL self-describing by writing the resolved
   * section in (via replace, no history entry).
   */
  function activate(): void {
    const fromUrl = scope.read()
    if (accept(fromUrl)) {
      if (fromUrl !== current.value) current.value = fromUrl
    } else if (current.value != null) {
      navigate(current.value)
    }
  }

  // ref → URL (user switched section).
  watch(current, (key) => {
    if (isOn()) navigate(key)
  })

  // URL → ref (Back / Forward / address bar / deep-link navigation). The
  // underlying scope masks its own in-flight writes, so this only fires for
  // external navigations; the idempotent compare makes echoes no-ops.
  watch(scope.value, () => {
    if (!isOn()) return
    const fromUrl = scope.value.value
    const next = accept(fromUrl) ? fromUrl : fallback()
    if (next !== current.value) current.value = next
  })

  // Enable/disable lifecycle — chiefly an overlay (modal/drawer) opening and
  // closing. Activating writes/adopts the key; deactivating DROPS this nav's
  // key from the URL (via replace, no history entry) so a closed overlay never
  // leaves a stale `&edit=perms` deep-link behind.
  watch(
    () => isOn(),
    (on, was) => {
      if (on === was) return
      if (on) activate()
      else scope.set(null, 'replace')
    },
  )

  if (isOn() && scope.active()) activate()

  return current
}
