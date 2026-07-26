/**
 * Admin manifest service - fetches the backend-driven module manifest from
 * <c>GET admin/diagnostics/admin-manifest</c>. Shipped in `@tnzi/ui-admin` 0.2.4
 * and consumed by `useAdminModuleManifest()` to auto-build the admin menu.
 *
 * Backed by the `Tnzi.AspNetCore.Dtos.AdminManifestDto` shape; see
 * `src/Tnzi.AspNetCore/Dtos/AdminManifestDto.cs` for the canonical schema.
 */

import type { HttpClient } from '@tnzi/core/http'

export interface AdminManifestEntity {
  /** Entity name extracted from the route, e.g. `"users"` or `"identity/users"`. */
  name: string
  /** Full route template, e.g. `"admin/users"`. */
  route: string
  /** Distinct HTTP methods exposed for this route, uppercased. */
  methods: string[]
  /** True when all four basic CRUD verbs are present. */
  hasFullCrud: boolean
  /** True when the underlying controller has `[DefaultController]` (consumer can override). */
  isDefault: boolean
  /** Controller type name (fully qualified). */
  controllerType: string
}

export interface AdminManifestModule {
  /** Short module name, e.g. `"Identity"`. */
  name: string
  /** Fully-qualified module type name, e.g. `"Tnzi.Identity"`. */
  fullName: string
  /** Assembly name. */
  assembly: string
  /** Whether the module is enabled. */
  isEnabled: boolean
  /** Admin entities exposed by this module. */
  entities: AdminManifestEntity[]
}

export interface AdminManifest {
  modules: AdminManifestModule[]
}

/**
 * Fetch the admin manifest from the backend.
 *
 * Returns `null` instead of throwing when the endpoint is unavailable
 * (e.g. older backend without the 0.2.4-era enhancement, or a 401/403).
 * Callers should treat null as "fall back to static menu".
 */
export async function fetchAdminManifest(
  client: HttpClient,
): Promise<AdminManifest | null> {
  try {
    const result = await client.get<AdminManifest>('/admin/diagnostics/admin-manifest')
    const data = result?.data
    if (!data || !Array.isArray(data.modules)) return null
    return data
  } catch {
    return null
  }
}
