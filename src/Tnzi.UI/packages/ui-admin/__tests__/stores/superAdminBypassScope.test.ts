import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { defineAdminApp } from '../../src/plugin/defineAdminApp'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminRouteStore, type AdminRouteRecord } from '../../src/stores/useAdminRouteStore'
import { usePermissionGuard } from '../../src/headless/usePermissionGuard'
import { canAction } from '../../src/headless/permission-gates'
import { vPermission } from '../../src/directives/vPermission'

/**
 * `Authorization:SuperAdminBypassScope = Platform` on the front end: a super
 * admin's access-profile list is the whole truth, so every gate (menus, the
 * route guard's rule, `v-permission`, CRUD gates) filters on it like for any
 * other user. In the default `All` mode the super-user bypass is unchanged.
 */
describe('narrowed super-admin bypass', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
  })

  const routes: AdminRouteRecord[] = [
    { name: 'identity.users', path: '/admin/identity/users', meta: { title: 'Users', permission: 'user.view', order: 1 } },
    { name: 'app.matters', path: '/admin/app/matters', meta: { title: 'Matters', permission: 'app.matter.view', order: 2 } },
  ]

  function signInSuperAdmin(scope: 'All' | 'Platform', permissions: string[]): void {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'op', username: 'operator', roles: ['SuperAdmin'], permissions })
    auth.setSuperUser(true)
    auth.setSuperAdminBypassScope(scope)
  }

  function probe(value: string): HTMLElement {
    const el = document.createElement('div')
    document.body.appendChild(el)
    vPermission.mounted!(el, { value, modifiers: {} } as never, null as never, null as never)
    return el
  }

  it('bypassesPermissionChecks is true only for a super admin under the default scope', () => {
    const auth = useAdminAuthStore()
    expect(auth.superAdminBypassScope).toBe('All')
    expect(auth.bypassesPermissionChecks).toBe(false)

    auth.setSuperUser(true)
    expect(auth.bypassesPermissionChecks).toBe(true)

    auth.setSuperAdminBypassScope('Platform')
    expect(auth.bypassesPermissionChecks).toBe(false)
    expect(auth.isSuperUser).toBe(true)

    auth.logout()
    expect(auth.superAdminBypassScope).toBe('All')
  })

  it('default scope: the super admin still sees and reaches every route', () => {
    useAdminRouteStore().setAuthRoutes(routes)
    signInSuperAdmin('All', [])

    expect(useAdminRouteStore().menus.map((m) => m.key)).toEqual(['identity.users', 'app.matters'])
    expect(useAdminRouteStore().deniedRouteNames.size).toBe(0)
    expect(usePermissionGuard().canReachRoute({ permission: 'app.matter.view' })).toBe(true)
    expect(canAction('app.matter.update')).toBe(true)
    expect(probe('app.matter.update').style.display).toBe('')
  })

  it('platform scope: menus, route rule, CRUD gates and v-permission follow the access-profile list', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setAuthRoutes(routes)
    signInSuperAdmin('Platform', ['user.view'])

    expect(routeStore.menus.map((m) => m.key)).toEqual(['identity.users'])
    expect([...routeStore.deniedRouteNames]).toEqual(['app.matters'])

    const guard = usePermissionGuard()
    expect(guard.canReachRoute({ permission: 'user.view' })).toBe(true)
    expect(guard.canReachRoute({ permission: 'app.matter.view' })).toBe(false)
    expect(guard.can('app.matter.view')).toBe(false)

    expect(canAction('app.matter.update')).toBe(false)
    expect(canAction('user.view')).toBe(true)

    expect(probe('app.matter.update').style.display).toBe('none')
    expect(probe('user.view').style.display).toBe('')
  })

  it('platform scope: an explicit business grant opens the business route for the super admin', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setAuthRoutes(routes)
    signInSuperAdmin('Platform', ['user.view', 'app.matter.view'])

    expect(routeStore.menus.map((m) => m.key)).toEqual(['identity.users', 'app.matters'])
    expect(usePermissionGuard().canReachRoute({ permission: 'app.matter.view' })).toBe(true)
  })

  function profileClient(profile: Record<string, unknown> | null) {
    return {
      get: async (url: string) => {
        if (url.startsWith('/users/profile')) {
          return { success: true, code: 200, data: { id: 'op', userName: 'operator', roles: ['SuperAdmin'] } }
        }
        if (url.endsWith('/access-profile')) {
          return profile ? { success: true, code: 200, data: profile } : { success: false, code: 500, message: 'Database timeout' }
        }
        return { success: false, code: 404 }
      },
      post: async () => ({ success: true, code: 200, data: null }),
      addUnauthorizedListener: () => () => {},
    } as never
  }

  it('loadPermissions records the narrowed scope from the access profile', async () => {
    const { loadPermissions } = defineAdminApp({
      client: profileClient({ isSuperAdmin: true, permissions: ['user.view'], superAdminBypassScope: 'Platform' }),
    })
    await loadPermissions()

    const auth = useAdminAuthStore()
    expect(auth.isSuperUser).toBe(true)
    expect(auth.superAdminBypassScope).toBe('Platform')
    expect(auth.bypassesPermissionChecks).toBe(false)
  })

  it('loadPermissions treats an absent scope as the default bypass and overwrites a stale narrowed one', async () => {
    useAdminAuthStore().setSuperAdminBypassScope('Platform')
    const { loadPermissions } = defineAdminApp({
      client: profileClient({ isSuperAdmin: true, permissions: ['user.view'] }),
    })
    await loadPermissions()

    const auth = useAdminAuthStore()
    expect(auth.superAdminBypassScope).toBe('All')
    expect(auth.bypassesPermissionChecks).toBe(true)
  })

  it('a failed access-profile read keeps the same user\'s narrowed scope', async () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'op', username: 'operator', roles: ['SuperAdmin'], permissions: ['user.view'] })
    auth.setSuperUser(true)
    auth.setSuperAdminBypassScope('Platform')

    const { loadPermissions } = defineAdminApp({ client: profileClient(null) })
    await loadPermissions()

    expect(auth.superAdminBypassScope).toBe('Platform')
    expect(auth.bypassesPermissionChecks).toBe(false)
  })
})
