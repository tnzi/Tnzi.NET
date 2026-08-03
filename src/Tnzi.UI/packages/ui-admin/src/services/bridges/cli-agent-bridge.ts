/**
 * External CLI agent bridge - `Tnzi.AI.Cli`.
 *
 * Deliberately a standalone bridge rather than another sub-contract on
 * `ai-bridge`: none of these three resources is a `BridgeCrudContract`
 * (runtimes are probe-discovered, bindings are keyed by agentId not by their
 * own id, runs are append-only and cancel-only). Forcing them into the CRUD
 * shape would mean four "rejects not implemented" stubs per resource.
 *
 * The module is optional AND disabled by default, so every read here treats a
 * 501/404 as "not available in this deployment" and returns an empty result -
 * an admin page must render an informative empty state, not an error toast.
 */
import type { HttpClient } from '@tnzi/core/http'
import type { PagedList } from '@tnzi/core'
import {
  useAdminCliRuntimeApi,
  useAdminCliBindingApi,
  useAdminCliRunApi,
  type CliRuntimeDto,
  type UpdateCliRuntimeDto,
  type CliAgentBindingDto,
  type UpsertCliAgentBindingDto,
  type CliRunDto,
  type CliRunMessageDto,
  type CliRunQueryDto,
  type CliProviderOptionDto,
  type CliRuntimeProbeResultDto,
} from '@tnzi/core/services/ai'

import { ensureOk, unwrapResult as unwrap } from '../_mappers'

export interface CliAgentBridge {
  runtimes: {
    list(): Promise<CliRuntimeDto[]>
    providers(): Promise<CliProviderOptionDto[]>
    probe(): Promise<CliRuntimeProbeResultDto>
    update(id: string, input: UpdateCliRuntimeDto): Promise<CliRuntimeDto>
    remove(id: string): Promise<void>
  }
  bindings: {
    /** `null` = the agent runs built-in. That is a normal answer, not an error. */
    get(agentId: string): Promise<CliAgentBindingDto | null>
    upsert(agentId: string, input: UpsertCliAgentBindingDto): Promise<CliAgentBindingDto>
    remove(agentId: string): Promise<void>
  }
  runs: {
    list(query?: CliRunQueryDto): Promise<PagedList<CliRunDto>>
    get(id: string): Promise<CliRunDto>
    messages(id: string, fromSequence?: number): Promise<CliRunMessageDto[]>
    cancel(id: string): Promise<void>
    streamUrl(id: string, fromSequence?: number): string
  }
}

export interface CliAgentBridgeDeps {
  client?: HttpClient
}

const EMPTY_PAGE: PagedList<CliRunDto> = {
  items: [],
  totalCount: 0,
  pageIndex: 1,
  pageSize: 20,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false,
}

export function createCliAgentBridge(deps: CliAgentBridgeDeps): CliAgentBridge {
  const client = deps.client
  const runtimeApi = client ? useAdminCliRuntimeApi(client) : null
  const bindingApi = client ? useAdminCliBindingApi(client) : null
  const runApi = client ? useAdminCliRunApi(client) : null

  return {
    runtimes: {
      async list() {
        if (!runtimeApi) return []
        const response = await runtimeApi.getList()
        // 501 (module not loaded / disabled) reads as "no runtimes here".
        return response?.succeeded ? (response.data ?? []) : []
      },
      async providers() {
        if (!runtimeApi) return []
        const response = await runtimeApi.getProviders()
        return response?.succeeded ? (response.data ?? []) : []
      },
      async probe() {
        if (!runtimeApi) return { runtimes: [], notFound: [] }
        return unwrap<CliRuntimeProbeResultDto>(await runtimeApi.probe())
      },
      async update(id, input) {
        if (!runtimeApi) throw new Error('AI.Cli module is not available')
        return unwrap<CliRuntimeDto>(await runtimeApi.update(id, input))
      },
      async remove(id) {
        if (!runtimeApi) throw new Error('AI.Cli module is not available')
        ensureOk(await runtimeApi.delete(id))
      },
    },

    bindings: {
      async get(agentId) {
        if (!bindingApi) return null
        const response = await bindingApi.getByAgentId(agentId)
        return response?.succeeded ? (response.data ?? null) : null
      },
      async upsert(agentId, input) {
        if (!bindingApi) throw new Error('AI.Cli module is not available')
        return unwrap<CliAgentBindingDto>(await bindingApi.upsert(agentId, input))
      },
      async remove(agentId) {
        if (!bindingApi) throw new Error('AI.Cli module is not available')
        ensureOk(await bindingApi.delete(agentId))
      },
    },

    runs: {
      async list(query) {
        if (!runApi) return EMPTY_PAGE
        const response = await runApi.getList(query)
        return response?.succeeded ? (response.data ?? EMPTY_PAGE) : EMPTY_PAGE
      },
      async get(id) {
        if (!runApi) throw new Error('AI.Cli module is not available')
        return unwrap<CliRunDto>(await runApi.getById(id))
      },
      async messages(id, fromSequence = 0) {
        if (!runApi) return []
        const response = await runApi.getMessages(id, fromSequence)
        return response?.succeeded ? (response.data ?? []) : []
      },
      async cancel(id) {
        if (!runApi) throw new Error('AI.Cli module is not available')
        ensureOk(await runApi.cancel(id))
      },
      streamUrl(id, fromSequence = 0) {
        return runApi ? runApi.streamUrl(id, fromSequence) : ''
      },
    },
  }
}

// DTOs travel with the bridge so pages import one module, not two.
export type {
  CliRuntimeDto,
  UpdateCliRuntimeDto,
  CliAgentBindingDto,
  UpsertCliAgentBindingDto,
  CliRunDto,
  CliRunMessageDto,
  CliRunQueryDto,
  CliProviderOptionDto,
  CliRuntimeProbeResultDto,
} from '@tnzi/core/services/ai'

// Re-export the enums so pages can consume runtime values through the bridge
// surface and stay clean under the `no-restricted-imports` guard that blocks
// value imports from `@tnzi/core/services/*` inside `pages/**`.
export {
  CliRunStatus,
  CliAgentEventType,
  CliRunFailureReason,
  CliRuntimeStatus,
  CliRuntimeMode,
  CliWorkDirectoryMode,
  CLI_RUN_TERMINAL_STATUSES,
} from '@tnzi/core/services/ai'
