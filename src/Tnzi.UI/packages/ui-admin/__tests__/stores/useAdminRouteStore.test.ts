import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'
import type { AdminRouteRecord } from '../../src/stores/useAdminRouteStore'

describe('useAdminRouteStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  const sampleConstantRoutes: AdminRouteRecord[] = [
    { name: 'home', path: '/', meta: { title: 'Home', constant: true, order: 0 } },
    { name: 'login', path: '/login', meta: { title: 'Login', constant: true, hideInMenu: true } },
  ]

  const sampleAuthRoutes: AdminRouteRecord[] = [
    { name: 'users', path: '/identity/users', meta: { title: 'Users', permissions: ['user:read'], order: 1 } },
    { name: 'roles', path: '/identity/roles', meta: { title: 'Roles', permissions: ['role:read'], order: 2 } },
    { name: 'secret', path: '/admin/secret', meta: { title: 'Secret', permissions: ['admin:super'], order: 99 } },
  ]

  it('starts with empty routes', () => {
    const store = useAdminRouteStore()
    expect(store.constantRoutes).toHaveLength(0)
    expect(store.authRoutes).toHaveLength(0)
    expect(store.menus).toHaveLength(0)
  })

  it('setConstantRoutes populates constant routes', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(sampleConstantRoutes)
    expect(store.constantRoutes).toHaveLength(2)
  })

  it('setAuthRoutes populates auth routes filtered by permissions', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(sampleAuthRoutes, ['user:read', 'role:read'])
    expect(store.authRoutes).toHaveLength(2)
    expect(store.authRoutes.map((r) => r.name)).toContain('users')
    expect(store.authRoutes.map((r) => r.name)).not.toContain('secret')
  })

  it('menus excludes hideInMenu routes', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(sampleConstantRoutes)
    store.setAuthRoutes(sampleAuthRoutes, ['user:read', 'role:read', 'admin:super'])
    const menuNames = store.menus.map((m) => m.key)
    expect(menuNames).toContain('home')
    expect(menuNames).not.toContain('login')
    expect(menuNames).toContain('users')
  })

  it('menus sorts by meta.order', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(sampleAuthRoutes, ['user:read', 'role:read', 'admin:super'])
    const menus = store.menus
    expect(menus[0].meta?.order).toBeLessThanOrEqual(menus[1].meta?.order ?? 999)
  })

  it('cacheRoutes contains only routes with keepAlive meta', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(
      [
        { name: 'a', path: '/a', meta: { title: 'A', keepAlive: true } },
        { name: 'b', path: '/b', meta: { title: 'B', keepAlive: false } },
        { name: 'c', path: '/c', meta: { title: 'C' } },
      ],
      [],
    )
    expect(store.cacheRoutes).toContain('a')
    expect(store.cacheRoutes).not.toContain('b')
    expect(store.cacheRoutes).not.toContain('c')
  })

  it('resetRouteCache removes a single route from cacheRoutes', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(
      [
        { name: 'a', path: '/a', meta: { title: 'A', keepAlive: true } },
        { name: 'b', path: '/b', meta: { title: 'B', keepAlive: true } },
      ],
      [],
    )
    expect(store.cacheRoutes).toContain('a')
    store.resetRouteCache('a')
    expect(store.cacheRoutes).not.toContain('a')
    expect(store.cacheRoutes).toContain('b')
  })
})
