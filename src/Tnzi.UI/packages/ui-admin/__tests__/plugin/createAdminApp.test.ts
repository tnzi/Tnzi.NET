import { describe, it, expect, beforeEach } from 'vitest'
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
    expect(redirectOf(catchAll!)).toEqual({ name: 'not-found' })
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
      rootRoutes: [{ path: '/sign/:token', component: RootStub, meta: { requiresAuth: false } } as RouteRecordRaw],
    })
    const paths = pathsOf(handle.routes)
    const signIdx = paths.indexOf('/sign/:token')
    const catchIdx = paths.indexOf('/:pathMatch(.*)*')
    expect(signIdx).toBeGreaterThan(-1)
    expect(catchIdx).toBeGreaterThan(signIdx)
  })

  it('honours a custom notFoundRedirect target', () => {
    const handle = createAdminApp({
      rootComponent: RootStub as never,
      client: dummyClient,
      notFoundRedirect: { name: 'dashboard' },
    })
    const catchAll = handle.routes.find((r) => r.path === '/:pathMatch(.*)*')
    expect(redirectOf(catchAll!)).toEqual({ name: 'dashboard' })
  })

  it('mounts the root component', () => {
    const handle = createAdminApp({ rootComponent: RootStub as never, client: dummyClient })
    const el = document.createElement('div')
    handle.mount(el)
    expect(el.textContent).toContain('root')
  })
})
