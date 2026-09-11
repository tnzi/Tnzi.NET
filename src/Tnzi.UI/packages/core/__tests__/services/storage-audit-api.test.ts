import { describe, it, expect, vi } from 'vitest';
import { useAdminStorageAuditApi } from '../../src/services/storage/api';

/**
 * The admin storage audit endpoints used to be hand-written URL strings inside
 * the ui-admin storage bridge, where nothing checked them against the backend
 * controllers. They now live here so the backend contract gate
 * (`FrontendBackendContractTests`) reads them like every other api.ts path.
 */

const EMPTY_PAGE = {
  items: [],
  totalCount: 0,
  pageIndex: 1,
  pageSize: 20,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

function mockClient() {
  return {
    get: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: EMPTY_PAGE })),
  };
}

describe('useAdminStorageAuditApi', () => {
  it('reads chunk rows from the admin audit endpoint, query as params', async () => {
    const c = mockClient();
    await useAdminStorageAuditApi(c as never).getChunks({ pageIndex: 2, pageSize: 50, uploadSessionId: 'u1' });
    expect(c.get).toHaveBeenCalledWith('/admin/storage/audit/chunks', {
      params: { pageIndex: 2, pageSize: 50, uploadSessionId: 'u1' },
    });
  });

  it('reads version rows with the file / current-only narrowing', async () => {
    const c = mockClient();
    await useAdminStorageAuditApi(c as never).getVersions({ pageIndex: 1, pageSize: 20, fileId: 'f1', currentOnly: true });
    expect(c.get).toHaveBeenCalledWith('/admin/storage/audit/versions', {
      params: { pageIndex: 1, pageSize: 20, fileId: 'f1', currentOnly: true },
    });
  });

  it('leaves the query out entirely when none is given (server defaults apply)', async () => {
    const c = mockClient();
    await useAdminStorageAuditApi(c as never).getChunks();
    expect(c.get).toHaveBeenCalledWith('/admin/storage/audit/chunks', { params: undefined });
  });
});
