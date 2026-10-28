import { describe, it, expect, vi } from 'vitest'
import { collectDescendants, createAuthorizationBridge } from '../../../src/services/bridges/authorization-bridge'

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

const catalogueRows = [
  {
    id: 'm1', code: 'user', name: 'User', order: 1, isEnabled: true,
    functions: [{ id: 'f1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 }],
  },
  { id: 'm2', code: 'order', name: 'Order', order: 2, isEnabled: false, parentId: 'm1', functions: [] },
]

function mockModuleFunctionApi() {
  return {
    getByModule: vi.fn(async () => [
      { id: 'f1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 },
    ]),
    getCatalogue: vi.fn(async () => catalogueRows),
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
    getCatalogue: vi.fn(async () => catalogueRows),
    getRoles: vi.fn(async () => [
      { id: 'r1', name: 'Editor', normalizedName: 'EDITOR', isSystem: false, isDefault: false, creationTime: '2026-10-01T00:00:00Z' },
    ]),
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

  // The permission catalogue page is gated by `authorization.permission.view`;
  // its rows come from the module-functions catalogue, never from the module
  // management read (`/admin/modules/{id}/functions`, functionModule.view).
  it('permissions.fetch requires moduleId filter and reads the module-functions catalogue', async () => {
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
    expect(mfApi.getCatalogue).toHaveBeenCalled()
    expect(mfApi.getByModule).not.toHaveBeenCalled()
    expect(result.items.map((f) => f.code)).toEqual(['user.view'])
  })

  it('each surface reads the catalogue behind its own gate and splits it per module', async () => {
    const fmApi = mockFunctionModuleApi()
    const mfApi = mockModuleFunctionApi()
    const rfApi = mockRoleFunctionApi()
    const ufApi = { getCatalogue: vi.fn(async () => catalogueRows) }
    const bridge = createAuthorizationBridge({
      functionModuleApi: fmApi as never,
      moduleFunctionApi: mfApi as never,
      roleFunctionApi: rfApi as never,
      userFunctionApi: ufApi as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })

    for (const read of [bridge.roleFunctions.catalogue, bridge.userFunctions.catalogue, bridge.permissions.catalogue]) {
      const catalogue = await read()
      expect(catalogue.modules.map((m) => m.id)).toEqual(['m1', 'm2'])
      expect(catalogue.modules[1]?.parentId).toBe('m1')
      expect(catalogue.functionsByModule.get('m1')?.map((f) => f.code)).toEqual(['user.view'])
      expect(catalogue.functionsByModule.get('m2')).toEqual([])
    }
    expect(rfApi.getCatalogue).toHaveBeenCalledTimes(1)
    expect(ufApi.getCatalogue).toHaveBeenCalledTimes(1)
    expect(mfApi.getCatalogue).toHaveBeenCalledTimes(1)
    expect(fmApi.getList).not.toHaveBeenCalled()
    expect(mfApi.getByModule).not.toHaveBeenCalled()
  })

  it('roleFunctions.roles reads the role list from the role-functions surface', async () => {
    const rfApi = mockRoleFunctionApi()
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: rfApi as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    expect((await bridge.roleFunctions.roles()).map((r) => r.name)).toEqual(['Editor'])
    expect(rfApi.getRoles).toHaveBeenCalled()
  })

  it('a refused catalogue read rejects with the server message', async () => {
    const rfApi = mockRoleFunctionApi()
    rfApi.getCatalogue = vi.fn(async () => ({
      succeeded: false, success: false, code: 403, data: null, message: 'Permission denied: authorization.roleFunction.view',
    }) as never)
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: rfApi as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    await expect(bridge.roleFunctions.catalogue()).rejects.toThrow('Permission denied: authorization.roleFunction.view')
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

  it('functionModules.fetch searches the name, code and description, not the field names', async () => {
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })
    const search = (searchText: string) =>
      bridge.functionModules.fetch({ pageIndex: 1, pageSize: 10, searchText, filters: {} })
    expect((await search('code')).totalCount).toBe(0)
    expect((await search('ROLE')).items.map((m) => m.code)).toEqual(['role'])
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

  it('permissions.holders unwraps the holders payload from the function-authorization api', async () => {
    const holders = { code: 'user.view', users: [{ id: 'u1', userName: 'alice' }] }
    const faApi = {
      getPermissionHolders: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: holders })),
    }
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
      functionAuthorizationApi: faApi as never,
    })
    await expect(bridge.permissions.holders('user.view')).resolves.toEqual(holders)
    expect(faApi.getPermissionHolders).toHaveBeenCalledWith('user.view')
  })

  it('permissions.holders rejects with the server message (an unknown code is not an empty list)', async () => {
    const faApi = {
      getPermissionHolders: vi.fn(async () => ({
        succeeded: false, success: false, code: 404, message: "Permission 'x.y' not found",
      })),
    }
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
      functionAuthorizationApi: faApi as never,
    })
    await expect(bridge.permissions.holders('x.y')).rejects.toThrow("Permission 'x.y' not found")
  })

  describe('grant-set reads reject a refused read (their Save replaces the whole set)', () => {
    // An empty list standing in for a failed read would let one tick replace every
    // grant a role or user holds, and one deny tick lift every other deny.
    const refused = { succeeded: false, success: false, code: 500, message: 'Grant store unavailable' }
    const granted = { succeeded: true, success: true, code: 200, data: ['f1', 'f2'] }

    function bridgeWith(rfApi: object, ufApi: object) {
      return createAuthorizationBridge({
        functionModuleApi: mockFunctionModuleApi() as never,
        moduleFunctionApi: mockModuleFunctionApi() as never,
        roleFunctionApi: { ...mockRoleFunctionApi(), ...rfApi } as never,
        entityRoleApi: mockEntityRoleApi() as never,
        userFunctionApi: ufApi as never,
      })
    }

    it('roleFunctions.getAssignedIds', async () => {
      const ok = bridgeWith({ getRoleFunctionIds: vi.fn(async () => granted) }, {})
      await expect(ok.roleFunctions.getAssignedIds('r1')).resolves.toEqual(['f1', 'f2'])
      const failing = bridgeWith({ getRoleFunctionIds: vi.fn(async () => refused) }, {})
      await expect(failing.roleFunctions.getAssignedIds('r1')).rejects.toThrow('Grant store unavailable')
    })

    it('userFunctions.getAssignedIds', async () => {
      const ok = bridgeWith({}, { getUserFunctionIds: vi.fn(async () => granted) })
      await expect(ok.userFunctions.getAssignedIds('u1')).resolves.toEqual(['f1', 'f2'])
      const failing = bridgeWith({}, { getUserFunctionIds: vi.fn(async () => refused) })
      await expect(failing.userFunctions.getAssignedIds('u1')).rejects.toThrow('Grant store unavailable')
    })

    it('userFunctions.getDeniedIds', async () => {
      const ok = bridgeWith({}, { getDeniedFunctionIds: vi.fn(async () => granted) })
      await expect(ok.userFunctions.getDeniedIds('u1')).resolves.toEqual(['f1', 'f2'])
      const failing = bridgeWith({}, { getDeniedFunctionIds: vi.fn(async () => refused) })
      await expect(failing.userFunctions.getDeniedIds('u1')).rejects.toThrow('Grant store unavailable')
    })

    it('a bare array (no envelope) still reads as the set', async () => {
      const bare = bridgeWith({ getRoleFunctionIds: vi.fn(async () => ['f3']) }, {})
      await expect(bare.roleFunctions.getAssignedIds('r1')).resolves.toEqual(['f3'])
    })
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

  // The update request replaces operation + filter only; the server has no
  // enabled flag on it, so sending one would claim a re-enable that never happens.
  it('entityRoles.setCell updates an existing rule with the new filter and no enabled flag', async () => {
    const erApi = mockEntityRoleApi()
    erApi.getByEntityInfo.mockResolvedValue([
      { id: 'er1', entityInfoId: 'e1', roleId: 'r1', operation: 'Query', filter: '{"a":1}', isEnabled: true },
    ] as never)
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: erApi as never,
    })

    await bridge.entityRoles.setCell({ entityInfoId: 'e1', roleId: 'r1', operation: 'Query', enable: true, filter: '{"b":2}' })

    expect(erApi.update).toHaveBeenCalledWith('er1', { operation: 'Query', filter: '{"b":2}' })
    expect(erApi.create).not.toHaveBeenCalled()
  })

  it('entityRoles.setCell leaves an existing rule alone when its filter is unchanged', async () => {
    const erApi = mockEntityRoleApi()
    erApi.getByEntityInfo.mockResolvedValue([
      { id: 'er1', entityInfoId: 'e1', roleId: 'r1', operation: 'Query', filter: '{"a":1}', isEnabled: true },
    ] as never)
    const bridge = createAuthorizationBridge({
      functionModuleApi: mockFunctionModuleApi() as never,
      moduleFunctionApi: mockModuleFunctionApi() as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: erApi as never,
    })

    await bridge.entityRoles.setCell({ entityInfoId: 'e1', roleId: 'r1', operation: 'Query', enable: true, filter: '{"a":1}' })

    expect(erApi.update).not.toHaveBeenCalled()
    expect(erApi.create).not.toHaveBeenCalled()
  })
})

