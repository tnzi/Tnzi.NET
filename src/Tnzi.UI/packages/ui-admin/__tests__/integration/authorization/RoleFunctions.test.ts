import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import RoleFunctions from '../../../src/pages/authorization/RoleFunctions.vue'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Authorization bridge — supplies the module/permission/role-function set.
vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: {
      fetch: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      getAll: vi.fn(async () => [
        { id: 'm1', code: 'user', name: 'User', order: 1, isEnabled: true },
        { id: 'm2', code: 'order', name: 'Order', order: 2, isEnabled: true },
      ]),
    },
    permissions: {
      fetch: vi.fn(),
      getByModule: vi.fn(async (id: string) =>
        id === 'm1'
          ? [
              { id: 'p1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 },
              { id: 'p2', code: 'user.edit', name: 'Edit User', moduleId: 'm1', isEnabled: true, order: 2 },
            ]
          : [],
      ),
    },
    roleFunctions: {
      fetch: vi.fn(),
      getAssignedIds: vi.fn(async () => ['p1']),
      setForRole: vi.fn(async () => undefined),
      clearForRole: vi.fn(async () => undefined),
    },
    entityRoles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

// Identity bridge — supplies the role list rendered in the left sidebar.
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    roles: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'r1', name: 'Admin', normalizedName: 'ADMIN', isSystem: true, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
          { id: 'r2', name: 'Editor', normalizedName: 'EDITOR', isSystem: false, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
        ],
        totalCount: 2,
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

describe('RoleFunctions page (Tier 3: dual-pane assignment)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts the dual-pane layout and loads roles + modules', async () => {
    const wrapper = mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()
    expect(wrapper.find('.t-content-page').exists()).toBe(true)
    // Role sidebar shows both seeded roles.
    const roles = wrapper.findAll('.t-role-func-page__role-item')
    expect(roles.length).toBe(2)
    expect(roles[0]?.text()).toContain('Admin')
    expect(roles[1]?.text()).toContain('Editor')
  })

  it('shows a select-role prompt before any role is picked', async () => {
    const wrapper = mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    expect(wrapper.find('.t-role-func-page__placeholder').exists()).toBe(true)
  })
})
