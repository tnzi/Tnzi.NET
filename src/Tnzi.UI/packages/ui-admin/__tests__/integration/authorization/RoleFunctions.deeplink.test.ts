import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import RoleFunctions from '../../../src/pages/authorization/RoleFunctions.vue'

// A reactive route the test can move after mount, the way a page kept alive in
// its tab sees a deep link: as a change of its route, not as a fresh mount.
const routing = vi.hoisted(() => ({ route: null as unknown as { query: Record<string, unknown> } }))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  routing.route = reactive({ query: {}, params: {}, path: '/admin/authorization/role-functions', fullPath: '/admin/authorization/role-functions', hash: '', name: 'authorization.roleFunctions', meta: {} }) as never
  return {
    useRoute: () => routing.route,
    useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
  }
})

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Captures the options the page hands naive's dialog, so a test can drive
// each close path (button / X / Esc) and see whether the promise settles.
const dialogCalls = vi.hoisted(() => ({ warning: [] as Record<string, (() => void) | undefined>[] }))
vi.mock('naive-ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('naive-ui')>()),
  useDialog: () => ({
    warning: (opts: Record<string, (() => void) | undefined>) => {
      dialogCalls.warning.push(opts)
    },
  }),
}))

// A role's assignment set is read whole and written back whole (Save replaces it).
const getAssignedIds = vi.fn(async (_roleId: string): Promise<string[]> => ['p1'])

const messageApi = vi.hoisted(() => ({
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}))
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => messageApi }))

// The page reads its role list and permission catalogue from the role-functions
// surface, under the page's own gate.
vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    permissions: { fetch: vi.fn() },
    roleFunctions: {
      fetch: vi.fn(),
      getAssignedIds,
      setForRole: vi.fn(async () => undefined),
      clearForRole: vi.fn(async () => undefined),
      roles: vi.fn(async () => [
        { id: 'r2', name: 'Editor', normalizedName: 'EDITOR', isSystem: false, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
        { id: 'r1', name: 'Admin', normalizedName: 'ADMIN', isSystem: true, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
      ]),
      catalogue: vi.fn(async () => ({
        modules: [
          { id: 'm1', code: 'user', name: 'User', order: 1, isEnabled: true },
          { id: 'm2', code: 'order', name: 'Order', order: 2, isEnabled: true },
        ],
        functionsByModule: new Map([
          ['m1', [
            { id: 'p1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 },
            { id: 'p2', code: 'user.edit', name: 'Edit User', moduleId: 'm1', isEnabled: true, order: 2 },
          ]],
          ['m2', []],
        ]),
      })),
    },
    entityRoles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

/**
 * The "Open Permission Editor" link lands on `?roleId=`. It was read once, on
 * mount; with the page kept alive in its tab, a link into the already-open
 * page arrives as a route change and left the previously selected role on
 * screen.
 */
describe('RoleFunctions - the roleId deep link', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routing.route.query = {}
    getAssignedIds.mockReset()
    getAssignedIds.mockImplementation(async () => ['p1'])
  })

  it('a deep link into the open page selects that role', async () => {
    const wrapper = mount(RoleFunctions)
    await flushPromises()
    const vm = wrapper.vm as unknown as { selectedRoleId: string | null }
    expect(vm.selectedRoleId).toBe('r1')

    routing.route.query = { roleId: 'r2' }
    await flushPromises()

    expect(vm.selectedRoleId).toBe('r2')
    expect(getAssignedIds).toHaveBeenLastCalledWith('r2')
    wrapper.unmount()
  })

  it('a deep link present at mount still selects that role', async () => {
    routing.route.query = { roleId: 'r2' }
    const wrapper = mount(RoleFunctions)
    await flushPromises()
    expect((wrapper.vm as unknown as { selectedRoleId: string | null }).selectedRoleId).toBe('r2')
    wrapper.unmount()
  })
})
