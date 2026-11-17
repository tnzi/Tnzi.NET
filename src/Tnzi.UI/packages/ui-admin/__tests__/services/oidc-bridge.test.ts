import { describe, it, expect, vi } from 'vitest'
import { createOidcBridge } from '../../src/services/bridges/oidc-bridge'

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data })
const refused = { succeeded: false, success: false, code: 400, message: 'Unknown scope(s): payments.' }

function mockApi(overrides: Record<string, unknown> = {}) {
  return {
    getClients: vi.fn(async () => ok({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    getClient: vi.fn(),
    createClient: vi.fn(async () => ok({ client: { id: 'c1' }, clientSecret: 's' })),
    updateClient: vi.fn(async () => ok({ id: 'c1' })),
    setClientEnabled: vi.fn(async () => ok(undefined)),
    rotateClientSecret: vi.fn(async () => ok({ client: { id: 'c1' }, clientSecret: 's2' })),
    deleteClient: vi.fn(async () => ok(undefined)),
    getGrants: vi.fn(async () => ok({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    revokeGrant: vi.fn(async () => ok(undefined)),
    ...overrides,
  }
}

describe('createOidcBridge', () => {
  it('★ a refused registration rejects instead of resolving to null', async () => {
    const bridge = createOidcBridge({ adminOidcApi: mockApi({ createClient: vi.fn(async () => refused) }) as never })
    await expect(bridge.clients.create({ displayName: 'x', redirectUris: [] })).rejects.toThrow(/payments/)
  })

  it('returns the one-time secret alongside the client', async () => {
    const bridge = createOidcBridge({ adminOidcApi: mockApi() as never })
    expect(await bridge.clients.create({ displayName: 'x', redirectUris: [] })).toEqual({ client: { id: 'c1' }, clientSecret: 's' })
  })

  it('maps the search box to keyword and keeps filters', async () => {
    const api = mockApi()
    await createOidcBridge({ adminOidcApi: api as never }).clients.fetch({ pageIndex: 2, pageSize: 10, searchText: 'app', filters: { isEnabled: true } } as never)
    expect(api.getClients).toHaveBeenCalledWith(expect.objectContaining({ pageIndex: 2, pageSize: 10, keyword: 'app', isEnabled: true }))
  })

  it('a refused enable/disable rejects', async () => {
    const bridge = createOidcBridge({ adminOidcApi: mockApi({ setClientEnabled: vi.fn(async () => refused) }) as never })
    await expect(bridge.clients.setEnabled('c1', false)).rejects.toThrow()
  })

  it('revokes grants one by one and refuses to create or edit them', async () => {
    const api = mockApi()
    const bridge = createOidcBridge({ adminOidcApi: api as never })
    await bridge.grants.delete(['g1', 'g2'])
    expect(api.revokeGrant).toHaveBeenCalledTimes(2)
    await expect(bridge.grants.create({} as never)).rejects.toThrow(/not supported/)
    await expect(bridge.grants.update('g1', {} as never)).rejects.toThrow(/not supported/)
  })

  it('rejects every call when no deps are provided', async () => {
    await expect(createOidcBridge().clients.fetch({ pageIndex: 1, pageSize: 20 } as never)).rejects.toThrow(/no deps/)
  })
})
