import { describe, it, expect, vi } from 'vitest'
import { useAppearanceApi, useAdminAppearanceApi } from '../../services/system/api'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: { theme: null, updatedAt: null } })),
    post: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useAppearanceApi', () => {
  it('getAdminTheme hits GET /appearance/admin-theme', async () => {
    const c = mockClient(); const api = useAppearanceApi(c as never)
    await api.getAdminTheme()
    expect(c.get).toHaveBeenCalledWith('/appearance/admin-theme')
  })
})

describe('useAdminAppearanceApi', () => {
  it('getTheme hits GET /admin/appearance/theme', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    await api.getTheme()
    expect(c.get).toHaveBeenCalledWith('/admin/appearance/theme')
  })
  it('saveTheme puts the snapshot body to /admin/appearance/theme', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    const body = { theme: { version: 1, admin: { tabVisible: false } } }
    await api.saveTheme(body)
    expect(c.put).toHaveBeenCalledWith('/admin/appearance/theme', body)
  })
  it('resetTheme deletes /admin/appearance/theme', async () => {
    const c = mockClient(); const api = useAdminAppearanceApi(c as never)
    await api.resetTheme()
    expect(c.delete).toHaveBeenCalledWith('/admin/appearance/theme')
  })
})
