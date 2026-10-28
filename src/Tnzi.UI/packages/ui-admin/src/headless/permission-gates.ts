/**
 * Shared operation-permission gating for CRUD write affordances - the fail-open
 * `.create/.update/.delete` check used by both `useCrudPage` (list pages) and
 * `useChildCollection` (detail-page nested collections), so a surface's write
 * buttons hide when the code isn't held. The backend `[ApiAuthorize]` remains
 * the real enforcement; this only shapes the UI.
 */
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

/**
 * Per-action permission codes gating a surface's write affordances. An omitted
 * action stays ungated (visible whenever its data callback exists). An action
 * may name several codes: the user needs all of them, mirroring stacked backend
 * `[ApiAuthorize]` gates, which are AND-ed.
 */
export interface CrudActionPermissions {
  create?: string | readonly string[]
  update?: string | readonly string[]
  delete?: string | readonly string[]
}

/** Expand a string prefix into the three write-action codes (`x` → `x.create` / `x.update` / `x.delete`). */
export function normalizeCrudPermission(
  permission?: string | CrudActionPermissions,
): CrudActionPermissions {
  if (!permission) return {}
  if (typeof permission === 'string') {
    return {
      create: `${permission}.create`,
      update: `${permission}.update`,
      delete: `${permission}.delete`,
    }
  }
  return permission
}

/**
 * Reactive action-permission check with the sidebar's fail-open semantics: no
 * code declared / no active store (bare test mounts) / user not loaded yet /
 * super admin whose bypass covers every code → allowed. Several codes → all of
 * them are required. The backend `[ApiAuthorize]` is the real wall.
 */
export function canAction(code: string | readonly string[] | undefined): boolean {
  if (!code || code.length === 0) return true
  try {
    const auth = useAdminAuthStore()
    if (auth.bypassesPermissionChecks || auth.userInfo === null) return true
    return typeof code === 'string' ? auth.hasPermission(code) : code.every((c) => auth.hasPermission(c))
  } catch {
    return true
  }
}
