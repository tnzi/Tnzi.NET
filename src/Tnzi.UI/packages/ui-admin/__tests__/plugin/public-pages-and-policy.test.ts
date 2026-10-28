/**
 * Two deployment choices an app makes once, in `defineAdminApp`:
 *
 * - `hidePublicPages`: an app whose recipients open shared files, unsubscribe
 *   links or invitations in another front end leaves those pages out. They
 *   used to be registered unconditionally, and none of `hideRoutes`,
 *   `overridePages` or the module filters reached them (they sit outside the
 *   shell), so the app carried screens it did not want at addresses it did not
 *   serve.
 * - `passwordPolicy`: the built-in register / reset / invitation forms check a
 *   new password against the policy the server is configured with, not only
 *   against the server's defaults.
 */
import { describe, it, expect, beforeEach } from 'vitest'
import { createApp, defineComponent, h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import type { Router } from 'vue-router'
import { useFormRules } from '@tnzi/ui'
import { defineAdminApp } from '../../src/plugin/defineAdminApp'
import { removePublicPages, PUBLIC_PAGE_ROUTE_NAMES } from '../../src/plugin/route-transforms'
import { defaultAdminRoutes } from '../../src/router/routes'

const dummyClient = {
  get: async () => ({ success: true, code: 200, data: null }),
  post: async () => ({ success: true, code: 200, data: null }),
  addUnauthorizedListener: () => () => {},
} as never

const topLevelNames = (routes: { name?: unknown }[]) =>
  routes.map((r) => (typeof r.name === 'string' ? r.name : '')).filter(Boolean)

describe('hidePublicPages', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('ships all three public pages by default', () => {
    const { routes } = defineAdminApp({ client: dummyClient })
    expect(topLevelNames(routes)).toEqual(expect.arrayContaining([...PUBLIC_PAGE_ROUTE_NAMES]))
  })

  it('leaves the named pages out of the mounted table and keeps the rest', () => {
    const { routes } = defineAdminApp({ client: dummyClient, hidePublicPages: ['accept-invitation', 'unsubscribe'] })
    const names = topLevelNames(routes)
    expect(names).not.toContain('accept-invitation')
    expect(names).not.toContain('unsubscribe')
    expect(names).toContain('share-link')
    expect(names).toContain('login')
    expect(names).toContain('admin-root')
  })

  it('cannot remove anything but a public page', () => {
    const kept = removePublicPages(defaultAdminRoutes, ['login', 'admin-root'] as never)
    expect(kept).toHaveLength(defaultAdminRoutes.length)
  })
})

describe('passwordPolicy', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('reaches every password() rule built in the app', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    const router = { beforeEach: () => {}, afterEach: () => {}, onError: () => {} } as unknown as Router
    defineAdminApp({ client: dummyClient, moduleGating: false, passwordPolicy: { requireUppercase: true } }).install(app, pinia, router)

    const rule = app.runWithContext(() => useFormRules().rules.password())[1]!
    const validate = rule.validator as (r: unknown, v: string) => true | Error
    expect(validate({}, 'admin123')).toBeInstanceOf(Error)
    expect(validate({}, 'Admin123')).toBe(true)
  })

  it('without it the server defaults apply', () => {
    const app = createApp(defineComponent({ render: () => h('div') }))
    const router = { beforeEach: () => {}, afterEach: () => {}, onError: () => {} } as unknown as Router
    defineAdminApp({ client: dummyClient, moduleGating: false }).install(app, createPinia(), router)

    const rule = app.runWithContext(() => useFormRules().rules.password())[1]!
    const validate = rule.validator as (r: unknown, v: string) => true | Error
    expect(validate({}, 'admin123')).toBe(true)
    expect(validate({}, 'ABCDEF12')).toBeInstanceOf(Error)
  })
})
