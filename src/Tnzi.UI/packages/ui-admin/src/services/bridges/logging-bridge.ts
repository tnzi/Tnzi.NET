/**
 * Logging bridge - wraps `useAdminLogFileApi` (from `@tnzi/core/services/logging`)
 * so admin pages get the same dependency-injection pattern other bridges use:
 *
 * ```ts
 * const bridge = createLoggingBridge({ client: useAdminClient() })
 * const result = await bridge.logFiles.getLevels()
 * ```
 *
 * Unlike `system-bridge` (which adapts CRUD endpoints into the
 * `BridgeCrudContract`), the log-file API is read-only and non-paged, so we
 * surface the four primitive operations directly. Keeping a thin bridge layer
 * (rather than letting pages import `useAdminLogFileApi` themselves) gives us
 * a single mock seam for unit tests and keeps page imports flat.
 */

import { useAdminLogFileApi } from '@tnzi/core/services/logging'
import type {
  LogLevelInfoDto,
  LogFileInfoDto,
  LogTailResultDto,
  LogTailQueryDto,
  LogSearchResultDto,
  LogSearchQueryDto,
} from '@tnzi/core/services/logging'

type HttpClient = Parameters<typeof useAdminLogFileApi>[0]

export interface LoggingBridgeDeps {
  client?: HttpClient
}

export interface LoggingBridge {
  logFiles: {
    getLevels(): Promise<LogLevelInfoDto[]>
    getFiles(level: string): Promise<LogFileInfoDto[]>
    tail(query: LogTailQueryDto): Promise<LogTailResultDto>
    search(query: LogSearchQueryDto): Promise<LogSearchResultDto>
  }
}

export function createLoggingBridge(deps: LoggingBridgeDeps = {}): LoggingBridge {
  const { client } = deps

  if (!client) {
    // Test-friendly fallback: every method rejects so consumers can still
    // mount the page in a unit-test environment without a real client.
    const noOp = () => Promise.reject(new Error('createLoggingBridge: no HttpClient provided'))
    return {
      logFiles: {
        getLevels: noOp as never,
        getFiles: noOp as never,
        tail: noOp as never,
        search: noOp as never,
      },
    }
  }

  const api = useAdminLogFileApi(client)

  // ApiResult-unwrap helper - every endpoint returns `{ data, code, success, message }`;
  // the page only needs `.data`, mirroring how `system-bridge` unwraps its
  // sub-contracts.
  const unwrap = async <T>(p: Promise<{ data?: T | null } | undefined | null>): Promise<T> => {
    const r = await p
    if (!r || r.data == null) {
      throw new Error('Empty response from log file API')
    }
    return r.data
  }

  return {
    logFiles: {
      getLevels: () => unwrap(api.getLevels()),
      getFiles: (level: string) => unwrap(api.getFiles(level)),
      tail: (query: LogTailQueryDto) => unwrap(api.tail(query)),
      search: (query: LogSearchQueryDto) => unwrap(api.search(query)),
    },
  }
}
