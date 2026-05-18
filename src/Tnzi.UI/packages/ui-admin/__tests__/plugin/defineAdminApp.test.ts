import { describe, it, expect, beforeEach } from 'vitest'
import { createApp, h, defineComponent } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import type { RouteRecordRaw } from 'vue-router'
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
})
