import { describe, it, expect, vi } from 'vitest'
import { createAuthorizationBridge } from '../../../src/services/bridges/authorization-bridge'

function mockFunctionModuleApi() {
  return {
    getList: vi.fn(async () => [
      { id: '1', name: 'User Module', code: 'user', order: 1, isEnabled: true },
      { id: '2', name: 'Role Module', code: 'role', order: 2, isEnabled: true },
    ]),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    getTree: vi.fn(async () => []),
    getById: vi.fn(async () => null),
    enable: vi.fn(async () => undefined),
    disable: vi.fn(async () => undefined),
  }
}

function mockModuleFunctionApi() {
  return {
    getByModule: vi.fn(async () => [
      { id: 'f1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 },
    ]),
  }
}

function mockRoleFunctionApi() {
  return {
    getFunctionRoles: vi.fn(async () => [
      { id: 'rf1', roleId: 'r1', functionId: 'f1', isEnabled: true },
    ]),
    assignFunctions: vi.fn(async () => undefined),
    removeFunctions: vi.fn(async () => undefined),
    setFunctions: vi.fn(async () => undefined),
    clearFunctions: vi.fn(async () => undefined),
    getRoleFunctionIds: vi.fn(async () => []),
  }
}

function mockEntityRoleApi() {
  return {
    getByRole: vi.fn(async () => [
      { id: 'er1', entityInfoId: 'e1', roleId: 'r1', operation: 'Query', isEnabled: true },
    ]),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'er-new' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    getUserEntityRoles: vi.fn(async () => []),
    getById: vi.fn(async () => null),
    getByEntityInfo: vi.fn(async () => []),
  }
}

describe('authorization-bridge', () => {
  it('exposes functionModules / permissions / roleFunctions / entityRoles sub-contracts', () => {
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    expect(typeof bridge.functionModules.fetch).toBe('function')
    expect(typeof bridge.permissions.fetch).toBe('function')
    expect(typeof bridge.roleFunctions.fetch).toBe('function')
    expect(typeof bridge.entityRoles.fetch).toBe('function')
  })

  it('permissions.fetch requires moduleId filter and delegates to mfApi.getByModule', async () => {
    const mfApi = mockModuleFunctionApi()
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mfApi as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    const result = await bridge.permissions.fetch({
      pageIndex: 1, pageSize: 10, searchText: '', filters: { moduleId: 'm1' },
    })
    expect(mfApi.getByModule).toHaveBeenCalledWith('m1')
    expect(result.items).toHaveLength(1)
  })

  it('functionModules.fetch calls api.getList and returns paged results', async () => {
    const fmApi = mockFunctionModuleApi()
    const bridge = createAuthorizationBridge({
      functionModuleApi: fmApi as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    const result = await bridge.functionModules.fetch({
      pageIndex: 1, pageSize: 10, searchText: '', filters: {},
    })
    expect(fmApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('functionModules.create delegates to api.create', async () => {
    const fmApi = mockFunctionModuleApi()
    const bridge = createAuthorizationBridge({
      functionModuleApi: fmApi as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    await bridge.functionModules.create({ code: 'test.module' })
    expect(fmApi.create).toHaveBeenCalledWith({ code: 'test.module' })
  })

  it('entityRoles.delete loops through api.delete for each id', async () => {
    const erApi = mockEntityRoleApi()
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: erApi as never,
    })
    await bridge.entityRoles.delete(['a', 'b'])
    expect(erApi.delete).toHaveBeenCalledTimes(2)
    expect(erApi.delete).toHaveBeenCalledWith('a')
    expect(erApi.delete).toHaveBeenCalledWith('b')
  })
})
