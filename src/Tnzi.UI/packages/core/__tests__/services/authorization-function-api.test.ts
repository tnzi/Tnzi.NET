import { describe, it, expect, vi } from 'vitest';
import {
  useAdminEntityRoleApi,
  useAdminFunctionAuthorizationApi,
  useAdminModuleFunctionApi,
  useAdminRoleFunctionApi,
  useAdminUserFunctionApi,
} from '../../src/services/authorization/api';
import {
  PermissionCategory,
  PermissionRoleGrantState,
  type PermissionHoldersDto,
} from '../../src/services/authorization/types';

/**
 * The bounded overrides (`set-in-scope` / `set-denied-in-scope`) exist because the
 * whole-set `set` silently deletes every direct grant outside the caller's
 * slice. They shipped in the backend and the docs on 2026-08-02 with no client
 * method, so a consumer rendering a permission sub-matrix could only reach the
 * dangerous write. These tests pin the wire shape of the client half.
 */

function mockClient() {
  const ok = { succeeded: true, success: true, code: 200 };
  return {
    get: vi.fn(async () => ({ ...ok, data: undefined })),
    post: vi.fn(async () => ({ ...ok, data: undefined })),
    put: vi.fn(async () => ({ ...ok, data: undefined })),
    delete: vi.fn(async () => ({ ...ok, data: undefined })),
  };
}

describe('useAdminUserFunctionApi bounded overrides', () => {
  it('setFunctionsInScope PUTs the slice and the new allow set to set-in-scope', async () => {
    const c = mockClient();
    await useAdminUserFunctionApi(c as never).setFunctionsInScope('u1', ['f1', 'f2'], ['f1']);
    expect(c.put).toHaveBeenCalledWith('/admin/user-functions/user/u1/set-in-scope', {
      scopeFunctionIds: ['f1', 'f2'],
      functionIds: ['f1'],
    });
  });

  it('setDeniedFunctionsInScope PUTs the slice and the new deny set to set-denied-in-scope', async () => {
    const c = mockClient();
    await useAdminUserFunctionApi(c as never).setDeniedFunctionsInScope('u1', ['f1', 'f2'], []);
    expect(c.put).toHaveBeenCalledWith('/admin/user-functions/user/u1/set-denied-in-scope', {
      scopeFunctionIds: ['f1', 'f2'],
      functionIds: [],
    });
  });

  it('returns the server refusal envelope intact when ids fall outside the slice', async () => {
    const c = mockClient();
    const refused = {
      succeeded: false,
      success: false,
      code: 400,
      message: 'Function ids outside the scope: f9',
      errorCode: 'AUTH_OUT_OF_SCOPE',
    };
    c.put.mockResolvedValueOnce(refused as never);
    const result = await useAdminUserFunctionApi(c as never).setFunctionsInScope('u1', ['f1'], ['f9']);
    expect(result).toEqual(refused);
  });
});

describe('useAdminRoleFunctionApi parity with DefaultRoleFunctionAdminController', () => {
  it('hasFunction GETs role/{id}/has-function/{functionId}', async () => {
    const c = mockClient();
    await useAdminRoleFunctionApi(c as never).hasFunction('r1', 'f1');
    expect(c.get).toHaveBeenCalledWith('/admin/role-functions/role/r1/has-function/f1');
  });

  it('batchAssignFunctions POSTs roleIds and functionIds to batch/assign', async () => {
    const c = mockClient();
    await useAdminRoleFunctionApi(c as never).batchAssignFunctions(['r1', 'r2'], ['f1']);
    expect(c.post).toHaveBeenCalledWith('/admin/role-functions/batch/assign', {
      roleIds: ['r1', 'r2'],
      functionIds: ['f1'],
    });
  });

  it('exportRolePermissions GETs role/{id}/export', async () => {
    const c = mockClient();
    await useAdminRoleFunctionApi(c as never).exportRolePermissions('r1');
    expect(c.get).toHaveBeenCalledWith('/admin/role-functions/role/r1/export');
  });

  it('importRolePermissions POSTs the export document to role/{id}/import', async () => {
    const c = mockClient();
    const doc = { version: '1.0', exportedAt: '2026-09-12T00:00:00Z', sourceRoleId: 'r0', functionCodes: ['a.b.c'] };
    await useAdminRoleFunctionApi(c as never).importRolePermissions('r1', doc);
    expect(c.post).toHaveBeenCalledWith('/admin/role-functions/role/r1/import', doc);
  });
});

