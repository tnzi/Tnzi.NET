import { describe, it, expect, vi } from 'vitest'
import { resolveAvatarUrl } from '../resolveAvatarUrl'

/**
 * Minimal storage-api stub. `resolveAvatarUrl` only ever calls
 * `getPreviewUrl(id)`, so that's all we mock here.
 */
function makeStorageApi(previewUrl = 'https://cdn.test/files/preview') {
  return {
    getPreviewUrl: vi.fn((id: string) => `${previewUrl}/${id}`),
  }
}

describe('resolveAvatarUrl', () => {
  it('returns avatarUrl (UserDetailDto naming) when present', () => {
    const storageApi = makeStorageApi()
    const url = resolveAvatarUrl(
      { avatarUrl: 'https://example.com/me.png', avatarId: 'file-1' },
      storageApi,
    )
    expect(url).toBe('https://example.com/me.png')
    // External URL wins — never falls through to the preview endpoint.
    expect(storageApi.getPreviewUrl).not.toHaveBeenCalled()
  })

  it('returns getPreviewUrl(avatarId) when only the file id is set', () => {
    const storageApi = makeStorageApi()
    const url = resolveAvatarUrl({ avatarId: 'file-42' }, storageApi)
    expect(storageApi.getPreviewUrl).toHaveBeenCalledWith('file-42')
    expect(url).toBe('https://cdn.test/files/preview/file-42')
  })

  it('returns avatar (UserDto naming) when present', () => {
    const storageApi = makeStorageApi()
    const url = resolveAvatarUrl({ avatar: 'https://example.com/u.jpg' }, storageApi)
    expect(url).toBe('https://example.com/u.jpg')
    expect(storageApi.getPreviewUrl).not.toHaveBeenCalled()
  })

  it('returns null when nothing resolvable is present', () => {
    const storageApi = makeStorageApi()
    expect(resolveAvatarUrl({}, storageApi)).toBeNull()
    expect(resolveAvatarUrl(null, storageApi)).toBeNull()
    expect(resolveAvatarUrl(undefined, storageApi)).toBeNull()
    expect(storageApi.getPreviewUrl).not.toHaveBeenCalled()
  })

  it('treats blank / whitespace external urls as absent and falls back to avatarId', () => {
    const storageApi = makeStorageApi()
    const url = resolveAvatarUrl({ avatarUrl: '   ', avatarId: 'file-7' }, storageApi)
    expect(storageApi.getPreviewUrl).toHaveBeenCalledWith('file-7')
    expect(url).toBe('https://cdn.test/files/preview/file-7')
  })

  it('prefers avatarUrl over avatar when both are present', () => {
    const storageApi = makeStorageApi()
    const url = resolveAvatarUrl(
      { avatarUrl: 'https://example.com/detail.png', avatar: 'https://example.com/list.png' },
      storageApi,
    )
    expect(url).toBe('https://example.com/detail.png')
  })
})