describe('collectDescendants', () => {
  const tree = [
    { id: 'a', parentId: undefined },
    { id: 'b', parentId: 'a' },
    { id: 'c', parentId: 'b' },
    { id: 'd', parentId: 'a' },
    { id: 'e', parentId: undefined },
  ]

  it('returns every module below the root, breadth-first, without the root itself', () => {
    expect(collectDescendants(tree, 'a').map((m) => m.id)).toEqual(['b', 'd', 'c'])
    expect(collectDescendants(tree, 'e')).toEqual([])
  })

  it('terminates on a parent cycle left in the data and lists each module once', () => {
    // A -> B -> A, with C hanging off A: a walk without a visited set never ends.
    const cyclic = [
      { id: 'a', parentId: 'b' },
      { id: 'b', parentId: 'a' },
      { id: 'c', parentId: 'a' },
    ]
    expect(collectDescendants(cyclic, 'a').map((m) => m.id)).toEqual(['b', 'c'])
  })
})

describe('functionModules.previewCascade on cyclic data', () => {
  it('terminates and counts every module once', async () => {
    const fmApi = mockFunctionModuleApi()
    fmApi.getList.mockResolvedValue([
      { id: 'a', name: 'A', code: 'a', order: 1, isEnabled: true, parentId: 'b' },
      { id: 'b', name: 'B', code: 'b', order: 2, isEnabled: true, parentId: 'a' },
    ] as never)
    const mfApi = mockModuleFunctionApi()
    const bridge = createAuthorizationBridge({
      functionModuleApi: fmApi as never,
      moduleFunctionApi: mfApi as never,
      roleFunctionApi: mockRoleFunctionApi() as never,
      entityRoleApi: mockEntityRoleApi() as never,
    })

    const preview = await bridge.functionModules.previewCascade('a')

    expect(preview.descendantModules.map((m) => m.id)).toEqual(['b'])
    expect(preview.descendantModuleCount).toBe(1)
    expect(mfApi.getByModule).toHaveBeenCalledTimes(2)
  })
})