// Each assignment page reads the catalogue from its own controller, so the
// page's class-level code alone is enough to use it. Before, all three read
// `/admin/modules` (authorization.functionModule.view) and a delegated
// administrator holding only the page's code got a 403 and an empty matrix.
describe('permission catalogue reads sit behind each page gate', () => {
  it('role matrix reads role-functions/catalogue and role-functions/roles', async () => {
    const c = mockClient();
    const api = useAdminRoleFunctionApi(c as never);
    await api.getCatalogue();
    await api.getRoles();
    expect(c.get).toHaveBeenNthCalledWith(1, '/admin/role-functions/catalogue');
    expect(c.get).toHaveBeenNthCalledWith(2, '/admin/role-functions/roles');
  });

  it('user grant matrix reads user-functions/catalogue', async () => {
    const c = mockClient();
    await useAdminUserFunctionApi(c as never).getCatalogue();
    expect(c.get).toHaveBeenCalledWith('/admin/user-functions/catalogue');
  });

  it('entity-role matrix reads its role axis from data-auth/roles', async () => {
    const c = mockClient();
    await useAdminEntityRoleApi(c as never).getRoles();
    expect(c.get).toHaveBeenCalledWith('/admin/data-auth/roles');
  });

  it('permission catalogue page reads module-functions/catalogue', async () => {
    const c = mockClient();
    await useAdminModuleFunctionApi(c as never).getCatalogue();
    expect(c.get).toHaveBeenCalledWith('/admin/module-functions/catalogue');
  });
});

describe('useAdminFunctionAuthorizationApi permission holders', () => {
  it('getPermissionHolders GETs permission/holders with the code as a query parameter', async () => {
    const c = mockClient();
    await useAdminFunctionAuthorizationApi(c as never).getPermissionHolders('catalog.product.export');
    expect(c.get).toHaveBeenCalledWith('/admin/function-authorization/permission/holders', {
      params: { permissionName: 'catalog.product.export' },
    });
  });

  it('passes the holders payload through untouched', async () => {
    const c = mockClient();
    const holders: PermissionHoldersDto = {
      functionId: 'f1',
      code: 'catalog.product.export',
      name: 'Export Products',
      category: PermissionCategory.Business,
      isEnabled: true,
      isRetired: false,
      isInEffect: true,
      userNamesHidden: false,
      roles: [{ id: 'r1', name: 'Root', state: PermissionRoleGrantState.SuperAdmin, memberCount: 1 }],
      users: [{ id: 'u1', userName: 'erin', isSuperAdmin: true, isDirect: false, roles: [{ id: 'r1', name: 'Root' }] }],
      deniedUsers: [],
    };
    c.get.mockResolvedValueOnce({ succeeded: true, success: true, code: 200, data: holders } as never);
    const result = await useAdminFunctionAuthorizationApi(c as never).getPermissionHolders('catalog.product.export');
    expect(result.data).toEqual(holders);
  });

  it('grant states use the backend member names (the wire format of JsonStringEnumConverter)', () => {
    expect(Object.values(PermissionRoleGrantState)).toEqual(['SuperAdmin', 'Effective', 'Dormant', 'GrantDisabled']);
  });

  it('returns the 404 envelope for an unknown code instead of throwing', async () => {
    const c = mockClient();
    const notFound = { succeeded: false, success: false, code: 404, message: "Permission 'x.y' not found" };
    c.get.mockResolvedValueOnce(notFound as never);
    const result = await useAdminFunctionAuthorizationApi(c as never).getPermissionHolders('x.y');
    expect(result).toEqual(notFound);
  });
});
