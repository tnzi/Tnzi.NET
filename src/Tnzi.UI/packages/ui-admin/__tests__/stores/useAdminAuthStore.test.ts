import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'

describe('useAdminAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts logged out with no token', () => {
    const store = useAdminAuthStore()
    expect(store.isLogin).toBe(false)
    expect(store.token).toBe('')
    expect(store.userInfo).toBeNull()
  })

  it('setToken marks the user as logged in', () => {
    const store = useAdminAuthStore()
    store.setToken('abc123')
    expect(store.token).toBe('abc123')
    expect(store.isLogin).toBe(true)
  })

  it('setUserInfo populates userInfo', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'alice', roles: ['admin'], permissions: ['user:read'] })
    expect(store.userInfo?.username).toBe('alice')
    expect(store.userRoles).toContain('admin')
    expect(store.userPermissions).toContain('user:read')
  })

  it('hasPermission returns true when permission present', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: ['user:read', 'user:write'] })
    expect(store.hasPermission('user:read')).toBe(true)
    expect(store.hasPermission('user:delete')).toBe(false)
  })

  it('hasAnyPermission returns true if any match', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: ['user:read'] })
    expect(store.hasAnyPermission(['user:read', 'user:write'])).toBe(true)
    expect(store.hasAnyPermission(['user:write', 'user:delete'])).toBe(false)
  })

  it('hasAllPermissions returns true only if all match', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: ['user:read', 'user:write'] })
    expect(store.hasAllPermissions(['user:read', 'user:write'])).toBe(true)
    expect(store.hasAllPermissions(['user:read', 'user:delete'])).toBe(false)
  })

  it('logout clears token and userInfo', () => {
    const store = useAdminAuthStore()
    store.setToken('t')
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: [] })
    store.logout()
    expect(store.token).toBe('')
    expect(store.userInfo).toBeNull()
    expect(store.isLogin).toBe(false)
  })

  it('hasRole returns true when role present', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: ['admin', 'editor'], permissions: [] })
    expect(store.hasRole('admin')).toBe(true)
    expect(store.hasRole('guest')).toBe(false)
  })
})
