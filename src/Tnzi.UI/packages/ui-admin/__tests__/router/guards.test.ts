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
