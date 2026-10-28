import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Permissions from '../../../src/pages/authorization/Permissions.vue'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Shared across bridge instances: the page and the holders drawer body each
// build their own bridge.
const holdersRead = vi.hoisted(() => vi.fn())
// The module-management read (`/admin/modules`, functionModule.view). The page
// is gated by `authorization.permission.view` and must not depend on it.
const moduleAdminGetAll = vi.hoisted(() => vi.fn(async () => {
  throw new Error('403: authorization.functionModule.view')
}))

vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: {
      fetch: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      getAll: moduleAdminGetAll,
    },
    permissions: {
      fetch: vi.fn(async () => ({
        items: [
          // Module access code (view-only prefix parenting other surfaces).
          { id: 'p0', code: 'identity.view', name: 'View Identity', moduleId: 'm1', isEnabled: true, order: 0, isSystemManaged: true, category: 'Business' },
          // A CRUD surface with a Technical write action + a custom row.
          { id: 'p1', code: 'user.view', name: 'View Users', moduleId: 'm1', isEnabled: true, order: 1, isSystemManaged: true, category: 'Business' },
          { id: 'p2', code: 'user.create', name: 'Create Users', moduleId: 'm1', isEnabled: true, order: 2, isSystemManaged: true, category: 'Business' },
          { id: 'p3', code: 'session.view', name: 'View Sessions', moduleId: 'm1', isEnabled: true, order: 3, isSystemManaged: true, category: 'Technical' },
          { id: 'p4', code: 'user.approve', name: 'Approve Users', moduleId: 'm1', isEnabled: false, order: 4, isSystemManaged: false, category: 'Business' },
          { id: 'p5', code: 'identity.loginLog.view', name: 'View Login Logs', moduleId: 'm1', isEnabled: true, order: 5, isSystemManaged: true, category: 'Technical' },
        ],
        totalCount: 6,
        pageIndex: 1,
        pageSize: 500,
      })),
      getByModule: vi.fn(async () => []),
      // Master-Detail page reads the catalogue behind its own code to populate the left tree.
      catalogue: vi.fn(async () => ({
        modules: [
          { id: 'm1', code: 'identity', name: 'Identity', order: 1, isEnabled: true },
          { id: 'm2', code: 'order', name: 'Order', order: 2, isEnabled: true },
        ],
        functionsByModule: new Map(),
      })),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      enable: vi.fn(),
      disable: vi.fn(),
      holders: holdersRead,
    },
    roleFunctions: { fetch: vi.fn() },
    entityRoles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

// Overlays render inline (no teleport) so the drawer body is inside the wrapper.
const stubs = {
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot name="header" /><slot /><slot name="footer" /></div>' },
}

async function mountLoaded() {
  const wrapper = mount(Permissions, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 100))
  await nextTick()
  return wrapper
}

describe('Permissions page (grouped surface catalogue)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    holdersRead.mockReset().mockResolvedValue({
      functionId: 'p2',
      code: 'user.create',
      name: 'Create Users',
      category: 'Business',
      isEnabled: true,
      isRetired: false,
      isInEffect: true,
      userNamesHidden: false,
      roles: [{ id: 'r1', name: 'Root', state: 'SuperAdmin', memberCount: 1 }],
      users: [{ id: 'u1', userName: 'erin', isSuperAdmin: true, isDirect: false, roles: [{ id: 'r1', name: 'Root' }] }],
      deniedUsers: [],
    })
  })

  it('mounts the master-detail layout and loads modules + permissions', async () => {
    const wrapper = await mountLoaded()
    expect(wrapper.find('.t-content-page').exists()).toBe(true)
    const tree = wrapper.find('.t-permission-page__naive-tree')
    expect(tree.exists()).toBe(true)
    expect(tree.text()).toContain('Identity')
    expect(tree.text()).toContain('Order')
  })

  it('renders the detail pane grouped by surface with stats', async () => {
    const wrapper = await mountLoaded()
    expect(wrapper.find('.t-permission-page__detail-header').exists()).toBe(true)
    // 6 codes → 5 surfaces: identity (access), user, session,
    // identity.loginLog, user.approve.
    const surfaceRows = wrapper.findAll('.t-permission-page__surface-row')
    expect(surfaceRows.length).toBe(5)
    const text = wrapper.text()
    // Surface label derives from the view code name: "View Users" → "Users".
    expect(text).toContain('Users')
    // Full codes render in the rows.
    expect(text).toContain('user.create')
    expect(text).toContain('session.view')
  })

  it('pins the module access code first and tags it as menu entry', async () => {
    const wrapper = await mountLoaded()
    const firstSurface = wrapper.find('.t-permission-page__surface-row')
    expect(firstSurface.text()).toContain('identity')
    expect(firstSurface.text()).toContain('Menu entry')
  })

  it('badges technical surfaces and custom (non system-managed) rows', async () => {
    const wrapper = await mountLoaded()
    const surfaceRows = wrapper.findAll('.t-permission-page__surface-row')
    const sessionRow = surfaceRows.find((r) => r.text().includes('session'))
    expect(sessionRow?.text()).toContain('Technical')
    expect(wrapper.text()).toContain('Custom')
  })

  it('filters by category', async () => {
    const wrapper = await mountLoaded()
    const vm = wrapper.vm as unknown as { categoryFilter: string }
    vm.categoryFilter = 'technical'
    await nextTick()
    const rows = wrapper.findAll('.t-permission-page__row')
    expect(rows.length).toBe(2)
    expect(rows.map((r) => r.text()).join(' ')).toContain('session.view')
  })

  it('opens the holders of a code from its row', async () => {
    const wrapper = await mountLoaded()
    expect(wrapper.find('.drawer-stub').exists()).toBe(false)

    const row = wrapper.findAll('.t-permission-page__row').find((r) => r.text().includes('user.create'))
    const button = row!.findAll('button').find((b) => b.text() === 'Holders')
    await button!.trigger('click')
    await flushPromises()

    expect(holdersRead).toHaveBeenCalledWith('user.create')
    const drawer = wrapper.find('.drawer-stub')
    expect(drawer.exists()).toBe(true)
    expect(drawer.text()).toContain('erin')
    expect(drawer.text()).toContain('Super admin (Root)')
  })
})
