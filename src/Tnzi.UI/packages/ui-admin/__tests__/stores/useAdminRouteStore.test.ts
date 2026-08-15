import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'
import type { AdminRouteRecord } from '../../src/stores/useAdminRouteStore'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'

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

  it('menus: fail-open while logged IN but permissions still loading (token set, userInfo null)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    // A session exists (token set on login / restore) but the permission list
    // hasn't arrived yet (userInfo still null) → show everything so the sidebar
    // is never blank during the async load, and apps that wire auth but never
    // call loadPermissions still work.
    useAdminAuthStore().setToken('t')
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('users')
    expect(names).toContain('secret')
  })

  it('menus: logged OUT (no token, no userInfo) does NOT fail-open - collapses to public entries', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(authRoutes)
    // No session at all (post-logout / never authenticated). Fail-open here is
    // what flashed EVERY menu for the 1-2s between clearing the store and the
    // login redirect, and let a freshly-switched role transiently see the full
    // menu. So the menu must collapse to only the public (no-permission) routes.
    const names = store.menus.map((m) => m.key)
    expect(names).not.toContain('users') // requires user.view
    expect(names).not.toContain('secret') // requires admin.super
    expect(names).toContain('public') // no requirement → still shown
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

  it('deniedRouteNames: empty before a user is loaded (deny nothing until permissions load)', () => {
    // deniedRouteNames only drives tab pruning for a logged-in privilege
    // downgrade; it intentionally denies nothing before the permission list
    // loads (unlike `menus`, which now collapses when logged out). On logout we
    // redirect away, so there is nothing to prune.
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
          { name: 'system.dictionaries', path: 'dictionaries', meta: { title: 'Dictionaries', permission: 'system.dictionary.view' } },
          { name: 'system.diagnostics', path: 'diagnostics', meta: { title: 'Diag', permission: 'system.diagnostics.view' } },
        ],
      },
    ])
    login(['system.view', 'system.dictionary.view']) // has the module + one child, not diagnostics
    const denied = store.deniedRouteNames
    expect(denied.has('system')).toBe(false)
    expect(denied.has('system.dictionaries')).toBe(false)
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

  // ── module-availability gating (meta.moduleGate + setAvailableModules) ──────
  const gatedRoutes: AdminRouteRecord[] = [
    { name: 'dashboard', path: '/dashboard', meta: { title: 'Dashboard', order: 0 } }, // no moduleGate → never gated
    {
      name: 'finance',
      path: '/finance',
      meta: { title: 'Finance', order: 1, moduleGate: true },
      children: [
        { name: 'finance.accounts', path: 'accounts', meta: { title: 'Accounts', permission: 'finance.account.view' } },
      ],
    },
    {
      name: 'identity',
      path: '/identity',
      meta: { title: 'Identity', order: 2, moduleGate: true },
      children: [
        { name: 'identity.users', path: 'users', meta: { title: 'Users', permission: 'user.view' } },
      ],
    },
  ]

  it('module gate: null signal is fail-open (gated modules stay visible)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    // availableModules never set → null → no gating
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('finance')
    expect(names).toContain('identity')
  })

  it('module gate: hides a gated module the backend did NOT load', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    store.setAvailableModules(new Set(['identity'])) // finance not loaded
    const names = store.menus.map((m) => m.key)
    expect(names).not.toContain('finance')
    expect(names).toContain('identity')
    expect(names).toContain('dashboard') // no moduleGate → never gated
  })

  it('module gate: holds for SUPER USERS too (orthogonal to permissions)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true) // super user - bypasses permission filter, NOT the module gate
    store.setAvailableModules(new Set(['identity']))
    expect(store.menus.map((m) => m.key)).not.toContain('finance')
  })

  it('module gate: never gates a node without meta.moduleGate', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    store.setAvailableModules(new Set([])) // nothing loaded
    const names = store.menus.map((m) => m.key)
    expect(names).toContain('dashboard') // survives even an empty signal
    expect(names).not.toContain('finance')
    expect(names).not.toContain('identity')
  })

  it('module gate: reacts to setAvailableModules', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    expect(store.menus.map((m) => m.key)).toContain('finance')
    store.setAvailableModules(new Set(['identity']))
    expect(store.menus.map((m) => m.key)).not.toContain('finance')
    store.setAvailableModules(null) // back to fail-open
    expect(store.menus.map((m) => m.key)).toContain('finance')
  })

  it('module gate: string moduleGate matches an explicit short name (normalized)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      { name: 'blog', path: '/blog', meta: { title: 'Blog', order: 1, moduleGate: 'Shop.Blog' } },
    ])
    login([], true)
    store.setAvailableModules(new Set(['identity'])) // no shop-blog
    expect(store.menus.map((m) => m.key)).not.toContain('blog')
    store.setAvailableModules(new Set(['shop-blog'])) // 'Shop.Blog' normalizes to 'shop-blog'
    expect(store.menus.map((m) => m.key)).toContain('blog')
  })

  it('unavailableRouteNames: empty when the signal is unavailable (fail-open)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    expect(store.unavailableRouteNames.size).toBe(0)
  })

  it('unavailableRouteNames: collects a gated module + all descendants', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    store.setAvailableModules(new Set(['identity']))
    const denied = store.unavailableRouteNames
    expect(denied.has('finance')).toBe(true)
    expect(denied.has('finance.accounts')).toBe(true)
    expect(denied.has('identity')).toBe(false)
    expect(denied.has('identity.users')).toBe(false)
    expect(denied.has('dashboard')).toBe(false)
  })

  it('unavailableRouteNames: holds for super users (orthogonal to permissions)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    login([], true)
    store.setAvailableModules(new Set([]))
    expect(store.unavailableRouteNames.has('finance')).toBe(true)
    expect(store.unavailableRouteNames.has('identity')).toBe(true)
  })

  it('clearRoutes resets the module signal to fail-open (null)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(gatedRoutes)
    store.setAvailableModules(new Set(['identity']))
    expect(store.availableModules).not.toBeNull()
    store.clearRoutes()
    expect(store.availableModules).toBeNull()
  })

  // ── Built-in-menus toggle ─────────────────────────────────────────────
  // Display-only, top-level only: with the toggle OFF (super admin), groups
  // stamped `meta.builtIn` hide except neutral ones (no permission anywhere
  // in the subtree - the landing dashboard); consumer routes (unstamped)
  // always stay. Never touches guards/tabs/reachability.

  const builtInRoutes: AdminRouteRecord[] = [
    { name: 'dashboard', path: '/dashboard', meta: { title: 'Dashboard', order: 0, builtIn: true } },
    {
      name: 'identity',
      path: '/identity',
      meta: { title: 'Identity', order: 1, builtIn: true },
      children: [
        { name: 'identity.users', path: 'users', meta: { title: 'Users', permission: 'user.view' } },
      ],
    },
    {
      name: 'blog',
      path: '/blog',
      meta: { title: 'Blog', order: 2 },
      children: [
        { name: 'blog.posts', path: 'posts', meta: { title: 'Posts', permission: 'shop.blog.post.view' } },
      ],
    },
  ]

  function loginSuper(): void {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'u1', username: 'u', roles: [], permissions: [] })
    auth.setSuperUser(true)
  }

  it('built-in toggle OFF: hides built-in groups, keeps consumer + neutral routes', async () => {
    const { useAdminAppStore } = await import('../../src/stores/useAdminAppStore')
    const store = useAdminRouteStore()
    store.setAuthRoutes(builtInRoutes)
    loginSuper()
    useAdminAppStore().setShowBuiltInMenus(false)
    const names = store.menus.map((m) => m.key)
    expect(names).not.toContain('identity') // built-in with permission leaves → hidden
    expect(names).toContain('blog') // consumer route (unstamped) → stays
    expect(names).toContain('dashboard') // built-in but neutral (no permission) → stays
  })

  it('built-in toggle defaults ON: full menu unchanged', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(builtInRoutes)
    loginSuper()
    const names = store.menus.map((m) => m.key)
    expect(names).toEqual(['dashboard', 'identity', 'blog'])
  })

  it('built-in toggle OFF never applies to non-super users (no cross-session residue)', async () => {
    const { useAdminAppStore } = await import('../../src/stores/useAdminAppStore')
    const store = useAdminRouteStore()
    store.setAuthRoutes(builtInRoutes)
    useAdminAppStore().setShowBuiltInMenus(false) // persisted OFF from a prior super session
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'u1', username: 'u', roles: [], permissions: ['user.view'] })
    expect(store.menus.map((m) => m.key)).toContain('identity')
  })
})

