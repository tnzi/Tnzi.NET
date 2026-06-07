import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Permissions from '../../../src/pages/authorization/Permissions.vue'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: {
      fetch: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      // Master-Detail page calls getAll() to populate the left tree.
      getAll: vi.fn(async () => [
        { id: 'm1', code: 'user', name: 'User', order: 1, isEnabled: true },
        { id: 'm2', code: 'order', name: 'Order', order: 2, isEnabled: true },
      ]),
    },
    permissions: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'p1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 },
          { id: 'p2', code: 'user.edit', name: 'Edit User', moduleId: 'm1', isEnabled: true, order: 2 },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 200,
      })),
    },
    roleFunctions: { fetch: vi.fn() },
    entityRoles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {} }),
  useRouter: () => ({ push: vi.fn() }),
}))

describe('Permissions page (Tier 2: Master-Detail tree)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts the master-detail layout and loads modules + permissions', async () => {
    const wrapper = mount(Permissions)
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    await nextTick()
    expect(wrapper.find('.t-content-page').exists()).toBe(true)
    // Module tree is hydrated with the seeded modules.
    const tree = wrapper.find('.t-permission-page__naive-tree')
    expect(tree.exists()).toBe(true)
    expect(tree.text()).toContain('User')
    expect(tree.text()).toContain('Order')
  })

  it('renders a detail pane (auto-selects first module after load)', async () => {
    const wrapper = mount(Permissions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()
    // After loadModules() auto-picks the first root, the detail header appears.
    expect(wrapper.find('.t-permission-page__detail-header').exists()).toBe(true)
  })
})
