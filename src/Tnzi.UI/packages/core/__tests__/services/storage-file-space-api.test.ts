import { describe, it, expect, vi } from 'vitest'
import {
  useAdminFileApi,
  useAdminFileFolderApi,
  useAdminStorageQuotaApi,
  useFileFolderApi,
  useFileSpaceApi,
  useRecycleBinApi,
  useStorageApi,
} from '../../src/services/storage/api'
import { FileSpaceSortField } from '../../src/services/storage/types'

function mockClient() {
  return {
    get: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    post: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ succeeded: true, code: 200, data: undefined })),
    upload: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })),
  }
}

describe('useFileSpaceApi', () => {
  it('lists a folder with its paging and sort options', async () => {
    const c = mockClient()
    const query = { folderId: 'd1', sortBy: FileSpaceSortField.Size, descending: true, pageIndex: 2, pageSize: 25 }
    await useFileSpaceApi(c as never).list(query)
    expect(c.get).toHaveBeenCalledWith('/storage/space/items', { params: query })
  })

  it('lists the root when no folder is given', async () => {
    const c = mockClient()
    await useFileSpaceApi(c as never).list()
    expect(c.get).toHaveBeenCalledWith('/storage/space/items', { params: {} })
  })

  it('searches by name and type', async () => {
    const c = mockClient()
    await useFileSpaceApi(c as never).search({ keyword: 'summary', extension: '.md' })
    expect(c.get).toHaveBeenCalledWith('/storage/space/search', { params: { keyword: 'summary', extension: '.md' } })
  })

  it('uploads into a folder, and sends no folder field for the root', async () => {
    const c = mockClient()
    const file = new File(['x'], 'notes.md')
    await useFileSpaceApi(c as never).upload(file, 'd1')
    expect(c.upload).toHaveBeenCalledWith('/storage/space/files', file, { onProgress: undefined, additionalData: { folderId: 'd1' } })

    const root = mockClient()
    await useFileSpaceApi(root as never).upload(file)
    expect(root.upload).toHaveBeenCalledWith('/storage/space/files', file, { onProgress: undefined, additionalData: undefined })
  })
})

describe('useRecycleBinApi', () => {
  it('moves, lists, restores, purges and empties through /storage/recycle-bin', async () => {
    const c = mockClient()
    const bin = useRecycleBinApi(c as never)

    await bin.moveToRecycleBin({ fileIds: ['f1'], folderIds: ['d1'] })
    await bin.list(2, 10)
    await bin.restore(['e1'])
    await bin.purge(['e2'])
    await bin.empty()

    expect(c.post).toHaveBeenCalledWith('/storage/recycle-bin', { fileIds: ['f1'], folderIds: ['d1'] })
    expect(c.get).toHaveBeenCalledWith('/storage/recycle-bin', { params: { pageIndex: 2, pageSize: 10 } })
    expect(c.post).toHaveBeenCalledWith('/storage/recycle-bin/restore', { entryIds: ['e1'] })
    expect(c.post).toHaveBeenCalledWith('/storage/recycle-bin/purge', { entryIds: ['e2'] })
    expect(c.delete).toHaveBeenCalledWith('/storage/recycle-bin')
  })
})

describe('useFileFolderApi (the signed-in user own folders)', () => {
  it('uses the user routes, not the admin ones', async () => {
    const c = mockClient()
    const folders = useFileFolderApi(c as never)

    await folders.getTree()
    await folders.create({ name: 'Docs' })
    await folders.moveFiles({ fileIds: ['f1'], folderId: 'd1' })
    await folders.move('d1', 'd2')

    expect(c.get).toHaveBeenCalledWith('/storage/folders/tree', { params: undefined })
    expect(c.post).toHaveBeenCalledWith('/storage/folders', { name: 'Docs' })
    expect(c.post).toHaveBeenCalledWith('/storage/folders/move-files', { fileIds: ['f1'], folderId: 'd1' })
    expect(c.post).toHaveBeenCalledWith('/storage/folders/d1/move', null, { params: { newParentId: 'd2' } })
  })
})

describe('quota', () => {
  it('reads the signed-in user quota and an administrator view of any user', async () => {
    const c = mockClient()
    await useStorageApi(c as never).getQuota()
    await useAdminFileApi(c as never).getUserQuota('u1')
    expect(c.get).toHaveBeenCalledWith('/files/quota')
    expect(c.get).toHaveBeenCalledWith('/admin/files/quota/u1')
  })

  it('manages per-user allowances under /admin/storage/quotas', async () => {
    const c = mockClient()
    const quotas = useAdminStorageQuotaApi(c as never)

    await quotas.list()
    await quotas.get('u1')
    await quotas.set('u1', { allowanceBytes: 1024, note: 'trial' })
    await quotas.remove('u1')

    expect(c.get).toHaveBeenCalledWith('/admin/storage/quotas', { params: { pageIndex: 1, pageSize: 20 } })
    expect(c.get).toHaveBeenCalledWith('/admin/storage/quotas/u1')
    expect(c.put).toHaveBeenCalledWith('/admin/storage/quotas/u1', { allowanceBytes: 1024, note: 'trial' })
    expect(c.delete).toHaveBeenCalledWith('/admin/storage/quotas/u1')
  })
})

describe('chunked upload completion into a folder', () => {
  it('sends the folder only when one is given', async () => {
    const c = mockClient()
    const storage = useStorageApi(c as never)

    await storage.completeChunkedUpload('s1', { folderId: 'd1' })
    await storage.completeChunkedUpload('s2')
    await storage.completeChunkedUpload('s3', true)

    expect(c.post).toHaveBeenCalledWith('/files/upload/chunk/s1/complete', { isTemporary: false, folderId: 'd1' })
    expect(c.post).toHaveBeenCalledWith('/files/upload/chunk/s2/complete', { isTemporary: false })
    expect(c.post).toHaveBeenCalledWith('/files/upload/chunk/s3/complete', { isTemporary: true })
  })
})

describe('useAdminFileFolderApi().getTree', () => {
  it('narrows the tree to one owner when asked', async () => {
    const c = mockClient()
    const folders = useAdminFileFolderApi(c as never)

    await folders.getTree(null, 'u1')
    await folders.getTree()

    expect(c.get).toHaveBeenCalledWith('/admin/storage/folders/tree', { params: { ownerId: 'u1' } })
    expect(c.get).toHaveBeenCalledWith('/admin/storage/folders/tree', { params: undefined })
  })
})
