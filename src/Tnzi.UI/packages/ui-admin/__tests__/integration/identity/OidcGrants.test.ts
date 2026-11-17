import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { grantPersonLabel } from '../../../src/pages/identity/oidc-client-config'

/**
 * OIDC grants page - one document row per grant. What matters: the person is
 * shown by name and email (the id only as a fallback and for support), revoke
 * is offered on active grants only, and the two query filters reach the bridge.
 */

const USER_ID = '6f1c2a9e-0000-4000-8000-000000000001'
const ORPHAN_ID = '6f1c2a9e-0000-4000-8000-000000000002'

const grantsFetch = vi.fn(async () => ({
  items: [
    {
      id: 'g1', userId: USER_ID, userDisplayName: 'Ada Lovelace', userEmail: 'ada@example.test',
      clientId: 'app-one', clientDisplayName: 'App One', scopes: ['openid', 'profile'], status: 'valid',
      creationDate: '2026-10-01T00:00:00Z',
    },
    {
      id: 'g2', userId: ORPHAN_ID, clientId: 'app-two', scopes: [], status: 'revoked',
      creationDate: '2026-10-02T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/oidc-bridge', () => ({
  createOidcBridge: () => ({
    clients: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), setEnabled: vi.fn(), rotateSecret: vi.fn() },
    grants: { fetch: grantsFetch, create: vi.fn(), update: vi.fn(), delete: vi.fn(async () => undefined) },
  }),
}))

type Vm = {
  rowActions: Array<{ key: string; show?: (row: unknown) => boolean }>
}

async function mountPage() {
  const { default: Page } = await import('../../../src/pages/identity/OidcGrants.vue')
  const wrapper = mount(Page)
  await flushPromises()
  return { wrapper, vm: wrapper.vm as unknown as Vm }
}

describe('OidcGrants page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('shows each grant as a row: application, person by name and email, scopes, status', async () => {
    const { wrapper } = await mountPage()
    const rows = wrapper.findAll('.t-item-card')
    expect(rows).toHaveLength(2)
    const first = rows[0]!.text()
    expect(first).toContain('App One')
    expect(first).toContain('Ada Lovelace · ada@example.test')
    expect(first).toContain('openid profile')
    // The raw id is not on the face of the row; it stays in the hover title.
    expect(first).not.toContain(USER_ID)
    expect(rows[0]!.html()).toContain(USER_ID)
  })

  it('falls back to the client id and the user id when nothing better resolves', async () => {
    const { wrapper } = await mountPage()
    const second = wrapper.findAll('.t-item-card')[1]!.text()
    expect(second).toContain('app-two')
    expect(second).toContain(ORPHAN_ID)
  })

  it('offers revoke on active grants only', async () => {
    const { vm } = await mountPage()
    const revoke = vm.rowActions.find((a) => a.key === 'delete')!
    expect(revoke.show!({ status: 'valid' })).toBe(true)
    expect(revoke.show!({ status: 'revoked' })).toBe(false)
    expect(vm.rowActions.map((a) => a.key)).toContain('copyUserId')
  })
})

describe('grantPersonLabel', () => {
  it('joins name and email, avoids repeating an email used as the name', () => {
    const base = { id: 'g', userId: 'u', clientId: 'c', scopes: [] }
    expect(grantPersonLabel({ ...base, userDisplayName: 'Ada', userEmail: 'a@x.test' })).toBe('Ada · a@x.test')
    expect(grantPersonLabel({ ...base, userDisplayName: 'a@x.test', userEmail: 'a@x.test' })).toBe('a@x.test')
    expect(grantPersonLabel({ ...base, userEmail: 'a@x.test' })).toBe('a@x.test')
    expect(grantPersonLabel(base)).toBe('u')
  })
})
