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
  useAdminDataDestructionApi,
  useAdminRecordAccessApi,
  type AuditOperationDto,
  type AuditOperationQueryDto,
  type DataDestructionDto,
  type DataDestructionQueryDto,
  type DataDestructionRunDto,
  type RecordAccessDto,
  type RecordAccessQueryDto,
  type RecordAccessUserStatDto,
} from '@tnzi/core/services/audit'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminAuditApi>[0]

export interface AuditBridgeDeps {
  /** Production path: provide HttpClient; bridge builds API internally. */
  client?: HttpClient
  /** Test path: inject mock API directly. */
  auditApi?: ReturnType<typeof useAdminAuditApi>
  /** Test path: inject the record-access API directly. */
  recordAccessApi?: ReturnType<typeof useAdminRecordAccessApi>
  /** Test path: inject the destruction API directly. */
  destructionApi?: ReturnType<typeof useAdminDataDestructionApi>
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
  /**
   * Record-level read trail (optional backend capability, off by default).
   *
   * Returns empty pages rather than errors when the capability is disabled, so
   * the page renders an ordinary "no data" state instead of a scary failure.
   */
  recordAccess: BridgeCrudContract<RecordAccessDto> & {
    /** Read volume per user - the retrospective counterpart to the per-hour quota. */
    userStatistics(
      startTime?: string,
      endTime?: string,
      topN?: number,
    ): Promise<RecordAccessUserStatDto[]>
    /** Verify one user's hash chain; omit userId for the anonymous chain. */
    verify(userId?: string): Promise<void>
  }
  /** Destruction certificates (optional backend capability, off by default). */
  destruction: BridgeCrudContract<DataDestructionDto> & {
    /** Verify the global certificate chain. */
    verify(): Promise<void>
    /** Trigger one destruction cycle. Needs audit.destruction.execute. */
    run(): Promise<DataDestructionRunDto>
  }
}

const readOnlyReject = (): Promise<never> =>
  Promise.reject(new Error('Audit data is read-only - create/update/delete are not permitted'))

/**
 * Each sub-contract stands or falls on its own api: a test that injects only
 * `auditApi` is exercising the logs/operations pair and should not be forced to
 * hand-roll mocks for the two optional capabilities it never touches.
 */
const noFetch = () => Promise.reject(new Error('createAuditBridge: no deps provided'))

const unavailable = <T>(): T =>
  ({ fetch: noFetch, create: readOnlyReject, update: readOnlyReject, delete: readOnlyReject }) as T

export function createAuditBridge(deps: AuditBridgeDeps = {}): AuditBridge {
  const auditApi = deps.auditApi ?? (deps.client ? useAdminAuditApi(deps.client) : null)
  const recordAccessApi =
    deps.recordAccessApi ?? (deps.client ? useAdminRecordAccessApi(deps.client) : null)
  const destructionApi =
    deps.destructionApi ?? (deps.client ? useAdminDataDestructionApi(deps.client) : null)

  if (!auditApi) {
    return {
      logs: { ...unavailable<AuditBridge['logs']>(), detail: noFetch as never, exportCsv: noFetch as never, exportJson: noFetch as never },
      operations: { ...unavailable<AuditBridge['operations']>(), detail: noFetch as never },
      recordAccess: buildRecordAccess(recordAccessApi),
      destruction: buildDestruction(destructionApi),
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

  return {
    logs,
    operations,
    recordAccess: buildRecordAccess(recordAccessApi),
    destruction: buildDestruction(destructionApi),
  }

}

/**
 * Record-level read trail sub-contract.
 *
 * Lives at module level rather than nested in the factory: it depends only on
 * its own api plus module-level helpers, and nesting made the closure harder to
 * read than the thing it was closing over.
 */
function buildRecordAccess(
  recordAccessApi: ReturnType<typeof useAdminRecordAccessApi> | null,
): AuditBridge['recordAccess'] {
  if (!recordAccessApi) {
    return { ...unavailable<AuditBridge['recordAccess']>(), userStatistics: noFetch as never, verify: noFetch as never }
  }

  return {
    fetch: async (query) => {
      const params = {
        ...(mapQueryToListRequest(query) as unknown as RecordAccessQueryDto),
        ...(query.filters ?? {}),
      } as RecordAccessQueryDto
      const result = unwrap<{ items: RecordAccessDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await recordAccessApi.getList(params),
      )
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    create: readOnlyReject,
    update: readOnlyReject,
    delete: readOnlyReject,
    userStatistics: async (startTime, endTime, topN) => {
      const result = await recordAccessApi.getUserStatistics(startTime, endTime, topN)
      ensureOk(result)
      return unwrap<RecordAccessUserStatDto[]>(result) ?? []
    },
    // ★ ensureOk, not unwrap: the HttpClient returns the failed envelope rather
    // than rejecting, and `unwrapResult` hands back its (null) data without
    // complaint - so a broken chain would surface as "chain intact". A security
    // check that reports a false all-clear is worse than no check at all.
    verify: async (userId) => {
      ensureOk(await recordAccessApi.verify(userId))
    },
  }
}

/** Destruction-certificate sub-contract. */
function buildDestruction(
  destructionApi: ReturnType<typeof useAdminDataDestructionApi> | null,
): AuditBridge['destruction'] {
  if (!destructionApi) {
    return { ...unavailable<AuditBridge['destruction']>(), verify: noFetch as never, run: noFetch as never }
  }

  return {
    fetch: async (query) => {
      const params = {
        ...(mapQueryToListRequest(query) as unknown as DataDestructionQueryDto),
        ...(query.filters ?? {}),
      } as DataDestructionQueryDto
      const result = unwrap<{ items: DataDestructionDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await destructionApi.getList(params),
      )
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    create: readOnlyReject,
    update: readOnlyReject,
    delete: readOnlyReject,
    // Same reasoning as recordAccess.verify: a broken certificate chain must
    // not read as "chain intact".
    verify: async () => {
      ensureOk(await destructionApi.verify())
    },
    run: async () => {
      const result = await destructionApi.run()
      // Without this the page would read `null.totalDestroyed` on failure and
      // report a TypeError instead of the backend's reason.
      ensureOk(result)
      return unwrap<DataDestructionRunDto>(result)
    },
  }
}

// 0.2.72+ (B4): re-export the enums so pages can consume the runtime values
// via the bridge surface and stay clean under the `no-restricted-imports`
// guard against `@tnzi/core/services/*` value imports from `pages/**`.
export { AuditResultType, EntityChangeType } from '@tnzi/core/services/audit'
export type {
  AuditOperationDto,
  AuditEntityEntryDto,
  AuditPropertyEntryDto,
  DataDestructionDto,
  DataDestructionPolicyResultDto,
  DataDestructionRunDto,
  RecordAccessDto,
  RecordAccessUserStatDto,
} from '@tnzi/core/services/audit'
