/**
 * Sandbox admin API wrappers - `/admin/sandbox/status` exposed by
 * `Tnzi.AI.Sandbox.Controllers.Admin.DefaultSandboxAdminController`.
 *
 * The sandbox module currently exposes a single read-only status endpoint
 * (active provider, data root, and the denylists the active provider applies
 * to every sandbox-bound tool call). `Tnzi.AI.Sandbox` is an optional sub-module of
 * `Tnzi.AI`; the backend returns 404 when not loaded.
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** Sandbox module status + the denylists the active provider enforces. */
export interface SandboxStatusDto {
  enabled: boolean;
  provider: string;
  dataRoot: string;
  /** Matched case-insensitively as substrings of the whole command. */
  deniedCommands: string[];
  /** Executable names refused at the start of any pipeline segment (token-level). */
  deniedCommandPrefixes: string[];
  /** File-name patterns hidden from `ls` and refused by `read_file`. */
  deniedPatterns: string[];
  /** Local provider only; empty for providers that do not inherit the host environment. */
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
