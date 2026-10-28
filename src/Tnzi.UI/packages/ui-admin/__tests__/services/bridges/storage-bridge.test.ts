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
    setFileVisibility: vi.fn(async (id: string, data: { isPublic: boolean }) => ({ id, isPublic: data.isPublic })),
    syncPublicFlags: vi.fn(async () => 7),
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
    downloadVersion: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: new Blob(['v']) })),
    deleteVersion: vi.fn(async () => ({ succeeded: true, success: true, code: 200 })),
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
  it('cleanup.trigger and files.initUpload reject with the server message on a failed envelope', async () => {
    const refused = (message: string) => ({ succeeded: false, success: false, code: 400, data: null, message })
    const fileApi = { ...mockFileApi(), cleanupTemporary: vi.fn(async () => refused('cleanup refused')) }
    const storageApi = { ...mockStorageApi(), initiateChunkedUpload: vi.fn(async () => refused('quota exceeded')) }
    const bridge = createStorageBridge({ fileApi: fileApi as never, storageApi: storageApi as never })
    await expect(bridge.cleanup.trigger(24)).rejects.toThrow('cleanup refused')
    // Before: `(undefined as { id: string }).id` - a TypeError that hid the quota message.
    await expect(bridge.files.initUpload({ name: 'f', size: 10, chunkCount: 1 })).rejects.toThrow('quota exceeded')
  })

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
    // No hardcoded /api prefix - the URL is resolved by the HttpClient.
    expect(url).not.toContain('/api/files')
  })

  it('files.upload forwards a stated lifetime and leaves the server default alone otherwise', async () => {
    const storageApi = mockStorageApi()
    const bridge = createStorageBridge({
      fileApi: mockFileApi() as never,
      storageApi: storageApi as never,
    })
    const file = new File(['x'], 'contract.pdf')

    await bridge.files.upload(file, { isTemporary: false })
    expect(storageApi.upload).toHaveBeenLastCalledWith(file, { isTemporary: false })

    await bridge.files.upload(file)
    expect(storageApi.upload).toHaveBeenLastCalledWith(file, undefined)
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

  describe('versions (one file\'s history, through the user-side files/{id}/versions routes)', () => {
    const refused = (code: number, message: string) => ({ succeeded: false, success: false, code, data: undefined, message })
    const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data })

    function bridgeWith(overrides: Record<string, unknown>) {
      const storageApi = { ...mockStorageApi(), ...overrides }
      return {
        storageApi,
        bridge: createStorageBridge({ fileApi: mockFileApi() as never, storageApi: storageApi as never }),
      }
    }

    it('list returns the history for one file', async () => {
      const history = [{ id: 'v2', fileId: 'f1', version: 2, size: 5, isCurrent: true, creationTime: '2026-10-03T00:00:00Z' }]
      const { bridge, storageApi } = bridgeWith({ getVersions: vi.fn(async () => ok(history)) })
      expect(await bridge.versions.list('f1')).toEqual(history)
      expect(storageApi.getVersions).toHaveBeenCalledWith('f1')
    })

    it('list resolves to null on 501: the host never loaded Tnzi.Storage.Workspace', async () => {
      const { bridge } = bridgeWith({
        getVersions: vi.fn(async () => refused(501, 'This capability requires the Tnzi.Storage.Workspace module')),
      })
      expect(await bridge.versions.list('f1')).toBeNull()
    })

    it('list rejects with the server message on any other refusal', async () => {
      const { bridge } = bridgeWith({ getVersions: vi.fn(async () => refused(404, 'File not found')) })
      await expect(bridge.versions.list('f1')).rejects.toThrow('File not found')
    })

    it('upload sends the file and note, and rejects a refused upload instead of resolving null', async () => {
      const file = new File(['x'], 'report.pdf')
      const created = { id: 'v3', fileId: 'f1', version: 3 }
      const { bridge, storageApi } = bridgeWith({ createVersion: vi.fn(async () => ok(created)) })
      expect(await bridge.versions.upload('f1', file, 'note')).toEqual(created)
      expect(storageApi.createVersion).toHaveBeenCalledWith('f1', file, 'note')

      const { bridge: refusing } = bridgeWith({ createVersion: vi.fn(async () => refused(400, 'File type is not allowed')) })
      await expect(refusing.versions.upload('f1', file)).rejects.toThrow('File type is not allowed')
    })

    it('restore posts through restoreVersion and rejects a refusal', async () => {
      const { bridge, storageApi } = bridgeWith({ restoreVersion: vi.fn(async () => ok({ id: 'f1' })) })
      await bridge.versions.restore('f1', 2)
      expect(storageApi.restoreVersion).toHaveBeenCalledWith('f1', 2)

      const { bridge: refusing } = bridgeWith({ restoreVersion: vi.fn(async () => refused(404, 'Version 9 not found')) })
      await expect(refusing.versions.restore('f1', 9)).rejects.toThrow('Version 9 not found')
    })

    it('download resolves the Blob, and rejects a refused download rather than handing back undefined', async () => {
      const blob = new Blob(['v2 bytes'])
      const { bridge, storageApi } = bridgeWith({ downloadVersion: vi.fn(async () => ok(blob)) })
      expect(await bridge.versions.download('f1', 2)).toBe(blob)
      expect(storageApi.downloadVersion).toHaveBeenCalledWith('f1', 2)

      const { bridge: refusing } = bridgeWith({ downloadVersion: vi.fn(async () => refused(404, 'Not Found')) })
      await expect(refusing.versions.download('f1', 2)).rejects.toThrow('Not Found')
    })

    it('delete goes through deleteVersion and surfaces the refusal for the current version', async () => {
      const { bridge, storageApi } = bridgeWith({})
      await bridge.versions.delete('f1', 1)
      expect(storageApi.deleteVersion).toHaveBeenCalledWith('f1', 1)

      const { bridge: refusing } = bridgeWith({
        deleteVersion: vi.fn(async () => refused(400, 'Cannot delete the current version')),
      })
      await expect(refusing.versions.delete('f1', 2)).rejects.toThrow('Cannot delete the current version')
    })
  })

  it('files.get reads one record and rejects a refused read', async () => {
    const record = { id: 'f1', originalName: 'contract.pdf' }
    const storageApi = { ...mockStorageApi(), getById: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: record })) }
    const bridge = createStorageBridge({ fileApi: mockFileApi() as never, storageApi: storageApi as never })
    expect(await bridge.files.get('f1')).toEqual(record)

    const denied = { ...mockStorageApi(), getById: vi.fn(async () => ({ succeeded: false, success: false, code: 404, message: 'File record not found' })) }
    const refusing = createStorageBridge({ fileApi: mockFileApi() as never, storageApi: denied as never })
    await expect(refusing.files.get('f1')).rejects.toThrow('File record not found')
  })

  it('metadata.get rejects a refused read instead of reporting the file has no metadata', async () => {
    // The metadata modal saves its rows back as the WHOLE map: an empty map standing
    // in for a failed read would let one added key erase every key the file has.
    const ok = { ...mockFileApi(), getMetadata: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: { author: 'Ann' } })) }
    const bridge = createStorageBridge({ fileApi: ok as never, storageApi: mockStorageApi() as never })
    expect(await bridge.metadata.get('f1')).toEqual({ author: 'Ann' })

    const none = { ...mockFileApi(), getMetadata: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: null })) }
    expect(await createStorageBridge({ fileApi: none as never, storageApi: mockStorageApi() as never }).metadata.get('f1')).toEqual({})

    const denied = { ...mockFileApi(), getMetadata: vi.fn(async () => ({ succeeded: false, success: false, code: 500, message: 'Metadata store unavailable' })) }
    const refusing = createStorageBridge({ fileApi: denied as never, storageApi: mockStorageApi() as never })
    await expect(refusing.metadata.get('f1')).rejects.toThrow('Metadata store unavailable')
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
    // Through @tnzi/core's useAdminStorageAuditApi: path + params object, not a
    // hand-built query string (the contract gate only sees api.ts paths).
    expect(mockClient.get).toHaveBeenCalledWith('/admin/storage/audit/chunks', {
      params: { pageIndex: 1, pageSize: 20, uploadSessionId: undefined },
    })
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
    expect(mockClient.get).toHaveBeenCalledWith('/admin/storage/audit/versions', {
      params: { pageIndex: 1, pageSize: 20, fileId: 'f1', currentOnly: undefined },
    })
    expect(result.items[0].fileId).toBe('f1')
  })

  it('visibility.set posts the flag and returns the updated record', async () => {
    const fileApi = mockFileApi()
    const bridge = createStorageBridge({
      fileApi: fileApi as never,
      storageApi: mockStorageApi() as never,
    })
    const updated = await bridge.visibility.set('f1', true)
    expect(fileApi.setFileVisibility).toHaveBeenCalledWith('f1', { isPublic: true })
    expect(updated.isPublic).toBe(true)
  })

  it('visibility.syncFromDeclarations returns the number of files repaired', async () => {
    const fileApi = mockFileApi()
    const bridge = createStorageBridge({
      fileApi: fileApi as never,
      storageApi: mockStorageApi() as never,
    })
    expect(await bridge.visibility.syncFromDeclarations()).toBe(7)
    expect(fileApi.syncPublicFlags).toHaveBeenCalled()
  })

  it('signedUrl mints a token and appends it, batching the whole list into one call', async () => {
    // Private files cannot render through a plain URL - an <img> sends no
    // Authorization header - so the bridge has to hand back a signed one.
    const post = vi.fn(async (_url: string, body: unknown) => ({
      succeeded: true,
      code: 200,
      data: (body as { fileIds: string[] }).fileIds.map((id) => ({
        fileId: id,
        token: `tok-${id}`,
        expiresAt: new Date(Date.now() + 600_000).toISOString(),
      })),
    }))
    const client = { get: vi.fn(), post, resolveUrl: (p: string) => `/api${p}` }
    const bridge = createStorageBridge({ client: client as never })

    const [one, two] = await Promise.all([
      bridge.files.signedUrl('f1'),
      bridge.files.signedUrl('f2', 'download'),
    ])

    expect(one).toBe('/api/files/f1/preview?sig=tok-f1')
    expect(two).toBe('/api/files/f2/download?sig=tok-f2')
    expect(post).toHaveBeenCalledTimes(1)
  })

  it('signedUrls resolves a batch and omits ids the caller cannot read', async () => {
    const post = vi.fn(async (_url: string, body: unknown) => ({
      succeeded: true,
      code: 200,
      data: (body as { fileIds: string[] })
        .fileIds.filter((id) => id !== 'denied')
        .map((id) => ({ fileId: id, token: `tok-${id}`, expiresAt: new Date(Date.now() + 600_000).toISOString() })),
    }))
    const client = { get: vi.fn(), post, resolveUrl: (p: string) => `/api${p}` }
    const bridge = createStorageBridge({ client: client as never })

    const map = await bridge.files.signedUrls(['f9', 'denied'])

    expect(map.get('f9')).toContain('sig=tok-f9')
    expect(map.has('denied')).toBe(false)
  })
})
