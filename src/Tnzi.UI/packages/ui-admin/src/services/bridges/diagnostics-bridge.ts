/**
 * Diagnostics bridge — delegates to `useAdminDiagnosticsApi`
 * (from `@tnzi/core/services/diagnostics`) so admin pages get the standard
 * dependency-injection + single-mock-seam pattern other bridges use:
 *
 * ```ts
 * const bridge = createDiagnosticsBridge({ client: useAdminClient() })
 * const summary = await bridge.exceptions.getSummary(60)
 * ```
 *
 * The diagnostics controller is read-only-ish (one DELETE for clearing the
 * in-memory exception ring buffer) and is bundled with every HostingModule
 * app, so this bridge works on any stack that loads `Tnzi.AspNetCore`.
 *
 * DTO types are re-exported below so existing page imports
 * (`import type { ModuleDiagnosticsDto } from '../../services/bridges/diagnostics-bridge'`)
 * keep resolving after the contract moved into `@tnzi/core`.
 */
import {
  useAdminDiagnosticsApi,
  type ExceptionSummaryDto,
  type ExceptionEntryDto,
  type ControllerDiagnosticsResultDto,
  type ControllerInfoDto,
  type ModuleDiagnosticsDto,
} from '@tnzi/core/services/diagnostics'
import { ensureOk, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminDiagnosticsApi>[0]

export type {
  ExceptionSummaryDto,
  ExceptionEntryDto,
  ControllerDiagnosticsResultDto,
  ControllerInfoDto,
  ModuleDiagnosticsDto,
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

  const api = useAdminDiagnosticsApi(client)

  return {
    exceptions: {
      getSummary: async (minutes = 60) =>
        unwrap<ExceptionSummaryDto>(await api.getExceptionSummary(minutes)),
      getRecent: async (count = 50) =>
        unwrap<ExceptionEntryDto[]>(await api.getRecentExceptions(count)) ?? [],
      clear: async () => {
        ensureOk(await api.clearExceptions())
      },
    },
    controllers: {
      list: async () =>
        unwrap<ControllerDiagnosticsResultDto>(await api.getControllers()),
    },
    modules: {
      list: async () =>
        unwrap<ModuleDiagnosticsDto[]>(await api.getModules()) ?? [],
    },
  }
}
