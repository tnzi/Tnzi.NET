import { ref, computed, watch, toValue, getCurrentScope, onScopeDispose, type MaybeRefOrGetter, type Ref } from 'vue'
import { useRouter, type Router } from 'vue-router'
import { useFormModal, type FormModalMode, type UseFormModalReturn } from './useFormModal'
import { useSectionRoute } from './useSectionRoute'
import { useQueryScope, tryRoute } from './queryScope'
import { tryInjectDeepLinkConfig } from '../plugin/deepLinkConfig'

// ── Dev-only duplicate-key guard ─────────────────────────────────────────────
// Two engines on the SAME route claiming the SAME query key silently clobber
// each other (the docs demand distinct keys, but nothing enforced it). Track
// live registrations per `route.path::key` and warn on overlap. Dev builds
// only; skipped under vitest (mounted test harnesses share mock paths).
// (The package tsconfig carries no vite/client types — probe untyped.)
const metaEnv = (import.meta as unknown as { env?: { DEV?: boolean; VITEST?: unknown } }).env
const IS_DEV_GUARD = !!metaEnv?.DEV && !metaEnv?.VITEST
const liveDeepLinkKeys = new Map<string, number>()

function guardDeepLinkKey(path: string | undefined, key: string): void {
  if (!IS_DEV_GUARD || path == null || !getCurrentScope()) return
  const id = `${path}::${key}`
  const count = (liveDeepLinkKeys.get(id) ?? 0) + 1
  liveDeepLinkKeys.set(id, count)
  if (count > 1) {
    console.warn(
      `[tnzi-admin] Duplicate deep-link query key "${key}" on route "${path}" — ` +
        `two useDetail/section engines will clobber each other. Give each its own ` +
        `key via \`url: '<name>'\` / \`sectionUrl: '<name>'\`.`,
    )
  }
  onScopeDispose(() => {
    const left = (liveDeepLinkKeys.get(id) ?? 1) - 1
    if (left <= 0) liveDeepLinkKeys.delete(id)
    else liveDeepLinkKeys.set(id, left)
  })
}

export type DetailMode = 'modal' | 'drawer' | 'page'
export type DetailAction = FormModalMode // 'create' | 'edit' | 'view'
export type DetailLayout = 'plain' | 'tabs' | 'side'

export interface DetailSection {
  key: string
  label: string
  icon?: string
  group?: string
  disabled?: boolean
}

/**
 * A record source for deep-link hydration — typically the surrounding CRUD
 * list. Structurally compatible with a `useCrudPage` return value, so a page
 * can write `source: crud` and be done: a deep-linked `?roles=edit:<id>`
 * resolves from the already-loaded `items` (waiting for the first fetch via
 * `loading`), then falls back to `loadById` for records beyond the current
 * page. Replaces hand-wiring `loadData` + `retryDeepLinkOn` + `busy`.
 */
export interface DetailSource<T> {
  items: Ref<T[]> | (() => T[])
  loading?: Ref<boolean> | (() => boolean)
  loadById?: (id: string) => Promise<T | null>
}

