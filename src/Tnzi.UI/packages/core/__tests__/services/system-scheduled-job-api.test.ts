import { describe, it, expect, vi } from 'vitest';
import { useAdminScheduledJobApi } from '../../src/services/system/api';

/**
 * Recurring Hangfire jobs. The ui-admin system bridge used to hand-write these
 * four paths (with a TODO pointing at a codegen that no longer exists); moving
 * them here puts them under the backend contract gate with every other api.ts
 * path. The controller is `Tnzi.Hangfire`'s `DefaultScheduledJobAdminController`.
 */

function mockClient() {
  const ok = async () => ({ succeeded: true, success: true, code: 200, data: undefined });
  return { get: vi.fn(ok), post: vi.fn(ok), delete: vi.fn(ok) };
}

describe('useAdminScheduledJobApi', () => {
  it('lists recurring jobs', async () => {
    const c = mockClient();
    await useAdminScheduledJobApi(c as never).getList();
    expect(c.get).toHaveBeenCalledWith('/admin/scheduled-jobs');
  });

  it('reads, triggers and removes one job by id', async () => {
    const c = mockClient();
    const api = useAdminScheduledJobApi(c as never);
    await api.get('nightly-cleanup');
    await api.trigger('nightly-cleanup');
    await api.delete('nightly-cleanup');
    expect(c.get).toHaveBeenCalledWith('/admin/scheduled-jobs/nightly-cleanup');
    expect(c.post).toHaveBeenCalledWith('/admin/scheduled-jobs/nightly-cleanup/trigger');
    expect(c.delete).toHaveBeenCalledWith('/admin/scheduled-jobs/nightly-cleanup');
  });

  it('URL-encodes the job id (Hangfire ids are free text and may contain slashes)', async () => {
    const c = mockClient();
    await useAdminScheduledJobApi(c as never).trigger('tenant/acme:sync');
    expect(c.post).toHaveBeenCalledWith('/admin/scheduled-jobs/tenant%2Facme%3Async/trigger');
  });
});
