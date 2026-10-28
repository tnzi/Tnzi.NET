import { describe, it, expect } from 'vitest'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defaultAdminRoutes } from '../../src/router/routes'
import { applyBasePath, normalizeBasePath } from '../../src/plugin/route-transforms'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'

/**
 * The backend's `DefaultInvitationUrlGenerator` builds the invite link as
 * `{System:FrontendUrl}/admin/accept-invitation?token=...` when no
 * `Identity:Invitation:AcceptUrlTemplate` is configured. This page is that
 * contract's other half, and it has to be checked on the table an app actually
 * mounts: `defineAdminApp` prefixes every top-level route with the `basePath`
 * (default `/admin`). The earlier version of this test built a router from the
 * raw preset, so it stayed green while the mounted page lived at
 * `/admin/accept-invitation` and the emailed `/accept-invitation` link fell to
 * the 404 route.
 */
describe('accept-invitation route', () => {
  const mounted = (basePath?: string) =>
    createRouter({
      history: createMemoryHistory(),
      routes: applyBasePath(defaultAdminRoutes, normalizeBasePath(basePath)),
    })

  it('resolves the default invite link under the default basePath to the acceptance page', () => {
    const resolved = mounted().resolve('/admin/accept-invitation?token=abc')
    expect(resolved.name).toBe('accept-invitation')
    expect(resolved.query.token).toBe('abc')
    expect(mounted().resolve({ name: 'accept-invitation' }).path).toBe('/admin/accept-invitation')
  })

  it('sits at the root on a root basePath, where the backend needs AcceptUrlTemplate', () => {
    expect(mounted('/').resolve({ name: 'accept-invitation' }).path).toBe('/accept-invitation')
  })

  it('is reachable anonymously - the invitee has no account to sign in with yet', () => {
    const record = defaultAdminRoutes.find((r) => r.name === 'accept-invitation')
    expect(record?.meta?.requiresAuth).toBe(false)
  })
})

/**
 * Public recipient pages carry a locale key as their title, like every other
 * built-in route: a literal English string reads English under zh-cn.
 */
describe('public recipient route titles', () => {
  const lookup = (dict: unknown, key: string): unknown =>
    key
      .replace(/^tnzi\./, '')
      .split('.')
      .reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], dict)

  for (const name of ['accept-invitation', 'share-link', 'unsubscribe']) {
    it(`${name} title is a key present in both bundled dictionaries`, () => {
      const title = String(defaultAdminRoutes.find((r) => r.name === name)?.meta?.title ?? '')
      expect(title).toMatch(/^tnzi\.admin\./)
      expect(typeof lookup(en, title)).toBe('string')
      expect(typeof lookup(zhCn, title)).toBe('string')
      expect(lookup(zhCn, title)).not.toBe(lookup(en, title))
    })
  }
})
