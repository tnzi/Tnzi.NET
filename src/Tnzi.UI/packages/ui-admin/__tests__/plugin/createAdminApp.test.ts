import { describe, it, expect, beforeEach, vi } from 'vitest'
import { h } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import type { RouteRecordRaw } from 'vue-router'
import { createAdminApp } from '../../src/plugin/createAdminApp'

const dummyClient = {
  get: async () => ({ success: true, code: 200, data: null }),
  post: async () => ({ success: true, code: 200, data: null }),
  addUnauthorizedListener: () => () => {},
  getAccessToken: () => null,
} as never

const RootStub = { render: () => h('div', 'root') }

function pathsOf(routes: RouteRecordRaw[]): string[] {
  return routes.map((r) => (typeof r.path === 'string' ? r.path : ''))
}
function redirectOf(r: RouteRecordRaw): unknown {
  return (r as { redirect?: unknown }).redirect
}
/** A consumer public page: no admin shell, no auth - the mailed-link case. */
function publicRoute(path: string): RouteRecordRaw {
  return { path, component: RootStub, meta: { requiresAuth: false } } as RouteRecordRaw
}

describe('createAdminApp', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('assembles the router (root redirect + 404 catch-all) and returns app/pinia/router', () => {
    const handle = createAdminApp({ rootComponent: RootStub as never, client: dummyClient })
    expect(handle.app).toBeTruthy()
    expect(handle.pinia).toBeTruthy()
    expect(handle.router).toBeTruthy()
    const paths = pathsOf(handle.routes)
    expect(paths).toContain('/:pathMatch(.*)*') // catch-all added
    expect(handle.routes.some((r) => r.path === '/admin')).toBe(true) // preset still present
    // Root redirect to the admin root (default basePath '/admin').
    expect(handle.routes.some((r) => r.path === '/' && redirectOf(r) === '/admin')).toBe(true)
  })

  it('routes the catch-all to the not-found route by name (basePath-agnostic)', () => {
    const handle = createAdminApp({ rootComponent: RootStub as never, client: dummyClient })
    const catchAll = handle.routes.find((r) => r.path === '/:pathMatch(.*)*')
    expect(redirectOf(catchAll!)).toEqual({ name: 'not-found', params: {} })
  })

  // A redirect by name inherits the catch-all's `pathMatch`, which the 404 route
  // does not declare: vue-router then warns "Discarded invalid param(s)" in the
  // console of every app on every unknown URL.
  it('lands an unknown URL on the 404 page without a discarded-param warning', async () => {
    const spy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const handle = createAdminApp({ rootComponent: RootStub as never, client: dummyClient })
    await handle.router.push('/admin/no/such/page')
    expect(handle.router.currentRoute.value.name).toBe('not-found')
    expect(spy.mock.calls.filter((call) => String(call[0]).includes('Discarded invalid param'))).toEqual([])
    spy.mockRestore()
  })

  it('omits the added root redirect for domain-root deployment (basePath "/")', () => {
    const handle = createAdminApp({ rootComponent: RootStub as never, client: dummyClient, basePath: '/' })
    // The admin root is rewritten to '/', so no separate '/' → basePath redirect is added.
    expect(handle.routes.some((r) => r.path === '/' && redirectOf(r) === '/')).toBe(false)
  })

  it('appends consumer rootRoutes before the catch-all', () => {
    const handle = createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      rootRoutes: [publicRoute('/sign/:token')],
    })
    const paths = pathsOf(handle.routes)
    const signIdx = paths.indexOf('/admin/sign/:token')
    const catchIdx = paths.indexOf('/:pathMatch(.*)*')
    expect(signIdx).toBeGreaterThan(-1)
    expect(catchIdx).toBeGreaterThan(signIdx)
  })

  // ── The mailed-link regression ──────────────────────────────────────────
  // A consumer's public token page must resolve to the SAME browser URL under
  // both documented deployment shapes, from ONE prefix-free path. It used to
  // be appended after the basePath pass, so under the sub-path shape it sat
  // outside the app's own prefix - an address nothing could navigate to, which
  // the catch-all then swallowed. Build, typecheck and router-driven smokes all
  // pass on that; the only witness is a recipient clicking a dead link.
  it('prefixes consumer rootRoutes with basePath, like the framework public page', () => {
    const handle = createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      rootRoutes: [publicRoute('/forms/:token')],
    })
    const paths = pathsOf(handle.routes)
    expect(paths).toContain('/admin/forms/:token')
    expect(paths).not.toContain('/forms/:token')
    // Same rule the framework's own public route follows.
    expect(paths).toContain('/admin/share/:token')
    // ...and it really resolves there.
    expect(handle.router.resolve('/admin/forms/abc').matched.length).toBeGreaterThan(0)
  })

  it('leaves consumer rootRoutes prefix-free under basePath "/" (history-base shape)', () => {
    const handle = createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      basePath: '/',
      rootRoutes: [publicRoute('/forms/:token')],
    })
    // The deployment prefix comes from createWebHistory(base) here, so the
    // in-router path stays bare - the browser URL is /admin/forms/... either way.
    expect(pathsOf(handle.routes)).toContain('/forms/:token')
  })

  it('follows a custom basePath', () => {
    const handle = createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      basePath: '/console',
      rootRoutes: [publicRoute('/forms/:token')],
    })
    expect(pathsOf(handle.routes)).toContain('/console/forms/:token')
  })

  it('warns when a rootRoute hardcodes the deployment prefix', () => {
    const spy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      rootRoutes: [publicRoute('/admin/forms/:token')],
    })
    // Only this module's own warnings: a router warning from an earlier test's
    // navigation can still land while the spy is installed.
    const own = spy.mock.calls.filter((call) => String(call[0]).startsWith('[tnzi-admin]'))
    expect(own).toHaveLength(1)
    expect(String(own[0][0])).toContain('already carries basePath')
    spy.mockRestore()
  })

  it('does not warn for the correct prefix-free shape', () => {
    const spy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      rootRoutes: [publicRoute('/forms/:token')],
    })
    expect(spy.mock.calls.filter((call) => String(call[0]).startsWith('[tnzi-admin]'))).toEqual([])
    spy.mockRestore()
  })

  it('honours a custom notFoundRedirect target', () => {
    const handle = createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      notFoundRedirect: { name: 'dashboard' },
    })
    const catchAll = handle.routes.find((r) => r.path === '/:pathMatch(.*)*')
    // by name, so it drops the catch-all's inherited params like the default does
    expect(redirectOf(catchAll!)).toEqual({ name: 'dashboard', params: {} })
  })

  it('mounts the root component', () => {
    const handle = createAdminApp({ rootComponent: RootStub as never, client: dummyClient })
    const el = document.createElement('div')
    handle.mount(el)
    expect(el.textContent).toContain('root')
  })
})
