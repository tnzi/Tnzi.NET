import { describe, it, expect, vi } from 'vitest'
import { createAuditBridge } from '../../../src/services/bridges/audit-bridge'

function mockAuditApi() {
  return {
    getById: vi.fn(async () => null),
    getList: vi.fn(async () => ({
      items: [
        { id: 'op1', functionName: 'GetUser', userId: 'u1', userName: 'admin', elapsed: 42, startTime: '2026-01-01T00:00:00Z', resultType: 1, entityEntries: [], creationTime: '2026-01-01T00:00:00Z' },
        { id: 'op2', functionName: 'CreateRole', userId: 'u1', userName: 'admin', elapsed: 88, startTime: '2026-01-01T00:01:00Z', resultType: 1, entityEntries: [], creationTime: '2026-01-01T00:01:00Z' },
      ],
      totalCount: 2,
      pageIndex: 1,
      pageSize: 20,
    })),
    getUserOperations: vi.fn(async () => []),
    getFunctionStatistics: vi.fn(async () => ({})),
    getUserStatistics: vi.fn(async () => ({})),
    deleteExpired: vi.fn(async () => 0),
    getTrend: vi.fn(async () => []),
    getTopFunctions: vi.fn(async () => []),
    getTopUsers: vi.fn(async () => []),
    exportCsv: vi.fn(async () => new Blob(['Id,Url\n1,/x'], { type: 'text/csv' })),
    exportJson: vi.fn(async () => new Blob(['[]'], { type: 'application/json' })),
  }
}

describe('audit-bridge', () => {
  it('exposes logs / operations sub-contracts', () => {
    const bridge = createAuditBridge({ auditApi: mockAuditApi() as never })
    expect(typeof bridge.logs.fetch).toBe('function')
    expect(typeof bridge.operations.fetch).toBe('function')
  })

  it('logs.exportCsv/exportJson return Blobs from the api', async () => {
    const auditApi = mockAuditApi()
    const bridge = createAuditBridge({ auditApi: auditApi as never })
    const csv = await bridge.logs.exportCsv({ httpMethod: 'POST' })
    const json = await bridge.logs.exportJson()
    expect(csv).toBeInstanceOf(Blob)
    expect(json).toBeInstanceOf(Blob)
    expect(auditApi.exportCsv).toHaveBeenCalledWith({ httpMethod: 'POST' })
    expect(auditApi.exportJson).toHaveBeenCalledWith({})
  })

  it('logs.detail / operations.detail unwrap getById (entity-level change tree)', async () => {
    const auditApi = mockAuditApi()
    const full = {
      id: 'op1',
      functionName: 'UpdateUser',
      entityEntries: [
        {
          id: 'ee1',
          entityTypeName: 'User',
          operationType: 'Modified',
          propertyEntries: [{ id: 'pe1', propertyName: 'Email', originalValue: 'a@x', newValue: 'b@x' }],
        },
      ],
    }
    auditApi.getById = vi.fn(async () => ({ success: true, data: full })) as never
    const bridge = createAuditBridge({ auditApi: auditApi as never })

    const logsDetail = await bridge.logs.detail('op1')
    const opsDetail = await bridge.operations.detail('op1')

    expect(auditApi.getById).toHaveBeenCalledWith('op1')
    expect(logsDetail.entityEntries).toHaveLength(1)
    expect(logsDetail.entityEntries[0].propertyEntries[0].newValue).toBe('b@x')
    expect(opsDetail).toEqual(full)
  })

  it('logs.fetch calls auditApi.getList and returns paged items', async () => {
    const auditApi = mockAuditApi()
    const bridge = createAuditBridge({ auditApi: auditApi as never })
    const result = await bridge.logs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(auditApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('operations.fetch calls auditApi.getList and returns paged items', async () => {
    const auditApi = mockAuditApi()
    const bridge = createAuditBridge({ auditApi: auditApi as never })
    const result = await bridge.operations.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(auditApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('operations.fetch forces isWriteOperation: true (change-type operations view)', async () => {
    const auditApi = mockAuditApi()
    const bridge = createAuditBridge({ auditApi: auditApi as never })
    await bridge.operations.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(auditApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ isWriteOperation: true }),
    )
  })

  it('operations.fetch isWriteOperation cannot be overridden by page filters', async () => {
    const auditApi = mockAuditApi()
    const bridge = createAuditBridge({ auditApi: auditApi as never })
    await bridge.operations.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { isWriteOperation: false },
    })
    expect(auditApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ isWriteOperation: true }),
    )
  })

  it('logs.fetch does NOT set isWriteOperation (request-level full view)', async () => {
    const auditApi = mockAuditApi()
    const bridge = createAuditBridge({ auditApi: auditApi as never })
    await bridge.logs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    const params = (auditApi.getList.mock.calls[0] as unknown[])[0] as Record<string, unknown>
    expect('isWriteOperation' in params).toBe(false)
  })

  it('logs.create / logs.update / logs.delete are read-only stubs that reject', async () => {
    const bridge = createAuditBridge({ auditApi: mockAuditApi() as never })
    await expect(bridge.logs.create({})).rejects.toThrow()
    await expect(bridge.logs.update('id', {})).rejects.toThrow()
    await expect(bridge.logs.delete(['id'])).rejects.toThrow()
  })

  it('operations.create / operations.update / operations.delete are read-only stubs that reject', async () => {
    const bridge = createAuditBridge({ auditApi: mockAuditApi() as never })
    await expect(bridge.operations.create({})).rejects.toThrow()
    await expect(bridge.operations.update('id', {})).rejects.toThrow()
    await expect(bridge.operations.delete(['id'])).rejects.toThrow()
  })
})
