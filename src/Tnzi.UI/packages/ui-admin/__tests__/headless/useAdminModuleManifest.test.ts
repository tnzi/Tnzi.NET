import { describe, it, expect } from 'vitest'
import { useAdminModuleManifest } from '../../src/headless/useAdminModuleManifest'
import type { AdminManifest } from '../../src/services/admin-manifest'

function manifestFixture(): AdminManifest {
  return {
    modules: [
      {
        name: 'Identity',
        fullName: 'Tnzi.Identity',
        assembly: 'Tnzi.Identity',
        isEnabled: true,
        entities: [
          {
            name: 'users',
            route: 'admin/users',
            methods: ['GET', 'POST', 'PUT', 'DELETE'],
            hasFullCrud: true,
            isDefault: true,
            controllerType: 'A',
          },
          {
            name: 'roles',
            route: 'admin/roles',
            methods: ['GET', 'POST', 'PUT', 'DELETE'],
            hasFullCrud: true,
            isDefault: true,
            controllerType: 'B',
          },
        ],
      },
      {
        name: 'Payment',
        fullName: 'Tnzi.Payment',
        assembly: 'Tnzi.Payment',
        isEnabled: true,
        entities: [
          {
            name: 'orders',
            route: 'admin/payment-orders',
            methods: ['GET'],
            hasFullCrud: false,
            isDefault: true,
            controllerType: 'C',
          },
        ],
      },
      {
        name: 'AI.Skills',
        fullName: 'Tnzi.AI.Skills',
        assembly: 'Tnzi.AI.Skills',
        isEnabled: false,
        entities: [
          {
            name: 'skills',
            route: 'admin/skills',
            methods: ['GET'],
            hasFullCrud: false,
            isDefault: true,
            controllerType: 'D',
          },
        ],
      },
    ],
  }
}

const dummyClient = { get: async () => ({ success: true, code: 200, data: null }) }

describe('useAdminModuleManifest', () => {
  it('exposes the manifest verbatim when provided directly', () => {
    const m = manifestFixture()
    const { manifest, isAvailable } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: m,
    })
    expect(manifest.value).toEqual(m)
    expect(isAvailable.value).toBe(true)
  })

  it('filters out disabled modules', () => {
    const { modules } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: manifestFixture(),
    })
    expect(modules.value.map((m) => m.name)).toEqual(['Identity', 'Payment'])
  })

  it('honors hideModules (case-insensitive, dot-normalized)', () => {
    const { modules } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: manifestFixture(),
      hideModules: ['payment'],
    })
    expect(modules.value.map((m) => m.name)).toEqual(['Identity'])
  })

  it('honors showOnlyModules (whitelist mode)', () => {
    const { modules } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: manifestFixture(),
      showOnlyModules: ['identity'],
    })
    expect(modules.value.map((m) => m.name)).toEqual(['Identity'])
  })

  it('builds a 2-level menu tree when a module has multiple entities', () => {
    const { menuTree } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: manifestFixture(),
    })
    const identity = menuTree.value.find((n) => n.key === 'identity')
    expect(identity).toBeTruthy()
    expect(identity!.children).toBeTruthy()
    expect(identity!.children!.map((c) => c.key)).toEqual([
      'identity/users',
      'identity/roles',
    ])
    expect(identity!.children![0]!.path).toBe('/admin/users')
  })

  it('collapses single-entity modules to a top-level leaf', () => {
    const { menuTree } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: manifestFixture(),
    })
    const payment = menuTree.value.find((n) => n.key.startsWith('payment'))
    expect(payment).toBeTruthy()
    expect(payment!.children).toBeUndefined()
    expect(payment!.path).toBe('/admin/payment-orders')
  })

  it('generates i18n keys following the tnzi.admin.modules.* convention', () => {
    const { menuTree } = useAdminModuleManifest({
      client: dummyClient as never,
      manifest: manifestFixture(),
    })
    const identity = menuTree.value.find((n) => n.key === 'identity')!
    expect(identity.i18nKey).toBe('tnzi.admin.modules.identity.label')
    expect(identity.children![0]!.i18nKey).toBe(
      'tnzi.admin.modules.identity.users.title',
    )
  })

  it('refresh sets manifest from client when no manifest prop given', async () => {
    const m = manifestFixture()
    const client = {
      get: async (_url: string) => ({ success: true, code: 200, data: m }),
    }
    const { manifest, refresh, isAvailable } = useAdminModuleManifest({
      client: client as never,
    })
    await refresh()
    expect(manifest.value).toEqual(m)
    expect(isAvailable.value).toBe(true)
  })

  it('isAvailable is false when client returns nothing', async () => {
    const client = {
      get: async () => ({ success: false, code: 500, data: null }),
    }
    const { isAvailable, refresh } = useAdminModuleManifest({
      client: client as never,
    })
    await refresh()
    expect(isAvailable.value).toBe(false)
  })
})
