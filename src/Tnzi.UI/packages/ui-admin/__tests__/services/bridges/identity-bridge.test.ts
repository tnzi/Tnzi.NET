import { describe, it, expect, vi } from 'vitest'
import { createIdentityBridge } from '../../../src/services/bridges/identity-bridge'

function mockUserApi() {
  return {
    getList: vi.fn(async () => ({
      items: [{ id: '1', userName: 'alice', email: 'a@a' }],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    create: vi.fn(async (data: unknown) => ({ ...(data as object), id: 'new' })),
    update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
    deleteMany: vi.fn(async () => undefined),
    exportCsv: vi.fn(async () => 'id,userName\n1,alice'),
    importCsv: vi.fn(async () => ({ successCount: 1, failureCount: 0, errors: [] })),
  } as any
}

function mockRoleApi() {
  return {
    getPagedList: vi.fn(async () => ({
      items: [],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    create: vi.fn(async () => ({ id: 'r1' })),
    update: vi.fn(async () => ({})),
    deleteMany: vi.fn(async () => undefined),
  } as any
}

function mockTenantApi() {
  return {
    getPagedList: vi.fn(async () => ({
      items: [],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    create: vi.fn(async () => ({ id: 't1' })),
    update: vi.fn(async () => ({})),
    delete: vi.fn(async () => undefined),
  } as any
}

function mockLoginLogApi() {
  return {
    getList: vi.fn(async () => ({
      items: [],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
  } as any
}

function makeBridge(overrides: Record<string, unknown> = {}) {
  return createIdentityBridge({
    userApi: mockUserApi(),
    roleApi: mockRoleApi(),
    tenantApi: mockTenantApi(),
    loginLogApi: mockLoginLogApi(),
    ...overrides,
  })
}

describe('identity-bridge', () => {
  it('users.fetch calls userApi.getList with mapped query', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    const result = await bridge.users.fetch({
      pageIndex: 2,
      pageSize: 10,
      searchText: 'alice',
      sortField: 'name',
      sortOrder: 'asc',
      filters: { active: true },
    })
    expect(userApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({
        pageIndex: 2,
        pageSize: 10,
        keyword: 'alice',
        sortBy: 'name',
        sortOrder: 'asc',
        active: true,
      }),
    )
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('users.create delegates to userApi.create', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    const result = await bridge.users.create({ userName: 'Bob' } as any)
    expect(userApi.create).toHaveBeenCalledWith({ userName: 'Bob' })
    expect((result as any).id).toBe('new')
  })

  it('users.update delegates to userApi.update', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    await bridge.users.update('7', { userName: 'renamed' } as any)
    expect(userApi.update).toHaveBeenCalledWith('7', { userName: 'renamed' })
  })

  it('users.delete delegates to userApi.deleteMany', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    await bridge.users.delete(['1', '2'])
    expect(userApi.deleteMany).toHaveBeenCalledWith(['1', '2'])
  })

  it('users.export wraps CSV in Blob; users.import calls importCsv', async () => {
    const userApi = mockUserApi()
    const bridge = createIdentityBridge({
      userApi,
      roleApi: mockRoleApi(),
      tenantApi: mockTenantApi(),
      loginLogApi: mockLoginLogApi(),
    })
    const blob = await bridge.users.export!({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: {},
    })
    expect(blob).toBeInstanceOf(Blob)
    await bridge.users.import!(new File(['x'], 'x.csv'))
    expect(userApi.importCsv).toHaveBeenCalled()
  })

  it('exposes roles sub-contract with fetch/create/update/delete', async () => {
    const bridge = makeBridge()
    expect(typeof bridge.roles.fetch).toBe('function')
    expect(typeof bridge.roles.create).toBe('function')
    expect(typeof bridge.roles.update).toBe('function')
    expect(typeof bridge.roles.delete).toBe('function')
  })
})
