/**
 * Sandbox bridge — wraps `/admin/sandbox/status` exposed by
 * `Tnzi.AI.Sandbox.Controllers.Admin.DefaultSandboxAdminController`.
 *
 * The sandbox module currently exposes a single read-only status endpoint.
 * More endpoints (session audit, execution history) are on the roadmap; the
 * bridge is structured so adding them is just appending new methods.
 *
 * The DTO + the raw API factory live in `@tnzi/core/services/ai` (`sandbox.ts`).
 * This bridge delegates to `useSandboxAdminApi(client)` and adds the tolerant
 * `unwrap` shaping the page expects.
 */
import type { HttpClient } from '@tnzi/core/http'
import { useSandboxAdminApi } from '@tnzi/core/services/ai'
import { unwrapResult as unwrap } from '../_mappers'

export type { SandboxStatusDto } from '@tnzi/core/services/ai'

import type { SandboxStatusDto } from '@tnzi/core/services/ai'

export interface SandboxBridgeDeps {
  client?: HttpClient
}

export interface SandboxBridge {
  getStatus(): Promise<SandboxStatusDto | null>
}

export function createSandboxBridge(deps: SandboxBridgeDeps = {}): SandboxBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createSandboxBridge: no HttpClient provided'))
    return { getStatus: noOp as never }
  }

  const api = useSandboxAdminApi(client)

  return {
    getStatus: async () =>
      unwrap<SandboxStatusDto | null>(await api.getStatus()),
  }
}
