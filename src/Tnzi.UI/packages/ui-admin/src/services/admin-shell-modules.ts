/**
 * Admin shell module-availability service - fetches which framework business
 * modules the backend host has actually loaded, from
 * `GET /admin/shell/modules`.
 *
 * This is the authoritative, permission-independent signal that powers
 * `defineAdminApp`'s default module gating: a top-level module route the
 * backend never loaded gets its menu hidden and its pages made unreachable, so
 * it can't surface a dead link that 404s on click - and because the gating is
 * orthogonal to the permission system, it holds for super-admins and
 * permission-exempt paths too.
 *
 * Backed by the `Tnzi.AspNetCore.Dtos.AdminShellModulesDto` shape; see
 * `src/Tnzi.AspNetCore/Dtos/AdminShellModulesDto.cs` for the canonical schema.
 * Unlike the richer diagnostics `admin/diagnostics/admin-manifest` (gated on
 * `system.diagnostics.view`), this endpoint carries no technical detail and is
 * readable by any signed-in admin user.
 */

import type { HttpClient } from '@tnzi/core/http'

/** One loaded framework business module (raw wire shape). */
export interface AdminShellModule {
  /** Short module name, e.g. `"Identity"`. */
  name: string
  /** Whether the module is enabled (a loaded-but-disabled module reads false). */
  isEnabled: boolean
}

/** Response shape of `GET /admin/shell/modules`. */
export interface AdminShellModules {
  modules: AdminShellModule[]
}

/**
 * Normalize a module name for comparison: lowercase, dots → dashes - so
 * `"AI.Skills"`, `"ai.skills"` and `"ai-skills"` all compare equal. Matches the
 * normalization `defineAdminApp` uses for `hideModules` and the backend's short
 * names, so a route's `meta.moduleGate` lines up with the loaded-module signal.
 */
export function normalizeModuleName(name: string): string {
  return name.toLowerCase().replace(/\./g, '-')
}

/**
 * Fetch the set of ENABLED framework business modules the backend host has
 * loaded, normalized for matching against front-end top-level module route
 * names (see {@link normalizeModuleName}).
 *
 * Returns `null` instead of throwing when the endpoint is unavailable - an
 * older backend without `GET /admin/shell/modules`, or a 401/403. Callers MUST
 * treat `null` as "signal unavailable → do NOT gate" (fail-open: show every
 * module), so the sidebar is never blanked by a missing/failed signal.
 */
export async function fetchAdminShellModules(
  client: HttpClient,
): Promise<Set<string> | null> {
  try {
    const result = await client.get<AdminShellModules>('/admin/shell/modules')
    const data = result?.data
    if (!data || !Array.isArray(data.modules)) return null
    const set = new Set<string>()
    for (const m of data.modules) {
      if (m && m.isEnabled && typeof m.name === 'string' && m.name) {
        set.add(normalizeModuleName(m.name))
      }
    }
    return set
  } catch {
    return null
  }
}
