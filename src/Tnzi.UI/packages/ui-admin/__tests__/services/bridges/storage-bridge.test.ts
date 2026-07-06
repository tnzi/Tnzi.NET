import { describe, it, expect, vi } from 'vitest'
import { createStorageBridge } from '../../../src/services/bridges/storage-bridge'

function mockFileApi() {
  return {
    query: vi.fn(async () => ({
      items: [
        { id: 'f1', fileName: 'photo.jpg', originalName: 'photo.jpg', size: 1024, contentType: 'image/jpeg', url: '/files/f1', referenceCount: 0, extension: '.jpg', provider: 'local' },
        { id: 'f2', fileName: 'doc.pdf', originalName: 'doc.pdf', size: 2048, contentType: 'application/pdf', url: '/files/f2', referenceCount: 0, extension: '.pdf', provider: 'local' },
      ],
      totalCount: 2,
      pageIndex: 1,
      pageSize: 20,
    })),
    batchDelete: vi.fn(async () => undefined),
    getStatistics: vi.fn(async () => ({})),
    getPresignedUrl: vi.fn(async () => 'https://example.com/presigned'),
    cleanupTemporary: vi.fn(async () => 0),
    getTemporaryFiles: vi.fn(async () => []),
    getReferences: vi.fn(async () => []),
    getReferencesByEntity: vi.fn(async () => []),
    getReferenceStatistics: vi.fn(async () => ({})),
    syncReferenceCount: vi.fn(async () => 0),
    syncAllReferenceCounts: vi.fn(async () => 0),
    validateReferenceCount: vi.fn(async () => true),
    batchConfirmReferences: vi.fn(async () => undefined),
    batchUpdateReferences: vi.fn(async () => undefined),
    getUserStorageUsage: vi.fn(async () => ({})),
    getTopUsersByStorage: vi.fn(async () => []),
    verifyFileIntegrity: vi.fn(async () => ({})),
    batchVerifyIntegrity: vi.fn(async () => ({})),
    getSharesByFile: vi.fn(async () => []),
    queryActiveShares: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    batchRevokeShares: vi.fn(async () => 0),
    setFileTags: vi.fn(async (id: string) => ({ id })),
    getFilesByTag: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
  }
}

function mockStorageApi() {
  return {
    getById: vi.fn(async () => null),
    delete: vi.fn(async () => undefined),
    upload: vi.fn(async () => ({ id: 'f-new', fileName: 'file.txt', url: '/files/f-new', originalName: 'file.txt', size: 100, contentType: 'text/plain' })),
    uploadMany: vi.fn(async () => []),
    download: vi.fn(async () => undefined),
    getPreviewUrl: vi.fn(() => '/preview/f1'),
    getDownloadUrl: vi.fn((id: string) => `/files/${id}/download`),
    getThumbnailUrl: vi.fn(() => '/thumb/f1'),
    getUrl: vi.fn(async () => '/url/f1'),
    getPresignedUrl: vi.fn(async () => 'https://example.com/presigned'),
    rename: vi.fn(async () => null),
    copy: vi.fn(async () => null),
    createVersion: vi.fn(async () => null),
    getVersions: vi.fn(async () => []),
    restoreVersion: vi.fn(async () => null),
    createShare: vi.fn(async () => null),
    getShare: vi.fn(async () => null),
    revokeShare: vi.fn(async () => undefined),
    downloadByShareToken: vi.fn(async () => undefined),
    compress: vi.fn(async () => null),
    decompress: vi.fn(async () => []),
    initiateChunkedUpload: vi.fn(async () => ({ id: 'sess-1', fileName: 'file.txt', totalSize: 3000, chunkSize: 1024, totalChunks: 3, uploadedChunks: 0, uploadedSize: 0, isCompleted: false, isCancelled: false, creationTime: new Date().toISOString(), expiresAt: new Date().toISOString() })),
    uploadChunk: vi.fn(async () => ({ id: 'c1', uploadSessionId: 'sess-1', chunkIndex: 0, chunkSize: 1024, creationTime: new Date().toISOString() })),
    completeChunkedUpload: vi.fn(async () => ({ id: 'f-new', fileName: 'file.txt', url: '/files/f-new', originalName: 'file.txt', size: 3000, contentType: 'text/plain', extension: '.txt', provider: 'local', referenceCount: 0 })),
    cancelChunkedUpload: vi.fn(async () => undefined),
    getUploadProgress: vi.fn(async () => ({})),
  }
}

