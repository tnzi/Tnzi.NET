/**
 * Sandbox bridge — wraps `/admin/sandbox/status` exposed by
 * `Tnzi.AI.Sandbox.Controllers.Admin.DefaultSandboxAdminController`.
 *
 * The sandbox module currently exposes a single read-only status endpoint.
 * More endpoints (session audit, execution history) are on the roadmap; the
 * bridge is structured so adding them is just appending new methods.
 */
import type { HttpClient } from '@tnzi/core/http'

export interface SandboxStatusDto {
  enabled: boolean
  provider: string
  dataRoot: string
  deniedCommands: string[]
  deniedPatterns: string[]
  environmentBlacklist: string[]
}

export interface SandboxBridgeDeps {
  client?: HttpClient
}

export interface SandboxBridge {
  getStatus(): Promise<SandboxStatusDto | null>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createSandboxBridge(deps: SandboxBridgeDeps = {}): SandboxBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createSandboxBridge: no HttpClient provided'))
    return { getStatus: noOp as never }
  }

  return {
    getStatus: async () =>
      unwrap<SandboxStatusDto | null>(
        await client.get<SandboxStatusDto>('/admin/sandbox/status'),
      ),
  }
}
