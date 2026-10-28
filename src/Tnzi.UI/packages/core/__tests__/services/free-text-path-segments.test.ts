import { describe, it, expect, vi } from 'vitest';
import { useAdminFileApi, useStoragePreviewApi } from '../../src/services/storage/api';
import { useAdminMcpToolAnalyticsApi } from '../../src/services/ai/api';

/**
 * Values a person typed (a file tag) or an external system named (an MCP tool)
 * go into the URL path, so they must be percent-encoded: `Q1/2026` would
 * otherwise add a path segment (404), `a#b` would be cut at the fragment and
 * `50%` would be a malformed escape.
 */

function mockClient() {
  const ok = { succeeded: true, success: true, code: 200, data: null };
  return {
    get: vi.fn(async () => ok),
    download: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: new Blob() })),
    resolveUrl: vi.fn((p: string) => `/api${p}`),
  };
}

describe('free-text path segments are encoded', () => {
  it.each([
    ['Q1/2026', 'Q1%2F2026'],
    ['a#b', 'a%23b'],
    ['50%', '50%25'],
    ['plain', 'plain'],
  ])('getFilesByTag(%j) requests by-tag/%s', async (tag, encoded) => {
    const c = mockClient();
    await useAdminFileApi(c as never).getFilesByTag(tag, 2, 50);
    expect(c.get).toHaveBeenCalledWith(`/admin/files/by-tag/${encoded}`, {
      params: { pageIndex: 2, pageSize: 50 },
    });
  });

  it('encodes the MCP tool name for its stats and its errors', async () => {
    const c = mockClient();
    const api = useAdminMcpToolAnalyticsApi(c as never);

    await api.getToolStats('files/read #1');
    await api.getToolErrors('files/read #1', 5);

    expect(c.get).toHaveBeenNthCalledWith(1, '/admin/ai/mcp/tool-analytics/stats/files%2Fread%20%231', {
      params: { from: undefined, to: undefined },
    });
    expect(c.get).toHaveBeenNthCalledWith(2, '/admin/ai/mcp/tool-analytics/errors/files%2Fread%20%231', {
      params: { top: 5 },
    });
  });
});

describe('useStoragePreviewApi().getGeneratedPreview', () => {
  it('downloads the preview with the session instead of handing out a bare URL', async () => {
    // The endpoint is authenticated and takes no ?sig=, so a URL put in an
    // <img> or <iframe> is refused for every file. The download carries the token.
    const c = mockClient();
    const result = await useStoragePreviewApi(c as never).getGeneratedPreview('f1');

    expect(c.download).toHaveBeenCalledWith('/files/preview/f1/preview');
    expect(c.resolveUrl).not.toHaveBeenCalled();
    expect(result.data).toBeInstanceOf(Blob);
  });
});
