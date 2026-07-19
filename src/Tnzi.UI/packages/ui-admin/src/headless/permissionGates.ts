/**
 * Shared operation-permission gating for CRUD write affordances — the fail-open
 * `.create/.update/.delete` check used by both `useCrudPage` (list pages) and
 * `useChildCollection` (detail-page nested collections), so a surface's write
 * buttons hide when the code isn't held. The backend `[ApiAuthorize]` remains
 * the real enforcement; this only shapes the UI.
 */
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

/**
 * Per-action permission codes gating a surface's write affordances. An omitted
 * action stays ungated (visible whenever its data callback exists).
 */
export interface CrudActionPermissions {
  create?: string
  update?: string
  delete?: string
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
 * super admin → allowed. The backend `[ApiAuthorize]` is the real wall.
 */
export function canAction(code: string | undefined): boolean {
  if (!code) return true
  try {
    const auth = useAdminAuthStore()
    if (auth.isSuperUser || auth.userInfo === null) return true
    return auth.hasPermission(code)
  } catch {
    return true
  }
}
