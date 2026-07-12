import { computed, type ComputedRef } from 'vue'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'
import { normalizeModuleName } from '../services/admin-shell-modules'

/**
 * Module-availability guard composable — the module twin of
 * `usePermissionGuard`, backed by the backend loaded-module signal
 * (`GET /admin/shell/modules` → `useAdminRouteStore.availableModules`).
 *
 * Answers "did the backend host actually load framework module X?" so UI
 * that surfaces an optional module (chat launcher, dashboard widgets,
 * settings sections, custom buttons) can hide itself instead of firing
 * requests that can only 404/500 on a host that never loaded the module.
 *
 * Two families of checks with DIFFERENT in-flight semantics:
 *
 * - `has` / `hasAny` / `hasAll` — pure-visibility checks. FAIL-OPEN while the
 *   signal is unavailable (`availableModules === null`): old backend, probe
 *   still in flight, or `moduleGating` disabled. Mirrors the sidebar menu
 *   filter, so a missing signal never blanks UI. Use for `v-if` on buttons,
 *   links, sections.
 * - `canActivate` — side-effect gate. ALSO false while the initial probe is
 *   still in flight (`moduleSignalPending`), so components whose mount fires
 *   requests / opens sockets (chat host, data widgets, pollers) defer until
 *   the signal settles instead of racing it. Once settled it degrades to
 *   `has` (fail-open on old backends).
 *
 * UNLIKE the permission guard there is NO super-user bypass: module
 * availability is a fact about the backend process, orthogonal to who is
 * signed in — an endpoint of an unloaded module 404s for the super admin too.
 *
 * Module names accept any of the forms used across the stack (`"AI.Skills"`,
 * `"ai.skills"`, `"ai-skills"`) — normalized via {@link normalizeModuleName}.
 */
export interface UseModuleAvailabilityReturn {
  /** True when the module is loaded, or the signal is unavailable (fail-open). */
  has: (module: string) => boolean
  /** True when ANY of the modules is loaded (fail-open on missing signal). */
  hasAny: (modules: string[]) => boolean
  /** True when ALL of the modules are loaded (fail-open on missing signal). */
  hasAll: (modules: string[]) => boolean
  /**
   * Side-effect gate: `has(module)` AND the initial availability probe is not
   * in flight. Mount sockets / auto-fetchers behind this instead of `has`.
   */
  canActivate: (module: string) => boolean
  /** True while the initial availability probe is in flight. */
  pending: ComputedRef<boolean>
  /** True once the signal is known (a Set arrived — even an empty one). */
  known: ComputedRef<boolean>
  /**
   * The raw normalized loaded-module set (`null` = signal unavailable).
   * Exposed for watchers that must react to signal arrival/refresh (e.g.
   * widgets re-running a fetch once the signal settles).
   */
  modules: ComputedRef<Set<string> | null>
}

export function useModuleAvailability(): UseModuleAvailabilityReturn {
  const routeStore = useAdminRouteStore()

  function has(module: string): boolean {
    const available = routeStore.availableModules
    if (available === null) return true
    return available.has(normalizeModuleName(module))
  }

  function hasAny(modules: string[]): boolean {
    if (routeStore.availableModules === null) return true
    return modules.some(has)
  }

  function hasAll(modules: string[]): boolean {
    return modules.every(has)
  }

  function canActivate(module: string): boolean {
    return !routeStore.moduleSignalPending && has(module)
  }

  const pending = computed(() => routeStore.moduleSignalPending)
  const known = computed(() => routeStore.availableModules !== null)
  const modules = computed(() => routeStore.availableModules)

  return { has, hasAny, hasAll, canActivate, pending, known, modules }
}
