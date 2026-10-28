import { describe, it, expect, vi } from 'vitest'
import { useStorageApi } from '../../src/services/storage/api'
import { versionedFileName } from '../../src/services/storage/file-name'

function mockClient() {
  return {
    get: vi.fn(async () => ({ succeeded: true, code: 200, data: [] })),
    post: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ succeeded: true, code: 200, data: undefined })),
    upload: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    download: vi.fn(async () => ({ succeeded: true, code: 200, data: new Blob(['v2']) })),
    resolveUrl: vi.fn((p: string) => p),
  }
}

describe('useStorageApi versions', () => {
  it('downloadVersion fetches the bytes through the client, not as a bare URL', async () => {
    // The route needs a signed-in caller; a browser navigation carries no bearer token
    // and a ?sig= token does not pass the route's auth gate, so it has to be a client download.
    const c = mockClient()
    const result = await useStorageApi(c as never).downloadVersion('f1', 2)
    expect(c.download).toHaveBeenCalledWith('/files/f1/versions/2/download')
    expect(c.resolveUrl).not.toHaveBeenCalled()
    expect(result.data).toBeInstanceOf(Blob)
  })

  it('deleteVersion deletes /files/{id}/versions/{version}', async () => {
    const c = mockClient()
    await useStorageApi(c as never).deleteVersion('f1', 3)
    expect(c.delete).toHaveBeenCalledWith('/files/f1/versions/3')
  })

  it('createVersion uploads to /files/{id}/versions and sends the description only when given', async () => {
    const c = mockClient()
    const file = new File(['x'], 'report.pdf')
    await useStorageApi(c as never).createVersion('f1', file, 'fixed typos')
    expect(c.upload).toHaveBeenCalledWith('/files/f1/versions', file, { additionalData: { description: 'fixed typos' } })

    const plain = mockClient()
    await useStorageApi(plain as never).createVersion('f1', file)
    expect(plain.upload).toHaveBeenCalledWith('/files/f1/versions', file, { additionalData: undefined })
  })

  it('getVersions and restoreVersion hit the per-file routes', async () => {
    const c = mockClient()
    const api = useStorageApi(c as never)
    await api.getVersions('f1')
    await api.restoreVersion('f1', 1)
    expect(c.get).toHaveBeenCalledWith('/files/f1/versions')
    expect(c.post).toHaveBeenCalledWith('/files/f1/versions/1/restore')
  })
})

describe('versionedFileName', () => {
  // Same cases as the backend's VersionDownloadFileNameTests: a blob download and a
  // direct download of the same version must land under the same name.
  it.each([
    ['report.pdf', 2, 'report.v2.pdf'],
    ['archive.tar.gz', 3, 'archive.tar.v3.gz'],
    ['notes', 4, 'notes.v4'],
    ['.gitignore', 5, '.gitignore.v5'],
    ['notes.', 6, 'notes..v6'],
    ['folder.d/readme', 7, 'folder.d/readme.v7'],
    // Backslash is a separator on both sides regardless of the server's OS.
    ['dir\\report.pdf', 8, 'dir\\report.v8.pdf'],
    ['a.b\\c', 9, 'a.b\\c.v9'],
    // A dotfile after a separator is still a dotfile.
    ['conf/.env', 10, 'conf/.env.v10'],
    // ':' is not a separator on either side.
    ['a.b:c', 11, 'a.v11.b:c'],
  ])('%s, version %d → %s', (name, version, expected) => {
    expect(versionedFileName(name, version)).toBe(expected)
  })
})
