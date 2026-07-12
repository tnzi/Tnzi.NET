/**
 * Global admin theme controller.
 *
 * Owns the client side of the "super admin themes everyone" mechanism:
 *  - `load()`   - fetch the global snapshot (`GET /appearance/admin-theme`,
 *                 anonymous / any user) and apply it to the theme store +
 *                 `@tnzi/ui` theme context, then re-overlay the user's own
 *                 allowed choices (preset color; the dark-mode choice is
 *                 handled inside `applyThemeSnapshot`).
 *  - `save()`   - serialize the CURRENT store/context state and persist it
 *                 as the global snapshot (privileged users only; the backend
 *                 enforces system.appearance.update).
 *  - `reset()`  - clear the global snapshot server-side.
 *  - `isDirty`  - whether the current local state differs from the last
 *                 loaded/saved snapshot (drives the drawer's unsaved badge).
 *
 * Fail-safe by design: no client, backend without the appearance endpoints,
 * network errors - everything degrades to the legacy local-only behavior.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { deepEqual } from '@tnzi/core'
import type { ThemeContext } from '@tnzi/ui'
import { isValidSnapshot, type AdminThemeSnapshot } from '../theme/admin-config'
import { applyThemeSnapshot, buildThemeSnapshot, overlayUserPreset } from '../theme/snapshot'
import { useAdminThemeStore } from '../stores/useAdminThemeStore'
import { createSystemBridge, type SystemBridge } from '../services/bridges/system-bridge'

type HttpClientLike = NonNullable<Parameters<typeof createSystemBridge>[0]>['client']

export interface UseGlobalThemeOptions {
  client?: HttpClientLike | null
  themeContext?: ThemeContext
  /** Master switch (defineAdminApp({ theme: { globalSync: false } })). Default true. */
  enabled?: boolean
  /**
   * Whether the user's personal preset color should overlay the global
   * colors after a snapshot apply. The shell passes "is the drawer in
   * presets mode" here: PRIVILEGED users edit the global theme directly, so
   * a lingering personal preset must NOT overlay - otherwise `save()` would
   * capture that personal color and publish it to every user, and the
   * unsaved badge would show phantom changes. Default: overlay (the
   * non-privileged / standalone behavior).
   */
  shouldOverlayUserPreset?: () => boolean
  /** Test seam. */
  bridge?: Pick<SystemBridge, 'appearance'>
}

export interface GlobalThemeController {
  /** Whether global sync participates at all (config + client + context present). */
  readonly enabled: boolean
  /** Last snapshot loaded from / saved to the server (null = unset). */
  remote: Ref<AdminThemeSnapshot | null>
  /** True once the initial load settled (success, unset or failure). */
  loaded: Ref<boolean>
  saving: Ref<boolean>
  /**
   * Current local state differs from `remote`. While nothing has ever been
   * saved (`remote` null after a settled load), everything counts as
   * unsaved - the badge is the only signal during first-time configuration.
   */
  isDirty: ComputedRef<boolean>
  load(): Promise<void>
  /** Re-apply `remote` + the user's own preset-color overlay. */
  applyRemote(): void
  /** Persist the current state as the global snapshot. Resolves false on failure. */
  save(): Promise<boolean>
  /**
   * Clear the global snapshot server-side. Resolves false on failure.
   * NOTE: other clients keep their locally CACHED theme until a new
   * snapshot is saved - a bare clear never reaches them. The drawer's
   * reset therefore saves the factory snapshot instead (see
   * TThemeDrawer.resetAll); this method exists for API parity with the
   * backend DELETE endpoint.
   */
  reset(): Promise<boolean>
}

/** Strip volatile envelope fields before comparing snapshots. */
function comparable(snapshot: AdminThemeSnapshot): Pick<AdminThemeSnapshot, 'admin' | 'ui'> {
  return { admin: snapshot.admin, ui: snapshot.ui }
}

export function useGlobalTheme(options: UseGlobalThemeOptions = {}): GlobalThemeController {
  const themeStore = useAdminThemeStore()
  const ctx = options.themeContext
  const enabled = options.enabled !== false && !!ctx && (!!options.client || !!options.bridge)
  const bridge = options.bridge ?? createSystemBridge({ client: options.client ?? undefined })

  const remote = ref<AdminThemeSnapshot | null>(null)
  const loaded = ref(false)
  const saving = ref(false)

  const isDirty = computed(() => {
    if (!enabled || !ctx) return false
    if (!remote.value) return loaded.value
    return !deepEqual(comparable(buildThemeSnapshot(themeStore, ctx)), comparable(remote.value))
  })

  function applyRemote(): void {
    if (!ctx || !remote.value) return
    // Idempotence guard - the common reload case re-fetches a snapshot
    // identical to the locally cached one; skip the ~50 store setters (and
    // their CSS-var / DOM-filter watchers) when nothing would change.
    if (deepEqual(comparable(buildThemeSnapshot(themeStore, ctx)), comparable(remote.value))) return
    applyThemeSnapshot(remote.value, themeStore, ctx, { modeAsDefault: true })
    if (options.shouldOverlayUserPreset?.() !== false) {
      overlayUserPreset(themeStore, ctx)
    }
  }

  async function load(): Promise<void> {
    if (!enabled || !ctx) {
      loaded.value = true
      return
    }
    try {
      // `GET /appearance/admin-theme` is ANONYMOUS (deployment-level public
      // appearance), so there is no token to wait for — firing immediately lets
      // the login page and the top-level exception pages (403/404/500, rendered
      // pre-auth outside the shell) pick up the global theme too, instead of
      // snapping back to the built-in palette on refresh.
      const dto = await bridge.appearance.getGlobal()
      const theme = dto?.theme
      if (theme && isValidSnapshot(theme)) {
        remote.value = theme
        applyRemote()
      } else {
        remote.value = null
      }
    } catch {
      // Older backend without the endpoint / network failure - keep the
      // locally persisted (cached) theme untouched.
    } finally {
      loaded.value = true
    }
  }

  async function save(): Promise<boolean> {
    if (!enabled || !ctx) return false
    saving.value = true
    try {
      const snapshot = buildThemeSnapshot(themeStore, ctx)
      await bridge.appearance.saveGlobal(snapshot as unknown as Record<string, unknown>)
      remote.value = snapshot
      return true
    } catch {
      return false
    } finally {
      saving.value = false
    }
  }

  async function reset(): Promise<boolean> {
    if (!enabled) return false
    saving.value = true
    try {
      await bridge.appearance.resetGlobal()
      remote.value = null
      return true
    } catch {
      return false
    } finally {
      saving.value = false
    }
  }

  return { enabled, remote, loaded, saving, isDirty, load, applyRemote, save, reset }
}
