import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { RouteLocationNormalized } from 'vue-router'
import { createAuthGuard, createModuleGuard, createPermissionGuard } from '../../src/router/guards'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'

function fakeRoute(
  path: string,
  meta: Record<string, unknown> = {},
  name?: string,
): RouteLocationNormalized {
  return {
    path,
    fullPath: path,
    name: name ?? (path.replace(/\//g, '') || 'home'),
    hash: '',
    query: {},
    params: {},
    meta,
    matched: [],
    redirectedFrom: undefined,
  } as unknown as RouteLocationNormalized
}

function loginAs(permissions: string[] = []) {
  const auth = useAdminAuthStore()
  auth.setToken('test-token')
  auth.setUserInfo({ id: '1', username: 'tester', roles: [], permissions })
}

describe('createAuthGuard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('redirects to an explicit loginPath when not logged in and requiresAuth !== false', async () => {
    const guard = createAuthGuard({ loginPath: '/login' }) as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith('/login')
  })

  it('redirects by route NAME when no loginPath is configured (prefix-agnostic default)', async () => {
    const guard = createAuthGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith({ name: 'login' })
  })

  it('allows navigation when logged in', async () => {
    loginAs()
    const guard = createAuthGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('allows navigation when requiresAuth is false even without login', async () => {
    const guard = createAuthGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/login', { requiresAuth: false }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('calls resolveSession when signed out and allows navigation if it resolves true', async () => {
    const resolveSession = vi.fn(async () => true)
    const guard = createAuthGuard({ resolveSession }) as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(resolveSession).toHaveBeenCalledTimes(1)
    expect(next).toHaveBeenCalledWith()
  })

  it('redirects to login when resolveSession resolves false (restore failed / no session)', async () => {
    const resolveSession = vi.fn(async () => false)
    const guard = createAuthGuard({ resolveSession }) as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(resolveSession).toHaveBeenCalledTimes(1)
    expect(next).toHaveBeenCalledWith({ name: 'login' })
  })

  it('always consults resolveSession when provided (token-authoritative, not short-circuited by persisted isLogin)', async () => {
    loginAs() // admin store shows isLogin, but the resolver is authoritative
    const resolveSession = vi.fn(async () => true)
    const guard = createAuthGuard({ resolveSession }) as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(resolveSession).toHaveBeenCalledTimes(1) // NOT skipped despite store isLogin
    expect(next).toHaveBeenCalledWith()
  })

  it('without a resolver falls back to the plain store isLogin check (legacy)', async () => {
    loginAs()
    const guard = createAuthGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })
})

describe('createPermissionGuard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('passes when route meta.permission is empty', async () => {
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('redirects to an explicit forbiddenPath when permission missing', async () => {
    loginAs([])
    const guard = createPermissionGuard({ forbiddenPath: '/403' }) as any
    const next = vi.fn()
    await guard(
      fakeRoute('/admin/users', { permission: 'user.view' }),
      fakeRoute('/'),
      next,
    )
    expect(next).toHaveBeenCalledWith('/403')
  })

  it('redirects by route NAME when no forbiddenPath is configured', async () => {
    loginAs([])
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(
      fakeRoute('/admin/users', { permission: 'user.view' }),
      fakeRoute('/'),
      next,
    )
    expect(next).toHaveBeenCalledWith({ name: 'forbidden' })
  })

  it('passes when user has required permission', async () => {
    loginAs(['user.view'])
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(
      fakeRoute('/admin/users', { permission: 'user.view' }),
      fakeRoute('/'),
      next,
    )
    expect(next).toHaveBeenCalledWith()
  })

  it('adds tab to tabStore on successful navigation', async () => {
    const auth = useAdminAuthStore()
    auth.isSuperUser = true
    const tabStore = useAdminTabStore()
    const spy = vi.spyOn(tabStore, 'addTab')
    const guard = createPermissionGuard() as any
    await guard(
      fakeRoute('/admin/users', { title: 'Users' }, 'admin.users'),
      fakeRoute('/'),
      vi.fn(),
    )
    expect(spy).toHaveBeenCalled()
  })

  it('redirects to forbidden when a role-gated route requires a role the user lacks', async () => {
    const auth = useAdminAuthStore()
    auth.setToken('t')
    auth.setUserInfo({ id: '1', username: 'u', roles: ['Lawyer'], permissions: [] })
    const guard = createPermissionGuard({ forbiddenPath: '/403' }) as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/staff', { roles: ['Owner', 'Management'] }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith('/403')
  })

  it('passes a role-gated route when the user holds one of the roles', async () => {
    const auth = useAdminAuthStore()
    auth.setToken('t')
    auth.setUserInfo({ id: '1', username: 'u', roles: ['Management'], permissions: [] })
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/staff', { roles: ['Owner', 'Management'] }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('role gate is case-insensitive (backend role casing may differ from meta.roles)', async () => {
    const auth = useAdminAuthStore()
    auth.setToken('t')
    auth.setUserInfo({ id: '1', username: 'u', roles: ['owner'], permissions: [] }) // lowercase from backend
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/staff', { roles: ['Owner'] }), fakeRoute('/'), next) // PascalCase meta
    expect(next).toHaveBeenCalledWith() // passes — mirrors the case-insensitive sidebar filter, no phantom 403
  })

  it('super-user bypasses the role gate', async () => {
    const auth = useAdminAuthStore()
    auth.setToken('t')
    auth.setUserInfo({ id: '1', username: 'u', roles: [], permissions: [] })
    auth.setSuperUser(true)
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/staff', { roles: ['Owner'] }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('fails open on a role-gated route while permissions are still loading (userInfo null)', async () => {
    const auth = useAdminAuthStore()
    auth.setToken('t') // token set, userInfo not yet loaded → fail-open
    const guard = createPermissionGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/staff', { roles: ['Owner'] }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })
})

describe('createModuleGuard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  function seed(available: Set<string> | null) {
    const store = useAdminRouteStore()
    store.setAuthRoutes([
      {
        name: 'finance',
        path: '/finance',
        meta: { title: 'Finance', moduleGate: true },
        children: [{ name: 'finance.accounts', path: 'accounts', meta: { title: 'Accounts' } }],
      },
      { name: 'identity', path: '/identity', meta: { title: 'Identity', moduleGate: true } },
    ])
    store.setAvailableModules(available)
  }

  it('passes when the module signal is unavailable (fail-open)', async () => {
    seed(null)
    const guard = createModuleGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/finance', {}, 'finance'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('bounces navigation into an unloaded module to the forbidden route', async () => {
    seed(new Set(['identity'])) // finance not loaded
    const guard = createModuleGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/finance', {}, 'finance'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith({ name: 'forbidden' })
  })

  it('bounces a descendant of an unloaded module too', async () => {
    seed(new Set(['identity']))
    const guard = createModuleGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/finance/accounts', {}, 'finance.accounts'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith({ name: 'forbidden' })
  })

  it('redirects to an explicit forbiddenPath when configured', async () => {
    seed(new Set(['identity']))
    const guard = createModuleGuard({ forbiddenPath: '/403' }) as any
    const next = vi.fn()
    await guard(fakeRoute('/finance', {}, 'finance'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith('/403')
  })

  it('passes navigation into a loaded module', async () => {
    seed(new Set(['identity', 'finance']))
    const guard = createModuleGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/finance', {}, 'finance'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith()
  })

  it('holds for super users (module gate is orthogonal to permissions)', async () => {
    const auth = useAdminAuthStore()
    auth.isSuperUser = true
    seed(new Set(['identity']))
    const guard = createModuleGuard() as any
    const next = vi.fn()
    await guard(fakeRoute('/finance', {}, 'finance'), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith({ name: 'forbidden' })
  })
})