describe('storage-bridge', () => {
  it('exposes files / chunks / versions sub-contracts', () => {
    const bridge = createStorageBridge({
      fileApi: mockFileApi() as never,
      storageApi: mockStorageApi() as never,
    })
    expect(typeof bridge.files.fetch).toBe('function')
    expect(typeof bridge.files.create).toBe('function')
    expect(typeof bridge.files.update).toBe('function')
    expect(typeof bridge.files.delete).toBe('function')
    expect(typeof bridge.chunks.fetch).toBe('function')
    expect(typeof bridge.versions.fetch).toBe('function')
  })

  it('files.fetch calls fileApi.query and returns paged items', async () => {
    const fileApi = mockFileApi()
    const bridge = createStorageBridge({
      fileApi: fileApi as never,
      storageApi: mockStorageApi() as never,
    })
    const result = await bridge.files.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(fileApi.query).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('files.delete calls fileApi.batchDelete with ids', async () => {
    const fileApi = mockFileApi()
    const bridge = createStorageBridge({
      fileApi: fileApi as never,
      storageApi: mockStorageApi() as never,
    })
    await bridge.files.delete(['f1', 'f2'])
    expect(fileApi.batchDelete).toHaveBeenCalledWith(['f1', 'f2'])
  })

  it('files.downloadUrl delegates to storageApi.getDownloadUrl (deployment-prefix aware)', () => {
    const fileApi = mockFileApi()
    const storageApi = mockStorageApi()
    const bridge = createStorageBridge({
      fileApi: fileApi as never,
      storageApi: storageApi as never,
    })
    const url = bridge.files.downloadUrl('abc')
    expect(storageApi.getDownloadUrl).toHaveBeenCalledWith('abc')
    expect(typeof url).toBe('string')
    expect(url.length).toBeGreaterThan(0)
    // No hardcoded /api prefix — the URL is resolved by the HttpClient.
    expect(url).not.toContain('/api/files')
  })

  it('files.initUpload / uploadChunk / completeUpload delegate to storageApi', async () => {
    const storageApi = mockStorageApi()
    const bridge = createStorageBridge({
      fileApi: mockFileApi() as never,
      storageApi: storageApi as never,
    })
    const { uploadId } = await bridge.files.initUpload({ name: 'file.txt', size: 3000, chunkCount: 3 })
    expect(uploadId).toBe('sess-1')
    expect(storageApi.initiateChunkedUpload).toHaveBeenCalled()

    await bridge.files.uploadChunk(uploadId, 0, new Blob(['abc']))
    expect(storageApi.uploadChunk).toHaveBeenCalled()

    const complete = await bridge.files.completeUpload(uploadId)
    expect(complete.url).toBeTruthy()
    expect(storageApi.completeChunkedUpload).toHaveBeenCalled()
  })

  it('chunks.fetch and versions.fetch require an HttpClient when deps.client is missing', async () => {
    // Plan E wired both to /admin/storage/audit/* via direct HttpClient calls.
    // Missing client surfaces a clear error instead of a silent empty result.
    const bridge = createStorageBridge({
      fileApi: mockFileApi() as never,
      storageApi: mockStorageApi() as never,
    })
    await expect(bridge.chunks.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} }))
      .rejects.toThrow(/HttpClient.*required/)
    await expect(bridge.versions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} }))
      .rejects.toThrow(/HttpClient.*required/)
  })

  it('versions.restore reuses the user-side POST /files/{id}/versions/{v}/restore endpoint', async () => {
    // Audit GET endpoint stays read-only, but the bridge now reuses the
    // user-facing restore route so the admin UI's per-row "Restore" action
    // actually works. With no HttpClient injected, the bridge surfaces a
    // clear error instead of silently throwing the previous "read-only"
    // message that misled the page.
    const bridge = createStorageBridge({
      fileApi: mockFileApi() as never,
      storageApi: mockStorageApi() as never,
    })
    await expect(bridge.versions.restore('file-1', 2)).rejects.toThrow(/HttpClient.*required/)
  })

  it('chunks.fetch calls GET /admin/storage/audit/chunks with paging params', async () => {
    const mockClient = {
      get: vi.fn(async () => ({
        succeeded: true,
        data: {
          items: [
            { id: 'c1', uploadSessionId: 's1', chunkIndex: 0, chunkSize: 1024, md5Hash: 'abc', creationTime: '2026-04-14T00:00:00Z' },
          ],
          totalCount: 1,
          pageIndex: 1,
          pageSize: 20,
        },
      })),
    }
    const bridge = createStorageBridge({
      client: mockClient as never,
      fileApi: mockFileApi() as never,
      storageApi: mockStorageApi() as never,
    })
    const result = await bridge.chunks.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(mockClient.get).toHaveBeenCalledWith(expect.stringContaining('/admin/storage/audit/chunks'))
    expect(result.items).toHaveLength(1)
    expect(result.items[0].uploadSessionId).toBe('s1')
  })

  it('versions.fetch calls GET /admin/storage/audit/versions and forwards fileId filter', async () => {
    const mockClient = {
      get: vi.fn(async () => ({
        succeeded: true,
        data: {
          items: [
            { id: 'v1', fileId: 'f1', version: 2, path: '/tmp/v2', size: 2048, md5Hash: 'def', description: null, isCurrent: true, creationTime: '2026-04-14T00:00:00Z', creatorId: null },
          ],
          totalCount: 1,
          pageIndex: 1,
          pageSize: 20,
        },
      })),
    }
    const bridge = createStorageBridge({
      client: mockClient as never,
      fileApi: mockFileApi() as never,
      storageApi: mockStorageApi() as never,
    })
    const result = await bridge.versions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: { fileId: 'f1' } })
    expect(mockClient.get).toHaveBeenCalledWith(expect.stringContaining('fileId=f1'))
    expect(result.items[0].fileId).toBe('f1')
  })
})
