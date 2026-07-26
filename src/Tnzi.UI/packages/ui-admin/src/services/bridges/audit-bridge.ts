/**
 * Audit bridge - full implementation (Phase 3 Task 3.21).
 *
 * Adapts the audit backend API to BridgeCrudContract shapes used by
 * TCrudPage-based audit pages. Both sub-contracts are READ-ONLY:
 * audit data is immutable - create/update/delete always reject.
 *
 * Sub-contracts:
 *   - logs       → useAdminAuditApi.getList  (request-level audit log view: ALL audited requests)
 *   - operations → useAdminAuditApi.getList + isWriteOperation: true (change-type operations view)
 *
 * BACKEND NOTE:
 *   There is only one backend admin API factory (useAdminAuditApi) covering
 *   /admin/audit-operations. The plan's "AuditLogDto" is a deprecated alias
 *   for AuditOperationDto (see @tnzi/core/services/audit/types.ts).
 *   Both sub-contracts delegate to the same API; the split reflects different
 *   page views (log-centric vs operation-centric column sets) not different endpoints.
 *
 *   logs.exportCsv/exportJson: direct Blob downloads via client.download
 *   (POST with AuditOperationQueryDto body; backend returns UTF-8 BOM CSV /
 *   JSON file with ApiResult envelope on failure). Trigger the browser save
 *   with downloadBlob from @tnzi/core/utils.
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
    /** Full detail by id - includes entityEntries/propertyEntries (list rows do not). */
    detail(id: string): Promise<AuditOperationDto>
    /** Export filtered audit operations as a CSV Blob (UTF-8 BOM). */
    exportCsv(query?: Partial<AuditOperationQueryDto>): Promise<Blob>
    /** Export filtered audit operations as a JSON Blob. */
    exportJson(query?: Partial<AuditOperationQueryDto>): Promise<Blob>
  }
  operations: BridgeCrudContract<AuditOperationDto> & {
    /** Full detail by id - includes entityEntries/propertyEntries (list rows do not). */
    detail(id: string): Promise<AuditOperationDto>
  }
}

const readOnlyReject = (): Promise<never> =>
  Promise.reject(new Error('Audit data is read-only - create/update/delete are not permitted'))

export function createAuditBridge(deps: AuditBridgeDeps = {}): AuditBridge {
  const auditApi = deps.auditApi ?? (deps.client ? useAdminAuditApi(deps.client) : null)

  if (!auditApi) {
    const noFetch = () => Promise.reject(new Error('createAuditBridge: no deps provided'))
    return {
      logs: { fetch: noFetch as never, detail: noFetch as never, create: readOnlyReject, update: readOnlyReject, delete: readOnlyReject, exportCsv: noFetch as never, exportJson: noFetch as never },
      operations: { fetch: noFetch as never, detail: noFetch as never, create: readOnlyReject, update: readOnlyReject, delete: readOnlyReject },
    }
  }

  // Capture narrowed reference so nested async functions can use it without TS null checks
  const api = auditApi

  // Both page views query the same endpoint but with different semantics:
  //   - Logs       = request-level audit trail: EVERY audited HTTP request
  //                  (reads + writes), no extra server-side filter.
  //   - Operations = change-type operations only: forces `isWriteOperation: true`
  //                  so the backend returns just POST/PUT/PATCH/DELETE entries
  //                  (the "business operations" view).
  // `extra` is spread AFTER the mapped query so the forced semantics cannot be
  // overridden by page-level filters.
  async function fetchAudit(query: CrudPageQuery, extra?: Partial<AuditOperationQueryDto>): Promise<CrudPageResult<AuditOperationDto>> {
    const params = { ...(mapQueryToListRequest(query) as unknown as AuditOperationQueryDto), ...extra }
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

  // Detail by id - the list endpoint returns rows WITHOUT entityEntries; the
  // GET-by-id endpoint Includes the full entity/property change tree.
  const detail = async (id: string): Promise<AuditOperationDto> =>
    unwrap<AuditOperationDto>(await api.getById(id))

  const logs: AuditBridge['logs'] = {
    // Request-level full view - no implicit filter.
    fetch: (query) => fetchAudit(query),
    detail,
    create: readOnlyReject,
    update: readOnlyReject,
    delete: readOnlyReject,
    exportCsv: async (query) => unwrap<Blob>(await api.exportCsv((query ?? {}) as AuditOperationQueryDto)),
    exportJson: async (query) => unwrap<Blob>(await api.exportJson((query ?? {}) as AuditOperationQueryDto)),
  }

  const operations: AuditBridge['operations'] = {
    // Change-type operations view - write methods only.
    fetch: (query) => fetchAudit(query, { isWriteOperation: true }),
    detail,
    create: readOnlyReject,
    update: readOnlyReject,
    delete: readOnlyReject,
  }

  return { logs, operations }
}

// 0.2.72+ (B4): re-export the enums so pages can consume the runtime values
// via the bridge surface and stay clean under the `no-restricted-imports`
// guard against `@tnzi/core/services/*` value imports from `pages/**`.
export { AuditResultType, EntityChangeType } from '@tnzi/core/services/audit'
export type { AuditOperationDto, AuditEntityEntryDto, AuditPropertyEntryDto } from '@tnzi/core/services/audit'
