import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createApp, h, defineComponent } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import type { RouteRecordRaw, Router } from 'vue-router'
import { defineAdminApp } from '../../src/plugin/defineAdminApp'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'

const dummyClient = {
  get: async () => ({ success: true, code: 200, data: null }),
  post: async () => ({ success: true, code: 200, data: null }),
} as never

function findAdminRoute(routes: RouteRecordRaw[]): RouteRecordRaw | undefined {
  return routes.find((r) => r.path === '/admin')
}

function namesOfChildren(route: RouteRecordRaw | undefined): string[] {
  return (route?.children ?? [])
    .map((c) => (typeof c.name === 'string' ? c.name : ''))
    .filter(Boolean)
}

describe('defineAdminApp', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('returns the full default route tree by default', () => {
    const { routes } = defineAdminApp({ client: dummyClient })
    const admin = findAdminRoute(routes)
    expect(admin).toBeTruthy()
    const childNames = namesOfChildren(admin)
    // Sanity: at minimum identity/authorization/system/storage are in the preset
    expect(childNames).toContain('identity')
    expect(childNames).toContain('authorization')
    expect(childNames).toContain('system')
  })

  it('hideModules removes the named module subtree from /admin', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment', 'audit'],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    expect(childNames).not.toContain('payment')
    expect(childNames).not.toContain('audit')
    expect(childNames).toContain('identity')
  })

  it('showOnlyModules keeps only the whitelisted modules', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      showOnlyModules: ['identity'],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    expect(childNames).toEqual(['identity'])
  })

  it('overridePages swaps the component for a named route deep in the tree', () => {
    const customPage = defineComponent({
      name: 'CustomUserPage',
      render: () => h('div', 'custom'),
    })
    const { routes } = defineAdminApp({
      client: dummyClient,
      overridePages: { 'identity.users': customPage },
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    const users = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.users',
    )
    expect(users).toBeTruthy()
    expect(users!.component).toBe(customPage)
  })

  it('addModules appends consumer-supplied routes under /admin', () => {
    const customRoute: RouteRecordRaw = {
      path: 'reports',
      name: 'reports',
      component: defineComponent({ render: () => h('div', 'reports') }),
      meta: { title: 'Reports' },
    }
    const { routes } = defineAdminApp({
      client: dummyClient,
      addModules: [customRoute],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    expect(childNames).toContain('reports')
  })

  it('loginComponent replaces the default /login route component', () => {
    const customLogin = defineComponent({
      name: 'CustomLogin',
      render: () => h('div', 'login'),
    })
    const { routes } = defineAdminApp({
      client: dummyClient,
      loginComponent: customLogin,
    })
    // Phase I.7.1: the default login route path is now
    // `/login/:module(pwd-login|...)?`. Match by name to stay path-shape neutral.
    const login = routes.find((r) => r.name === 'login')
    expect(login).toBeTruthy()
    expect(login!.component).toBe(customLogin)
  })

  it('forbiddenComponent replaces the placeholder /403 component', () => {
    const customForbidden = defineComponent({
      name: 'CustomForbidden',
      render: () => h('div', 'forbidden'),
    })
    const { routes } = defineAdminApp({
      client: dummyClient,
      forbiddenComponent: customForbidden,
    })
    const forbidden = routes.find((r) => r.path === '/403')
    expect(forbidden!.component).toBe(customForbidden)
  })

  it('install seeds the admin route store from the filtered routes', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)

    const { install } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
    })
    install(app, pinia)

    const routeStore = useAdminRouteStore()
    expect(routeStore.authRoutes.length).toBeGreaterThan(0)
    const names = routeStore.authRoutes.map((r) => r.name)
    expect(names).toContain('identity')
    expect(names).not.toContain('payment')
  })

  it('install returns a TnziUiAdminInstance with an uninstall hook', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)

    const { install } = defineAdminApp({ client: dummyClient })
    const instance = install(app, pinia)
    expect(typeof instance.uninstall).toBe('function')
    expect(() => instance.uninstall()).not.toThrow()
  })

  it('installs auth + permission guards only when auth.enabled (open by default)', () => {
    const beforeEachCount = (auth?: { enabled: boolean }): number => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)
      const router = { beforeEach: vi.fn(), afterEach: vi.fn(), onError: vi.fn() } as unknown as Router
      defineAdminApp({ client: dummyClient, ...(auth ? { auth } : {}) }).install(app, pinia, router)
      return (router.beforeEach as ReturnType<typeof vi.fn>).mock.calls.length
    }
    // Delta-based so the assertion is robust to however many beforeEach hooks
    // useRouteProgress installs: enabling auth adds exactly 2 (auth + permission).
    expect(beforeEachCount({ enabled: true }) - beforeEachCount()).toBe(2)
  })

  it('auth guard redirects to the REAL login route under the default basePath (/login, not /admin/login)', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const guards: Array<(to: unknown, from: unknown, next: (arg?: unknown) => void) => void> = []
    const router = {
      beforeEach: vi.fn((g) => guards.push(g)),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    // default basePath ('/admin') — applyBasePath leaves login at '/login'.
    defineAdminApp({ client: dummyClient, auth: { enabled: true } }).install(app, pinia, router)
    // Fresh store = not logged in → the auth guard must redirect to the real
    // route. Regression guard for the basePath default-prefix bug.
    const redirects: unknown[] = []
    const to = { meta: {}, name: 'x', path: '/admin/x', fullPath: '/admin/x', query: {}, params: {} }
    for (const g of guards) g(to, {}, (arg?: unknown) => { if (arg) redirects.push(arg) })
    expect(redirects).toContain('/login')
    expect(redirects).not.toContain('/admin/login')
  })

  it('hideRoutes marks the matching sub-menu meta.hideInMenu without touching others', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideRoutes: ['identity.tenants'],
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    expect(identity).toBeTruthy()
    const tenants = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.tenants',
    )
    const users = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.users',
    )
    expect(tenants).toBeTruthy()
    expect(users).toBeTruthy()
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
    // Sibling sub-menus are untouched.
    expect((users!.meta as Record<string, unknown>).hideInMenu).toBeFalsy()
  })

  it('hideRoutes drops the matched entry from the rendered menu tree', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)

    const { install } = defineAdminApp({
      client: dummyClient,
      hideRoutes: ['identity.tenants'],
    })
    install(app, pinia)

    const routeStore = useAdminRouteStore()
    const identityMenu = routeStore.menus.find((m) => m.key === 'identity')
    expect(identityMenu).toBeTruthy()
    const childKeys = (identityMenu!.children ?? []).map((c) => c.key)
    expect(childKeys).not.toContain('identity.tenants')
    // Sibling entries still render — we only hide the targeted route.
    expect(childKeys).toContain('identity.users')
  })

  it('hideRoutes is case-sensitive on exact route.name match', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      // Wrong case — should NOT hide anything.
      hideRoutes: ['Identity.Tenants'],
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    const tenants = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.tenants',
    )
    expect(tenants).toBeTruthy()
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBeFalsy()
  })

  it('hideRoutes works alongside hideModules without conflict', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
      hideRoutes: ['identity.tenants', 'system.signalr'],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    // hideModules still strips the module entirely.
    expect(childNames).not.toContain('payment')
    // hideRoutes targets survive at the route table level.
    expect(childNames).toContain('identity')
    expect(childNames).toContain('system')
    const identity = admin?.children?.find((c) => c.name === 'identity')
    const system = admin?.children?.find((c) => c.name === 'system')
    const tenants = identity?.children?.find((c) => c.name === 'identity.tenants')
    const signalr = system?.children?.find((c) => c.name === 'system.signalr')
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
    expect((signalr!.meta as Record<string, unknown>).hideInMenu).toBe(true)
  })

  it('framework built-in routes ship with default meta.order', () => {
    const { routes } = defineAdminApp({ client: dummyClient })
    const admin = findAdminRoute(routes)
    const childByName = (name: string) =>
      admin?.children?.find((c) => typeof c.name === 'string' && c.name === name)
    const metaOrder = (name: string): number | undefined => {
      const route = childByName(name)
      return (route?.meta as Record<string, unknown> | undefined)?.order as number | undefined
    }
    expect(metaOrder('dashboard')).toBe(0)
    expect(metaOrder('identity')).toBe(100)
    expect(metaOrder('authorization')).toBe(110)
    expect(metaOrder('system')).toBe(120)
    expect(metaOrder('audit')).toBe(130)
    expect(metaOrder('chat')).toBe(140)
    expect(metaOrder('ai')).toBe(150)
    expect(metaOrder('storage')).toBe(160)
    expect(metaOrder('notification')).toBe(170)
    expect(metaOrder('payment')).toBe(180)
    expect(metaOrder('template')).toBe(190)
  })

  it('routeOrders overrides the default order of a framework route', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      routeOrders: { dashboard: 5 },
    })
    const admin = findAdminRoute(routes)
    const dashboard = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'dashboard',
    )
    expect(dashboard).toBeTruthy()
    expect((dashboard!.meta as Record<string, unknown>).order).toBe(5)
  })

  it('routeOrders does not affect routes not listed in it', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      routeOrders: { dashboard: 5 },
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    expect(identity).toBeTruthy()
    // identity stays at its framework default (100).
    expect((identity!.meta as Record<string, unknown>).order).toBe(100)
  })

  it('routeOrders works alongside hideRoutes and hideModules', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
      hideRoutes: ['identity.tenants'],
      routeOrders: { dashboard: 5, authorization: 95 },
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    // hideModules still strips payment.
    expect(childNames).not.toContain('payment')
    // hideRoutes still flips meta.hideInMenu on the target.
    const identity = admin?.children?.find((c) => c.name === 'identity')
    const tenants = identity?.children?.find((c) => c.name === 'identity.tenants')
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
    // routeOrders applies its overrides to the same tree.
    const dashboard = admin?.children?.find((c) => c.name === 'dashboard')
    const authorization = admin?.children?.find((c) => c.name === 'authorization')
    expect((dashboard!.meta as Record<string, unknown>).order).toBe(5)
    expect((authorization!.meta as Record<string, unknown>).order).toBe(95)
    // Untouched framework default remains intact.
    expect((identity!.meta as Record<string, unknown>).order).toBe(100)
  })

  it('normalizes module names case-insensitively for hideModules', () => {
    const { routes: hideLower } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
    })
    const { routes: hideUpper } = defineAdminApp({
      client: dummyClient,
      hideModules: ['PAYMENT'],
    })
    expect(namesOfChildren(findAdminRoute(hideLower))).toEqual(
      namesOfChildren(findAdminRoute(hideUpper)),
    )
  })

  describe('basePath', () => {
    function findByName(routes: RouteRecordRaw[], name: string): RouteRecordRaw | undefined {
      return routes.find((r) => r.name === name)
    }

    it('defaults to /admin so existing consumers keep working', () => {
      const { routes } = defineAdminApp({ client: dummyClient })
      const adminRoot = findByName(routes, 'admin-root')
      const login = findByName(routes, 'login')
      const forbidden = findByName(routes, 'forbidden')
      expect(adminRoot?.path).toBe('/admin')
      // Login keeps its path-param shape and is NOT prefixed under the default.
      expect(login?.path).toBe(
        '/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat|two-factor)?',
      )
      expect(forbidden?.path).toBe('/403')
    })

    it('basePath="/console" prefixes admin-root and login', () => {
      const { routes } = defineAdminApp({ client: dummyClient, basePath: '/console' })
      const adminRoot = findByName(routes, 'admin-root')
      const login = findByName(routes, 'login')
      const forbidden = findByName(routes, 'forbidden')
      expect(adminRoot?.path).toBe('/console')
      expect(typeof login?.path).toBe('string')
      expect(login?.path as string).toContain('/console/login')
      // The original path-param shape is preserved after the prefix.
      expect((login?.path as string).startsWith('/console/login/:module(')).toBe(true)
      expect(forbidden?.path).toBe('/console/403')
    })

    it('basePath="/" deploys at domain root without producing //login', () => {
      const { routes } = defineAdminApp({ client: dummyClient, basePath: '/' })
      const adminRoot = findByName(routes, 'admin-root')
      const login = findByName(routes, 'login')
      const forbidden = findByName(routes, 'forbidden')
      expect(adminRoot?.path).toBe('/')
      // Login at root must keep its original shape — no leading "//".
      expect(login?.path).toBe(
        '/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat|two-factor)?',
      )
      expect((login?.path as string).startsWith('//')).toBe(false)
      expect(forbidden?.path).toBe('/403')
    })

    it('basePath normalizes input variants to /admin', () => {
      const variants = ['admin', '/admin', '/admin/', ' admin ', '']
      const reference = defineAdminApp({ client: dummyClient }).routes
      const refAdmin = findByName(reference, 'admin-root')?.path
      const refLogin = findByName(reference, 'login')?.path
      const refForbidden = findByName(reference, 'forbidden')?.path
      for (const v of variants) {
        const { routes } = defineAdminApp({ client: dummyClient, basePath: v })
        expect(findByName(routes, 'admin-root')?.path).toBe(refAdmin)
        expect(findByName(routes, 'login')?.path).toBe(refLogin)
        expect(findByName(routes, 'forbidden')?.path).toBe(refForbidden)
      }
    })

    it('basePath normalizes "/console/" → "/console"', () => {
      const a = defineAdminApp({ client: dummyClient, basePath: '/console/' }).routes
      const b = defineAdminApp({ client: dummyClient, basePath: 'console' }).routes
      expect(findByName(a, 'admin-root')?.path).toBe('/console')
      expect(findByName(b, 'admin-root')?.path).toBe('/console')
      expect(findByName(a, 'login')?.path).toBe(findByName(b, 'login')?.path)
    })

    it('basePath works alongside hideRoutes / hideModules / routeOrders', () => {
      const { routes } = defineAdminApp({
        client: dummyClient,
        basePath: '/console',
        hideModules: ['payment'],
        hideRoutes: ['identity.tenants'],
        routeOrders: { dashboard: 5, authorization: 95 },
      })
      const adminRoot = findByName(routes, 'admin-root')
      expect(adminRoot?.path).toBe('/console')
      const childNames = namesOfChildren(adminRoot)
      // hideModules still strips payment under the rewritten root.
      expect(childNames).not.toContain('payment')
      expect(childNames).toContain('identity')
      // hideRoutes flag still flips on a deep child.
      const identity = adminRoot?.children?.find((c) => c.name === 'identity')
      const tenants = identity?.children?.find((c) => c.name === 'identity.tenants')
      expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
      // routeOrders overrides land on the prefixed tree.
      const dashboard = adminRoot?.children?.find((c) => c.name === 'dashboard')
      const authorization = adminRoot?.children?.find((c) => c.name === 'authorization')
      expect((dashboard!.meta as Record<string, unknown>).order).toBe(5)
      expect((authorization!.meta as Record<string, unknown>).order).toBe(95)
    })

    it('install seeds the route store with paths under basePath', () => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)

      const { install } = defineAdminApp({
        client: dummyClient,
        basePath: '/console',
      })
      install(app, pinia)

      const routeStore = useAdminRouteStore()
      const identity = routeStore.authRoutes.find((r) => r.name === 'identity')
      expect(identity).toBeTruthy()
      // Top-level menu entry sits under /console.
      expect(identity!.path).toBe('/console/identity')
      const users = identity!.children?.find((c) => c.name === 'identity.users')
      expect(users?.path).toBe('/console/identity/users')
    })

    it('install seeds the route store with no prefix when basePath="/"', () => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)

      const { install } = defineAdminApp({
        client: dummyClient,
        basePath: '/',
      })
      install(app, pinia)

      const routeStore = useAdminRouteStore()
      const identity = routeStore.authRoutes.find((r) => r.name === 'identity')
      expect(identity?.path).toBe('/identity')
      const users = identity!.children?.find((c) => c.name === 'identity.users')
      expect(users?.path).toBe('/identity/users')
    })
  })
})
