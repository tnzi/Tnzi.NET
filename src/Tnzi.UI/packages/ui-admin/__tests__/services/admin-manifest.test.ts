import { describe, it, expect, vi } from 'vitest'
import { fetchAdminManifest, type AdminManifest } from '../../src/services/admin-manifest'

function makeManifest(): AdminManifest {
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
            controllerType: 'Tnzi.Identity.Controllers.Admin.DefaultUserAdminController',
          },
        ],
      },
    ],
  }
}

function mockClient(getImpl: (url: string) => unknown): { get: ReturnType<typeof vi.fn> } {
  return {
    get: vi.fn(async (url: string) => ({
      success: true,
      code: 200,
      data: getImpl(url),
    })),
  }
}

describe('fetchAdminManifest', () => {
  it('returns the manifest data when the endpoint responds 200', async () => {
    const manifest = makeManifest()
    const client = mockClient(() => manifest)
    const result = await fetchAdminManifest(client as never)
    expect(result).toEqual(manifest)
    expect(client.get).toHaveBeenCalledWith('/admin/diagnostics/admin-manifest')
  })

  it('returns null when the endpoint returns no data', async () => {
    const client = {
      get: vi.fn(async () => ({ success: false, code: 500, data: null })),
    }
    const result = await fetchAdminManifest(client as never)
    expect(result).toBeNull()
  })

  it('returns null when the modules field is missing', async () => {
    const client = {
      get: vi.fn(async () => ({ success: true, code: 200, data: { notModules: [] } })),
    }
    const result = await fetchAdminManifest(client as never)
    expect(result).toBeNull()
  })

  it('returns null when the HTTP call rejects', async () => {
    const client = {
      get: vi.fn(async () => {
        throw new Error('network')
      }),
    }
    const result = await fetchAdminManifest(client as never)
    expect(result).toBeNull()
  })
})
