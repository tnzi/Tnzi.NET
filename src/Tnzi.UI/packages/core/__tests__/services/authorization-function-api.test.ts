import { describe, it, expect, vi } from 'vitest';
import {
  useAdminRoleFunctionApi,
  useAdminUserFunctionApi,
} from '../../src/services/authorization/api';

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
