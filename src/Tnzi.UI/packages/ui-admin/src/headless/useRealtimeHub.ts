import { computed, type ComputedRef } from 'vue'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'

/**
 * Realtime-hub availability guard - the SignalR twin of
 * {@link useModuleAvailability}, backed by the realtime half of the backend
 * shell signal (`GET /admin/shell/modules` → `useAdminRouteStore.realtime`).
 *
 * Answers "did the backend actually map hub X, and at which path?", which the
 * loaded-module list cannot answer: hubs are mapped by their owning business
 * module ONLY when `SignalRModule` is also loaded, and `SignalRModule` is a
 * framework module that the module list omits by design. A host that loaded
 * Tnzi.System without Tnzi.SignalR therefore looks module-complete while
 * `/hubs/settings` does not exist - which is how the admin shell ended up
 * opening a connection that could never succeed.
 *
 * Semantics mirror the module guard:
 * - `has` - fail-OPEN while the signal is unknown (`realtime === null`: older
 *   backend, request failed, gating disabled), so an upgrade is never required
 *   to keep realtime working.
 * - `canConnect` - side-effect gate: also false while the initial probe is in
 *   flight, so a component whose mount opens a socket defers instead of racing
 *   a signal that is about to rule it out.
 *
 * There is NO super-user bypass: a hub that was never mapped 404s for the super
 * admin too.
 */
export interface UseRealtimeHubReturn {
  /** True when the hub is mapped, or the signal is unknown (fail-open). */
  has: (hub: string) => boolean
  /** Side-effect gate: `has(hub)` AND the initial signal probe has settled. */
  canConnect: (hub: string) => boolean
  /**
   * The backend-reported connect path for `hub` (PathBase already applied), or
   * `undefined` when unknown. Prefer this over a hand-configured URL: the
   * backend knows its own deployment prefix, so a sub-application deployment
   * needs no client-side configuration at all.
   */
  path: (hub: string) => string | undefined
  /** True while the initial shell-signal probe is in flight. */
  pending: ComputedRef<boolean>
  /** True once the backend reported its realtime capability. */
  known: ComputedRef<boolean>
}

export function useRealtimeHub(): UseRealtimeHubReturn {
  const routeStore = useAdminRouteStore()

  function has(hub: string): boolean {
    const signal = routeStore.realtime
    if (signal === null) return true
    return signal.hubs[hub.toLowerCase()] !== undefined
  }

  function canConnect(hub: string): boolean {
    return !routeStore.moduleSignalPending && has(hub)
  }

  function path(hub: string): string | undefined {
    return routeStore.realtime?.hubs[hub.toLowerCase()]
  }

  const pending = computed(() => routeStore.moduleSignalPending)
  const known = computed(() => routeStore.realtime !== null)

  return { has, canConnect, path, pending, known }
}
