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

/** One mapped realtime hub (raw wire shape). */
export interface AdminShellHub {
  /** Logical hub name, e.g. `"settings"` / `"chat"` / `"presence"`. */
  name: string
  /** Path to connect to, PathBase included, e.g. `"/api/hubs/settings"`. */
  path: string
}

/** Realtime capability block of `GET /admin/shell/modules`. */
export interface AdminShellRealtimeWire {
  available: boolean
  hubs: AdminShellHub[]
}

/** Response shape of `GET /admin/shell/modules`. */
export interface AdminShellModules {
  modules: AdminShellModule[]
  /** Absent on backends predating the realtime signal. */
  realtime?: AdminShellRealtimeWire
}

/**
 * Which realtime hubs the backend actually mapped, keyed by logical hub name.
 *
 * `null` means the backend never told us (older backend / failed request) -
 * callers MUST treat that as "unknown → do not gate", exactly like the module
 * set, so an old backend keeps today's behaviour.
 */
export interface AdminShellRealtime {
  /** True when the host mapped at least one hub. */
  available: boolean
  /** Hub name → connect path (PathBase applied by the backend). */
  hubs: Record<string, string>
}

/** Both halves of the admin shell bootstrap signal. */
export interface AdminShellSignal {
  /** Enabled business modules, normalized; `null` = signal unavailable. */
  modules: Set<string> | null
  /** Realtime capability; `null` = backend didn't report it. */
  realtime: AdminShellRealtime | null
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
export async function fetchAdminShellSignal(client: HttpClient): Promise<AdminShellSignal> {
  try {
    const result = await client.get<AdminShellModules>('/admin/shell/modules')
    const data = result?.data
    if (!data || !Array.isArray(data.modules)) return { modules: null, realtime: null }
    const set = new Set<string>()
    for (const m of data.modules) {
      if (m && m.isEnabled && typeof m.name === 'string' && m.name) {
        set.add(normalizeModuleName(m.name))
      }
    }
    return { modules: set, realtime: parseRealtime(data.realtime) }
  } catch {
    return { modules: null, realtime: null }
  }
}

/**
 * Normalize the realtime block. Returns `null` when the backend omitted it -
 * a host predating the signal, where the only safe reading is "unknown", not
 * "no realtime": claiming the latter would silently kill live config push on
 * every backend that has not been upgraded yet.
 */
function parseRealtime(raw: AdminShellRealtimeWire | undefined): AdminShellRealtime | null {
  if (!raw || typeof raw !== 'object' || !Array.isArray(raw.hubs)) return null
  const hubs: Record<string, string> = {}
  for (const h of raw.hubs) {
    if (h && typeof h.name === 'string' && h.name && typeof h.path === 'string' && h.path) {
      hubs[h.name.toLowerCase()] = h.path
    }
  }
  return { available: raw.available === true, hubs }
}

/**
 * Modules-only convenience over {@link fetchAdminShellSignal} - kept because it
 * is part of the package's public surface.
 */
export async function fetchAdminShellModules(
  client: HttpClient,
): Promise<Set<string> | null> {
  return (await fetchAdminShellSignal(client)).modules
}
