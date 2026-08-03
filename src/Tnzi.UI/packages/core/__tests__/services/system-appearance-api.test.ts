import { describe, it, expect, vi } from 'vitest'
import { useAppearanceApi, useAdminAppearanceApi } from '../../src/services/system/api'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: { theme: null, updatedAt: null } })),
    post: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useAppearanceApi', () => {
  it('getTheme hits GET /appearance/theme/{scope}', async () => {
    const c = mockClient(); const api = useAppearanceApi(c as never)
    await api.getTheme('chat')
    expect(c.get).toHaveBeenCalledWith('/appearance/theme/chat')
  })
  it('getAdminTheme keeps hitting the pre-scope alias', async () => {
    const c = mockClient(); const api = useAppearanceApi(c as never)
    await api.getAdminTheme()
    expect(c.get).toHaveBeenCalledWith('/appearance/admin-theme')
  })
})

describe('useAdminAppearanceApi', () => {
  it('getTheme hits GET /admin/appearance/theme/{scope}', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    await api.getTheme('admin')
    expect(c.get).toHaveBeenCalledWith('/admin/appearance/theme/admin')
  })
  it('saveTheme puts the snapshot body to the scoped url', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    const body = { theme: { version: 1, admin: { tabVisible: false } } }
    await api.saveTheme('admin', body)
    expect(c.put).toHaveBeenCalledWith('/admin/appearance/theme/admin', body)
  })
  it('resetTheme deletes the scoped url', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    await api.resetTheme('chat')
    expect(c.delete).toHaveBeenCalledWith('/admin/appearance/theme/chat')
  })
  // The scope lands in the path, so a value with a slash in it would otherwise
  // address a different endpoint entirely.
  it('encodes the scope', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    await api.getTheme('a/b')
    expect(c.get).toHaveBeenCalledWith('/admin/appearance/theme/a%2Fb')
  })
})