export interface UseDetailOptions<T> {
  /** Presentation mode. Default 'modal'. */
  mode?: DetailMode
  /**
   * Section list driving the nav + URL validation. Accepts a ref/getter so a
   * page can forward an async/computed list (e.g. settings groups loaded over
   * the wire); the default re-resolves the moment the list changes.
   */
  sections?: MaybeRefOrGetter<DetailSection[]>
  /**
   * Section to land on. Accepts a ref/getter. When OMITTED entirely, the first
   * section is auto-picked; when PROVIDED but currently nullish (e.g. an async
   * default not yet resolved), the nav DEFERS (no spurious lock onto a
   * placeholder) until it resolves — mirrors {@link useSectionRoute}.
   */
  defaultSection?: MaybeRefOrGetter<string | null | undefined>
  /**
   * Load a record when opened with an id (view/edit, page-mode deep links AND
   * overlay deep links). May return `null` when the id can't be resolved — the
   * overlay deep-link reconciler then drops the dangling URL key. Prefer
   * {@link UseDetailOptions.source} when the record lives in a loaded list.
   */
  loadData?: (id: string | number) => Promise<T | null>
  /** Persist on submit. */
  submitData?: (action: DetailAction, data: T) => Promise<void>
  /** Page-mode routing target. Required for `mode: 'page'` open()/close(). */
  pageRoute?: { name: string; idParam?: string }
  /**
   * Keep the active section in a URL **query key** (deep-linkable + the
   * browser Back/Forward buttons step through sections). `true` uses the
   * default key `'section'`; a string names the key — a modal/drawer detail
   * opened ON TOP of a page detail MUST name its own key (e.g. `'edit'`) so
   * the two coexist in one URL (`?section=tools&edit=perms`) without
   * clobbering each other. Works in page, modal AND drawer mode.
   */
  sectionUrl?: boolean | string
  /**
   * Two-way sync the overlay's open-state (action + record id) into a URL
   * query key (`?<key>=view:<id>` / `=edit:<id>` / `=new`) so an opened
   * modal/drawer is **deep-linkable, refresh-survivable and Back-closeable**.
   * `true` uses the default key `'detail'`; a string names the key (several
   * overlays on one page each claim their own — e.g. `'roles'`,
   * `'reset-pwd'`). Default off for a bare useDetail; `useCrudPage` turns it
   * on by default. Ignored in page mode (there the route IS the open-state).
   * No-ops without a router.
   */
  url?: boolean | string
  /**
   * Derive the record id (used for BOTH the page-mode route param and the overlay
   * open-state URL) from a record payload. Defaults to reading `data.id`. Required
   * if you call `open(action, fullRecord)` in page mode with a non-`id` key, or if
   * the overlay URL should key on a field other than `id`.
   */
  getId?: (data: T) => string | number
  /**
   * Where deep-linked record ids resolve from — pass the surrounding CRUD
   * state (`source: crud`) or any `{ items, loading?, loadById? }`. Fills
   * `loadData` / `retryDeepLinkOn` / `busy` automatically; an explicitly
   * provided option always wins over the derived one.
   */
  source?: DetailSource<T>
  /**
   * Advanced. A reactive getter re-attempted as a deep-link source when it
   * changes — e.g. a list that loads after first paint. Derived from
   * {@link source} automatically; only set it for exotic data flows.
   */
  retryDeepLinkOn?: () => unknown
  /**
   * Advanced. While this returns true, a deep-link whose id can't be resolved
   * yet is KEPT (not wiped). Derived from {@link source} (its `loading`);
   * defaults to this detail's own `loading`.
   */
  busy?: () => boolean
}

export interface UseDetailReturn<T> {
  mode: Ref<DetailMode>
  action: Ref<DetailAction | null>
  visible: Ref<boolean>
  data: Ref<T | null>
  loading: Ref<boolean>
  error: Ref<Error | null>
  activeSection: Ref<string | null>
  open: (action: DetailAction, payload?: T | string | number | null) => Promise<void>
  close: () => void
  submit: () => Promise<void>
  setSection: (key: string) => void
  /**
   * The underlying form-modal primitive (`visible`/`mode`/`formData`/`open`/`close`).
   * Re-exposed so `useCrudPage` can delegate its public `formModal` to THIS single
   * instance — the overlay open-state URL sync below operates on these refs, so a
   * delegating consumer gets deep-linking for free without a second engine.
   */
  form: UseFormModalReturn<T>
}

function isId(v: unknown): v is string | number {
  return typeof v === 'string' || typeof v === 'number'
}

function tryGetRouter(): Router | undefined {
  try {
    return useRouter() as Router
  } catch {
    return undefined
  }
}

