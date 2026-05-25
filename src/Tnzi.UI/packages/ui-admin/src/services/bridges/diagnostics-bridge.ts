/**
 * Diagnostics bridge — wraps `/admin/diagnostics/*` endpoints exposed by
 * `Tnzi.AspNetCore.Controllers.DefaultDiagnosticsAdminController`.
 *
 * The diagnostics controller is read-only-ish (one DELETE for clearing the
 * in-memory exception ring buffer) and is bundled with every HostingModule
 * app, so this bridge works on any stack that loads `Tnzi.AspNetCore`.
 *
 * @tnzi/core has no generated `useAdminDiagnosticsApi` factory yet, so this
 * bridge calls the HttpClient directly. When contracts:sync regenerates the
 * factory, swap the `client.get(...)` calls for `api.getXxx()` and the
 * Diagnostics.vue consumer stays untouched.
 */
import type { HttpClient } from '@tnzi/core/http'

/** Mirror of Tnzi.AspNetCore.Dtos.ExceptionSummaryDto. */
export interface ExceptionSummaryDto {
  totalCount: number
  uniqueExceptionCount: number
  windowMinutes: number
  topExceptions?: Array<{ exceptionType: string; count: number; lastSeenUtc?: string | null }>
  byController?: Array<{ controller: string; count: number }>
}

/** Mirror of Tnzi.AspNetCore.Dtos.ExceptionEntryDto. */
export interface ExceptionEntryDto {
  exceptionType: string
  message: string
  stackTrace?: string | null
  path?: string | null
  method?: string | null
  controller?: string | null
  action?: string | null
  userId?: string | null
  occurredAtUtc: string
  durationMs?: number | null
}

/** Mirror of Tnzi.AspNetCore.Dtos.ControllerDiagnosticsResultDto. */
export interface ControllerDiagnosticsResultDto {
  totalCount: number
  controllers: ControllerInfoDto[]
}

export interface ControllerInfoDto {
  type: string
  route: string
  module: string
  isDefault: boolean
  methods: string[]
}

/** Mirror of Tnzi.AspNetCore.Dtos.ModuleDiagnosticsDto. */
export interface ModuleDiagnosticsDto {
  type: string
  assembly: string
  isEnabled: boolean
  initializationState: string
  dependencyCount: number
  manifest: {
    serviceCount: number
    controllers: string[]
    events: string[]
    backgroundTasks: string[]
    options: string[]
  }
}

export interface DiagnosticsBridgeDeps {
  client?: HttpClient
}

export interface DiagnosticsBridge {
  exceptions: {
    getSummary(minutes?: number): Promise<ExceptionSummaryDto>
    getRecent(count?: number): Promise<ExceptionEntryDto[]>
    clear(): Promise<void>
  }
  controllers: {
    list(): Promise<ControllerDiagnosticsResultDto>
  }
  modules: {
    list(): Promise<ModuleDiagnosticsDto[]>
  }
}

function unwrap<T>(res: T | { data?: T | null; succeeded?: boolean }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createDiagnosticsBridge(deps: DiagnosticsBridgeDeps = {}): DiagnosticsBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createDiagnosticsBridge: no HttpClient provided'))
    return {
      exceptions: {
        getSummary: noOp as never,
        getRecent: noOp as never,
        clear: noOp as never,
      },
      controllers: { list: noOp as never },
      modules: { list: noOp as never },
    }
  }

  return {
    exceptions: {
      getSummary: async (minutes = 60) =>
        unwrap<ExceptionSummaryDto>(
          await client.get<ExceptionSummaryDto>(`/admin/diagnostics/exceptions/summary?minutes=${minutes}`),
        ),
      getRecent: async (count = 50) =>
        unwrap<ExceptionEntryDto[]>(
          await client.get<ExceptionEntryDto[]>(`/admin/diagnostics/exceptions/recent?count=${count}`),
        ) ?? [],
      clear: async () => {
        await client.delete('/admin/diagnostics/exceptions')
      },
    },
    controllers: {
      list: async () =>
        unwrap<ControllerDiagnosticsResultDto>(
          await client.get<ControllerDiagnosticsResultDto>('/admin/diagnostics/controllers'),
        ),
    },
    modules: {
      list: async () =>
        unwrap<ModuleDiagnosticsDto[]>(
          await client.get<ModuleDiagnosticsDto[]>('/admin/diagnostics/modules'),
        ) ?? [],
    },
  }
}
