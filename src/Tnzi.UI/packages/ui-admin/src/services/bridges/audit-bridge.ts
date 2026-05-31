/**
 * Audit bridge — full implementation (Phase 3 Task 3.21).
 *
 * Adapts the audit backend API to BridgeCrudContract shapes used by
 * TCrudPage-based audit pages. Both sub-contracts are READ-ONLY:
 * audit data is immutable — create/update/delete always reject.
 *
 * Sub-contracts:
 *   - logs       → useAdminAuditApi.getList  (operation log view)
 *   - operations → useAdminAuditApi.getList  (operation management view)
 *
 * BACKEND NOTE:
 *   There is only one backend admin API factory (useAdminAuditApi) covering
 *   /admin/audit-operations. The plan's "AuditLogDto" is a deprecated alias
 *   for AuditOperationDto (see @tnzi/core/services/audit/types.ts).
 *   Both sub-contracts delegate to the same API; the split reflects different
 *   page views (log-centric vs operation-centric column sets) not different endpoints.
 *
 *   logs.export: backend provides /export/csv and /export/json URL resolvers but
 *   no direct Blob-returning client method. export() returns the CSV URL as a Blob
 *   containing the URL string — callers should use window.open(url) instead.
 *   This is documented as a BACKEND GAP: no direct POST-to-blob admin client method.
 */
import {
  useAdminAuditApi,
  type AuditOperationDto,
  type AuditOperationQueryDto,
} from '@tnzi/core/services/audit'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminAuditApi>[0]

export interface AuditBridgeDeps {
  /** Production path: provide HttpClient; bridge builds API internally. */
  client?: HttpClient
  /** Test path: inject mock API directly. */
  auditApi?: ReturnType<typeof useAdminAuditApi>
}

export interface AuditBridge {
  logs: BridgeCrudContract<AuditOperationDto> & {
    /** Returns export CSV URL as string (no direct blob endpoint in backend). */
    exportCsvUrl(): string
  }
  operations: BridgeCrudContract<AuditOperationDto>
}

const readOnlyReject = (): Promise<never> =>
  Promise.reject(new Error('Audit data is read-only — create/update/delete are not permitted'))

export function createAuditBridge(deps: AuditBridgeDeps = {}): AuditBridge {
  const auditApi = deps.auditApi ?? (deps.client ? useAdminAuditApi(deps.client) : null)

  if (!auditApi) {
    const noFetch = () => Promise.reject(new Error('createAuditBridge: no deps provided'))
    return {
      logs: { fetch: noFetch as never, create: readOnlyReject, update: readOnlyReject, delete: readOnlyReject, exportCsvUrl: () => '' },
      operations: { fetch: noFetch as never, create: readOnlyReject, update: readOnlyReject, delete: readOnlyReject },
    }
  }

  // Capture narrowed reference so nested async functions can use it without TS null checks
  const api = auditApi

  async function fetchOperations(query: CrudPageQuery): Promise<CrudPageResult<AuditOperationDto>> {
    const params = mapQueryToListRequest(query) as unknown as AuditOperationQueryDto
    const result = unwrap<{ items: AuditOperationDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await api.getList(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const logs: AuditBridge['logs'] = {
    fetch: fetchOperations,
    create: readOnlyReject,
    update: readOnlyReject,
    delete: readOnlyReject,
    exportCsvUrl: () => api.getExportCsvUrl(),
  }

  const operations: AuditBridge['operations'] = {
    fetch: fetchOperations,
    create: readOnlyReject,
    update: readOnlyReject,
    delete: readOnlyReject,
  }

  return { logs, operations }
}

// 0.2.72+ (B4): re-export the enum so pages can consume the runtime value
// via the bridge surface and stay clean under the `no-restricted-imports`
// guard against `@tnzi/core/services/*` value imports from `pages/**`.
export { AuditResultType } from '@tnzi/core/services/audit'
export type { AuditOperationDto } from '@tnzi/core/services/audit'
