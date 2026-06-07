import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import EntityRoles from '../../../src/pages/authorization/EntityRoles.vue'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), getAll: vi.fn() },
    permissions: { fetch: vi.fn(), getByModule: vi.fn() },
    roleFunctions: { fetch: vi.fn(), getAssignedIds: vi.fn(), setForRole: vi.fn(), clearForRole: vi.fn() },
    entityInfos: {
      getAll: vi.fn(async () => [
        { id: 'ei1', name: 'User', typeName: 'Tnzi.Identity.User', displayName: 'User', isDataAuthEnabled: true },
        { id: 'ei2', name: 'Order', typeName: 'Demo.Order', displayName: 'Order', isDataAuthEnabled: true },
      ]),
    },
    entityRoles: {
      fetch: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      getByEntityInfo: vi.fn(async () => []),
      setCell: vi.fn(async () => undefined),
    },
  }),
}))

vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    roles: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'r1', name: 'Admin', normalizedName: 'ADMIN', isSystem: true, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
        ],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 500,
      })),
    },
    users: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    tenants: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    organizations: { getTree: vi.fn() },
    sessions: { listForUser: vi.fn() },
    loginLogs: { fetch: vi.fn() },
    gdpr: { fetchRequests: vi.fn() },
  }),
}))

describe('EntityRoles page (Tier 3: data-permission matrix)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts the matrix page and shows a select-entity prompt before pick', async () => {
    const wrapper = mount(EntityRoles)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    expect(wrapper.find('.t-content-page').exists()).toBe(true)
    expect(wrapper.find('.t-entity-role-page__placeholder').exists()).toBe(true)
  })

  it('renders the toolbar entity selector', async () => {
    const wrapper = mount(EntityRoles)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    // Toolbar contains the entity-picker label + NSelect placeholder. We don't
    // probe the underlying entity options because NSelect renders them lazily
    // in a teleported popover only when the trigger is opened.
    expect(wrapper.find('.t-entity-role-page__toolbar').exists()).toBe(true)
    expect(wrapper.find('.n-base-selection').exists()).toBe(true)
  })
})