export function useDetail<T = unknown>(options: UseDetailOptions<T> = {}): UseDetailReturn<T> {
  const mode = ref<DetailMode>(options.mode ?? 'modal')
  const form = useFormModal<T>()
  const loading = ref(false)
  const error = ref<Error | null>(null)

  const router = tryGetRouter()
  // App-wide kill switch (`defineAdminApp({ deepLink })`). A disabled channel
  // overrides the per-page `url` / `sectionUrl` options — built-in pages set
  // those, so only a global gate lets a consumer opt the whole app out.
  const deepLink = tryInjectDeepLinkConfig()

  if (options.url || options.sectionUrl) {
    const path = tryRoute()?.path
    if (options.url) guardDeepLinkKey(path, typeof options.url === 'string' ? options.url : 'detail')
    if (options.sectionUrl) guardDeepLinkKey(path, typeof options.sectionUrl === 'string' ? options.sectionUrl : 'section')
  }

  function recordId(data: T): string | null {
    const raw = options.getId ? options.getId(data) : (data as { id?: unknown } | null)?.id
    return raw != null ? String(raw) : null
  }

  // Resolve the effective deep-link data plumbing: an explicit option always
  // wins; otherwise derive it from `source` (items-first, then loadById; retry
  // once the list arrives; treat the list's `loading` as busy).
  const src = options.source
  const loadData: ((id: string | number) => Promise<T | null>) | undefined =
    options.loadData ??
    (src
      ? async (id) => {
          const sid = String(id)
          const fromItems = toValue(src.items).find((r) => recordId(r) === sid)
          if (fromItems) return fromItems
          if (src.loadById) return src.loadById(sid)
          return null
        }
      : undefined)
  const retryDeepLinkOn: (() => unknown) | undefined =
    options.retryDeepLinkOn ?? (src ? () => toValue(src.items) : undefined)
  const srcLoading = src?.loading
  const busy: (() => boolean) | undefined =
    options.busy ?? (srcLoading ? () => !!toValue(srcLoading) : undefined)

  // Section nav. With `sectionUrl`, two-way bind to a URL query key
  // (deep-linkable + the browser Back/Forward buttons step through sections,
  // push history) in ANY mode — page, modal or drawer; otherwise a plain ref.
  // The shared composable owns the URL read/write — no hand-wired router calls.
  const activeSection = useSectionRoute({
    // Page sections sync whenever the page is mounted; an overlay's sections sync
    // only while it is OPEN, so a closed modal/drawer leaves no stale deep-link
    // and two overlays' keys never race to write the URL on first mount.
    enabled: () =>
      !!options.sectionUrl && deepLink.section && (mode.value === 'page' || form.visible.value),
    key: typeof options.sectionUrl === 'string' ? options.sectionUrl : 'section',
    sections: options.sections,
    // Preserve the caller's intent precisely: when they PROVIDED `defaultSection`
    // (even a getter that's currently nullish) forward it verbatim so the
    // defer-on-nullish semantics hold (an async settings page must not lock onto
    // its always-present Advanced placeholder); only when they gave NONE do we
    // synthesize "first section".
    defaultSection:
      'defaultSection' in options
        ? options.defaultSection
        : () => toValue(options.sections)?.[0]?.key,
  })

  async function loadIfNeeded(payload?: T | string | number | null): Promise<T | null> {
    if (payload == null) return null
    if (isId(payload)) {
      if (!loadData) return null
      loading.value = true
      error.value = null
      try {
        return await loadData(payload)
      } catch (e) {
        error.value = e instanceof Error ? e : new Error(String(e))
        return null
      } finally {
        loading.value = false
      }
    }
    return payload
  }

  async function open(action: DetailAction, payload?: T | string | number | null): Promise<void> {
    if (mode.value === 'page' && options.pageRoute && router) {
      // Page mode: navigate to the detail route; the route component builds its
      // own useDetail and hydrates from params/query on mount.
      const idParam = options.pageRoute.idParam ?? 'id'
      // Resolve the route id: a bare id payload is used directly; an object
      // payload is run through getId. 'create' legitimately has no id.
      const id = isId(payload)
        ? payload
        : payload != null && options.getId
          ? options.getId(payload)
          : undefined
      const params = id != null ? { [idParam]: String(id) } : {}
      await router.push({ name: options.pageRoute.name, params, query: { action } })
      return
    }
    const loaded = await loadIfNeeded(payload)
    form.open(action, loaded)
  }

  function close(): void {
    if (mode.value === 'page' && router) {
      router.back()
      return
    }
    form.close()
  }

  async function submit(): Promise<void> {
    if (!form.formData.value || !form.mode.value) return
    if (options.submitData) {
      await options.submitData(form.mode.value, form.formData.value)
    } else if (form.mode.value !== 'view') {
      // No persistence wired for an editable detail — do not fake success by
      // closing. Surfaces a misconfiguration instead of silently discarding.
      return
    }
    form.close()
  }

  function setSection(key: string): void {
    activeSection.value = key
  }

  // ── Overlay open-state ⇄ URL query key ────────────────────────────────────
  // Mirror the overlay's (action + record id) into its own query key so an
  // opened modal/drawer is deep-linkable, refresh-survivable and
  // Back-closeable — `?<key>=view:<id>` / `=edit:<id>` / `=new`. Non-invasive:
  // open()/close() stay untouched — a watcher mirrors `form` → URL
  // (any open/close path), a reconciler mirrors URL → `form` (Back/Forward/deep
  // link). Both are idempotent (each writes only when the other side differs), so
  // they can't loop. Disabled in page mode (the route IS the open-state) and when
  // `url` is off / no router (degrades to controlled, zero churn).
  const overlay = useQueryScope(
    () => (typeof options.url === 'string' ? options.url : 'detail'),
    { enabled: () => !!options.url && deepLink.detail && mode.value !== 'page' },
  )

  /** The URL code describing the CURRENTLY open overlay (null when closed). */
  function overlayCode(): string | null {
    const action = form.mode.value
    if (!form.visible.value || !action) return null
    if (action === 'create') return 'new'
    const data = form.formData.value
    const id = data != null ? recordId(data) : null
    return id != null && id.length > 0 ? `${action}:${id}` : null
  }

  function decodeOverlay(code: string): { action: FormModalMode; id: string | null } | null {
    if (code === 'new') return { action: 'create', id: null }
    const i = code.indexOf(':')
    if (i < 0) return null
    const action = code.slice(0, i)
    const id = code.slice(i + 1)
    if (action !== 'edit' && action !== 'view') return null
    return { action, id }
  }

  async function resolveRecord(id: string | null): Promise<T | null> {
    if (id == null || !loadData) return null
    try {
      return await loadData(id)
    } catch {
      return null
    }
  }

  function isBusy(): boolean {
    return busy ? busy() : loading.value
  }

  /**
   * Make `form` match the URL. `giveUp` clears a dangling deep-link when the
   * record can't be resolved AND the data source has settled (so an initial
   * deep-link, fired before the first fetch, defers via `busy()`/
   * `retryDeepLinkOn` instead of self-wiping).
   */
  function reconcileOverlay(giveUp: boolean): void {
    if (!overlay.active()) return
    const code = overlay.value.value
    if (code === overlayCode()) return
    if (code == null) {
      form.close()
      return
    }
    const parsed = decodeOverlay(code)
    if (!parsed) {
      overlay.set(null, 'replace') // garbage value → drop it
      return
    }
    if (parsed.action === 'create') {
      form.open('create', {} as T)
      return
    }
    void resolveRecord(parsed.id).then((rec) => {
      if (overlay.value.value !== code) return // superseded by a newer navigation
      if (rec) form.open(parsed.action, rec)
      else if (giveUp && !isBusy()) overlay.set(null, 'replace')
    })
  }

  if (overlay.active()) {
    // form → URL (covers EVERY open/close path: open, submit, X button).
    watch(
      [() => form.visible.value, () => form.mode.value, () => form.formData.value],
      () => {
        if (!overlay.active()) return
        const code = overlayCode()
        if (code === overlay.value.value) return
        // Opening pushes (Back closes it); closing replaces (no dead history).
        overlay.set(code, code == null ? 'replace' : 'push')
      },
    )
    // URL → form (Back / Forward / address-bar / in-session deep-link).
    watch(() => overlay.value.value, () => reconcileOverlay(true))
    // A deep-linked id may arrive only once an async data source loads.
    if (retryDeepLinkOn) {
      watch(retryDeepLinkOn, () => {
        if (overlay.value.value && overlay.value.value !== overlayCode()) reconcileOverlay(true)
      })
    }
    // Initial deep-link (`?<key>=view:<id>` on first paint). Give up on an
    // unresolvable id immediately UNLESS a deferred data source (`retryDeepLinkOn`,
    // e.g. a list that loads after paint) might still resolve it — then defer and
    // let the retry watcher decide. With only `loadData` (awaited inline), the
    // first null resolution is definitive, so the dangling deep-link is dropped.
    reconcileOverlay(!retryDeepLinkOn)
  }

  return {
    mode,
    action: computed(() => form.mode.value) as Ref<DetailAction | null>,
    visible: form.visible,
    data: form.formData,
    loading,
    error,
    activeSection,
    open,
    close,
    submit,
    setSection,
    form,
  }
}
