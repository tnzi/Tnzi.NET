import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'
import type { AdminRouteRecord } from '../../src/stores/useAdminRouteStore'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import type { MenuTreeNode } from '@tnzi/core/services/system'

describe('useAdminRouteStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  const constantRoutes: AdminRouteRecord[] = [
    { name: 'home', path: '/', meta: { title: 'Home', constant: true, order: 0 } },
    { name: 'login', path: '/login', meta: { title: 'Login', constant: true, hideInMenu: true } },
  ]

  // The real route table uses SINGULAR `meta.permission` (71 occurrences); the
  // store must honour it. We keep one plural-`permissions` route to lock the
  // back-compat OR semantics, and one public route (no requirement).
  const authRoutes: AdminRouteRecord[] = [
    { name: 'users', path: '/identity/users', meta: { title: 'Users', permission: 'user.view', order: 1 } },
    { name: 'roles', path: '/identity/roles', meta: { title: 'Roles', permission: 'role.view', order: 2 } },
    { name: 'secret', path: '/admin/secret', meta: { title: 'Secret', permission: 'admin.super', order: 99 } },
    { name: 'multi', path: '/multi', meta: { title: 'Multi', permissions: ['a.view', 'b.view'], order: 3 } },
    { name: 'public', path: '/public', meta: { title: 'Public', order: 4 } },
  ]

  function login(permissions: string[], superUser = false): void {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'u1', username: 'u', roles: [], permissions })
    if (superUser) auth.setSuperUser(true)
  }

  it('starts with empty routes', () => {
    const store = useAdminRouteStore()
    expect(store.constantRoutes).toHaveLength(0)
    expect(store.authRoutes).toHaveLength(0)
    expect(store.menus).toHaveLength(0)
  })

  it('setConstantRoutes populates constant routes', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(constantRoutes)
    expect(store.constantRoutes).toHaveLength(2)
  })

  it('setAuthRoutes keeps ALL routes (visibility is filtered at the menu layer, not removed)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    // every route stays resolvable for vue-router + guards
    expect(store.authRoutes).toHaveLength(authRoutes.length)
    expect(store.authRoutes.map((r) => r.name)).toContain('secret')
  })

  it('menus: fail-open when no user is loaded (permission source uninitialised)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    // not logged in → show everything so the sidebar is never blank before the
    // permission list is fetched (and apps that never wire permissions still work)
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('users')
    expect(names).toContain('secret')
  })

  it('menus: filters by singular meta.permission once a user is loaded', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login(['user.view', 'role.view'])
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('users')
    expect(names).toContain('roles')
    expect(names).not.toContain('secret') // lacks admin.super
    expect(names).toContain('public') // no requirement → public
  })

  it('menus: plural meta.permissions uses OR semantics', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login(['a.view']) // has one of [a.view, b.view]
    expect(store.menus.map((m) => m.key)).toContain('multi')
  })

  it('menus: plural meta.permissions hidden when none granted', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login(['user.view'])
    expect(store.menus.map((m) => m.key)).not.toContain('multi')
  })

  it('menus: super-user sees everything', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    expect(store.menus.map((m) => m.key)).toContain('secret')
  })

  it('menus: reacts to auth changes', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login(['user.view'])
    expect(store.menus.map((m) => m.key)).not.toContain('secret')
    // grant super-user → menu recomputes reactively
    useAdminAuthStore().setSuperUser(true)
    expect(store.menus.map((m) => m.key)).toContain('secret')
  })

  it('menus: a directory whose children are all filtered out is dropped', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      {
        name: 'group',
        path: '/group',
        meta: { title: 'Group', order: 1 }, // directory itself is public
        children: [
          { name: 'g-secret', path: 'secret', meta: { title: 'GS', permission: 'never.granted' } },
        ],
      },
    ])
    login(['user.view']) // does NOT have never.granted
    expect(store.menus.map((m) => m.key)).not.toContain('group')
  })

  it('deniedRouteNames: empty before a user is loaded (fail-open, matches menus)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    expect(store.deniedRouteNames.size).toBe(0)
  })

  it('deniedRouteNames: empty for super-users', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    expect(store.deniedRouteNames.size).toBe(0)
  })

  it('deniedRouteNames: collects route names whose meta.permission is not granted', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login(['user.view', 'role.view'])
    const denied = store.deniedRouteNames
    expect(denied.has('secret')).toBe(true) // lacks admin.super
    expect(denied.has('multi')).toBe(true) // lacks a.view/b.view
    expect(denied.has('users')).toBe(false) // granted
    expect(denied.has('public')).toBe(false) // no requirement → never denied
  })

  it('deniedRouteNames: includes hidden (hideInMenu) routes, unlike menus', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      { name: 'hidden.secret', path: '/hidden', meta: { title: 'H', permission: 'never.granted', hideInMenu: true } },
    ])
    login(['user.view'])
    // menus never shows it (hideInMenu); deniedRouteNames still flags it so its
    // persisted tab can be pruned.
    expect(store.menus.map((m) => m.key)).not.toContain('hidden.secret')
    expect(store.deniedRouteNames.has('hidden.secret')).toBe(true)
  })

  it('deniedRouteNames: walks children (denies a technical sub-page under an allowed module)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      {
        name: 'system',
        path: '/system',
        meta: { title: 'System', permission: 'system.view', order: 1 },
        children: [
          { name: 'system.menus', path: 'menus', meta: { title: 'Menus', permission: 'system.menu.view' } },
          { name: 'system.diagnostics', path: 'diagnostics', meta: { title: 'Diag', permission: 'system.diagnostics.view' } },
        ],
      },
    ])
    login(['system.view', 'system.menu.view']) // has the module + one child, not diagnostics
    const denied = store.deniedRouteNames
    expect(denied.has('system')).toBe(false)
    expect(denied.has('system.menus')).toBe(false)
    expect(denied.has('system.diagnostics')).toBe(true)
  })

  it('menus excludes hideInMenu routes', () => {
    const store = useAdminRouteStore()
    store.setConstantRoutes(constantRoutes)
    store.setAuthRoutes(authRoutes)
    login([], true)
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('home')
    expect(names).not.toContain('login')
  })

  it('menus sorts by meta.order', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    const menus = store.menus
    for (let i = 1; i < menus.length; i++) {
      expect(menus[i - 1].meta?.order ?? 999).toBeLessThanOrEqual(menus[i].meta?.order ?? 999)
    }
  })

  it('cacheRoutes contains only routes with keepAlive meta', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      { name: 'a', path: '/a', meta: { title: 'A', keepAlive: true } },
      { name: 'b', path: '/b', meta: { title: 'B', keepAlive: false } },
      { name: 'c', path: '/c', meta: { title: 'C' } },
    ])
    expect(store.cacheRoutes).toContain('a')
    expect(store.cacheRoutes).not.toContain('b')
    expect(store.cacheRoutes).not.toContain('c')
  })

  it('resetRouteCache removes a single route from cacheRoutes', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      { name: 'a', path: '/a', meta: { title: 'A', keepAlive: true } },
      { name: 'b', path: '/b', meta: { title: 'B', keepAlive: true } },
    ])
    expect(store.cacheRoutes).toContain('a')
    store.resetRouteCache('a')
    expect(store.cacheRoutes).not.toContain('a')
    expect(store.cacheRoutes).toContain('b')
  })

  it('menus: permission matching is case-insensitive (mirrors backend)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login(['User.View', 'ROLE.VIEW']) // mixed-case grants
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('users') // 'User.View' satisfies route 'user.view'
    expect(names).toContain('roles') // 'ROLE.VIEW' satisfies route 'role.view'
    expect(names).not.toContain('secret') // no admin.super in any case
  })

  // ── 'merge' menu source: backend Sys_Menu overrides keyed by menuKey ────────
  function node(partial: {
    menuKey?: string
    name: string
    icon?: string
    sortOrder?: number
    isHidden?: boolean
  }): MenuTreeNode {
    return {
      id: `n-${partial.menuKey ?? partial.name}`,
      parentId: null,
      menuKey: partial.menuKey,
      name: partial.name,
      icon: partial.icon ?? null,
      path: null,
      component: null,
      sortOrder: partial.sortOrder ?? 0,
      isHidden: partial.isHidden ?? false,
      permission: null,
      type: 1,
      children: [],
    }
  }

  it('merge: backend override retitles an entry by menuKey (route name)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    store.setBackendMenus([node({ menuKey: 'users', name: 'Renamed Users' })])
    expect(store.menus.find((m) => m.key === 'users')?.label).toBe('Renamed Users')
  })

  it('merge: backend override with isHidden drops the entry', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    store.setBackendMenus([node({ menuKey: 'secret', name: 'x', isHidden: true })])
    expect(store.menus.map((m) => m.key)).not.toContain('secret')
  })

  it('merge: empty backend menus is a no-op (route-derived)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    store.setBackendMenus([])
    expect(store.menus.map((m) => m.key)).toContain('users')
  })

  it('merge: override sortOrder reorders the entry', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    store.setBackendMenus([node({ menuKey: 'users', name: 'Users', sortOrder: 1000 })])
    const keys = store.menus.map((m) => m.key)
    expect(keys.indexOf('users')).toBeGreaterThan(keys.indexOf('roles'))
  })

  it('merge: reacts to setBackendMenus', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    login([], true)
    expect(store.menus.find((m) => m.key === 'users')?.label).toBe('Users')
    store.setBackendMenus([node({ menuKey: 'users', name: 'Live Renamed' })])
    expect(store.menus.find((m) => m.key === 'users')?.label).toBe('Live Renamed')
  })
})
