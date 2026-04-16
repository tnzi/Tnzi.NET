import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { RouteLocationNormalized } from 'vue-router'
import { createAuthGuard, createPermissionGuard } from '../../src/router/guards'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'

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

  it('redirects to /login when not logged in and requiresAuth !== false', async () => {
    const guard = createAuthGuard({ loginPath: '/login' }) as any
    const next = vi.fn()
    await guard(fakeRoute('/admin/users', { requiresAuth: true }), fakeRoute('/'), next)
    expect(next).toHaveBeenCalledWith('/login')
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

  it('redirects to /403 when permission missing', async () => {
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