describe('useAdminRouteStore - role gating (meta.roles)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  // Consumer routes may declare `meta.roles` (ANY-of). Framework routes never
  // do, so role gating is zero-impact for apps that don't use it.
  const roleRoutes: AdminRouteRecord[] = [
    { name: 'staff', path: '/admin/staff', meta: { title: 'Staff', roles: ['Owner', 'Management'], order: 1 } },
    { name: 'files', path: '/admin/files', meta: { title: 'Files', order: 2 } }, // public (no roles)
    { name: 'both', path: '/admin/both', meta: { title: 'Both', permission: 'x.view', roles: ['Owner'], order: 3 } },
  ]

  function loginAs(roles: string[], permissions: string[] = [], superUser = false): void {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'u1', username: 'u', roles, permissions })
    if (superUser) auth.setSuperUser(true)
  }

  it('menus: hides a role-gated route when the user lacks all its roles', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(roleRoutes)
    loginAs(['Lawyer'])
    const names = store.menus.map((m) => m.key)
    expect(names).not.toContain('staff')
    expect(names).toContain('files') // public route unaffected
  })

  it('menus: shows a role-gated route with ANY-of the declared roles (case-insensitive)', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(roleRoutes)
    loginAs(['management']) // lowercase - must still match 'Management'
    expect(store.menus.map((m) => m.key)).toContain('staff')
  })

  it('menus: super-user bypasses the role gate', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(roleRoutes)
    loginAs([], [], true)
    expect(store.menus.map((m) => m.key)).toContain('staff')
  })

  it('menus: a route with BOTH permission and roles requires both', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(roleRoutes)
    loginAs(['Owner'], []) // has role, lacks x.view permission
    expect(store.menus.map((m) => m.key)).not.toContain('both')
    // grant the permission too → now visible
    useAdminAuthStore().setUserInfo({ id: 'u1', username: 'u', roles: ['Owner'], permissions: ['x.view'] })
    expect(store.menus.map((m) => m.key)).toContain('both')
  })

  it('deniedRouteNames: denies a role-gated route the user cannot hold', () => {
    const store = useAdminRouteStore()
    store.setAuthRoutes(roleRoutes)
    loginAs(['Lawyer'])
    expect(store.deniedRouteNames.has('staff')).toBe(true)
    expect(store.deniedRouteNames.has('files')).toBe(false)
  })
})
