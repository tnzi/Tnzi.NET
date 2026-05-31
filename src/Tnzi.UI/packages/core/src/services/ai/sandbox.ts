/**
 * Sandbox admin API wrappers — `/admin/sandbox/status` exposed by
 * `Tnzi.AI.Sandbox.Controllers.Admin.DefaultSandboxAdminController`.
 *
 * The sandbox module currently exposes a single read-only status endpoint
 * (active provider, data root, and the three denylists applied to every
 * sandbox-bound tool call). `Tnzi.AI.Sandbox` is an optional sub-module of
 * `Tnzi.AI`; the backend returns 404 when not loaded.
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** Sandbox module status + the three denylists the provider enforces. */
export interface SandboxStatusDto {
  enabled: boolean;
  provider: string;
  dataRoot: string;
  deniedCommands: string[];
  deniedPatterns: string[];
  environmentBlacklist: string[];
}

// ---------------------------------------------------------------------------
// Admin: Sandbox API
// Route: /admin/sandbox
// ---------------------------------------------------------------------------

/** Admin sandbox status API */
export function useSandboxAdminApi(client: HttpClient) {
  return {
    /** Get the sandbox module status (provider, data root, denylists) */
    getStatus: () =>
      client.get<SandboxStatusDto>('/admin/sandbox/status'),
  };
}
