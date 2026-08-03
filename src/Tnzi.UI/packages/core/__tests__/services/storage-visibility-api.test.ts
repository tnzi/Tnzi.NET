import { describe, it, expect, vi } from 'vitest'
import { useStorageApi, useAdminFileApi } from '../../src/services/storage/api'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: null })),
    post: vi.fn(async () => ({ success: true, code: 200, data: 0 })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
    upload: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    uploadFormData: vi.fn(async () => ({ success: true, code: 200, data: [] })),
    resolveUrl: vi.fn((p: string) => p),
    download: vi.fn(async () => undefined),
  }
}

const FILE = new File(['x'], 'avatar.png', { type: 'image/png' })

describe('useStorageApi upload options', () => {
  it('sends no isPublic field by default (files are private unless asked)', async () => {
    const c = mockClient()
    await useStorageApi(c as never).upload(FILE)
    expect(c.upload).toHaveBeenCalledWith('/files/upload', FILE, {
      onProgress: undefined,
      additionalData: undefined,
    })
  })

  it('sends isPublic as a form field when requested', async () => {
    const c = mockClient()
    await useStorageApi(c as never).upload(FILE, { isPublic: true })
    expect(c.upload).toHaveBeenCalledWith(
      '/files/upload',
      FILE,
      expect.objectContaining({ additionalData: { isPublic: 'true' } }),
    )
  })

  it('still accepts a bare progress callback in the options position', async () => {
    // Backwards compatibility: `upload(file, onProgress)` predates the options
    // object and is called that way across consuming apps.
    const c = mockClient()
    const onProgress = vi.fn()
    await useStorageApi(c as never).upload(FILE, onProgress)
    expect(c.upload).toHaveBeenCalledWith(
      '/files/upload',
      FILE,
      expect.objectContaining({ onProgress, additionalData: undefined }),
    )
  })

  it('uploadMany appends isPublic to the form data only when requested', async () => {
    const c = mockClient()
    await useStorageApi(c as never).uploadMany([FILE], { isPublic: true })
    const form = c.uploadFormData.mock.calls[0][1] as FormData
    expect(form.get('isPublic')).toBe('true')

    const plain = mockClient()
    await useStorageApi(plain as never).uploadMany([FILE])
    expect((plain.uploadFormData.mock.calls[0][1] as FormData).get('isPublic')).toBeNull()
  })
})

describe('useAdminFileApi visibility', () => {
  it('setFileVisibility puts the flag to /admin/files/{id}/visibility', async () => {
    const c = mockClient()
    await useAdminFileApi(c as never).setFileVisibility('f1', { isPublic: true })
    expect(c.put).toHaveBeenCalledWith('/admin/files/f1/visibility', { isPublic: true })
  })

  it('syncPublicFlags posts to /admin/files/sync-public-flags', async () => {
    const c = mockClient()
    await useAdminFileApi(c as never).syncPublicFlags()
    expect(c.post).toHaveBeenCalledWith('/admin/files/sync-public-flags')
  })
})
