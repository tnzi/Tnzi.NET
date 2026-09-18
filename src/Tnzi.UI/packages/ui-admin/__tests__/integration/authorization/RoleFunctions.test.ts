import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import RoleFunctions from '../../../src/pages/authorization/RoleFunctions.vue'

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

// Authorization bridge - supplies the module/permission/role-function set.
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

// Identity bridge - supplies the role list rendered in the left sidebar.
// The page must read the UNPAGED `roles.getAll`: the paged `fetch` clamps to
// 100 rows on the server whatever pageSize is asked for, so a `pageSize: 500`
// call silently left roles 101+ out of the matrix. `fetch` stays on the mock
// so the assertion that it is never called means something.
const rolesFetch = vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 }))
const rolesGetAll = vi.fn(async () => [
  { id: 'r2', name: 'Editor', normalizedName: 'EDITOR', isSystem: false, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
  { id: 'r1', name: 'Admin', normalizedName: 'ADMIN', isSystem: true, isDefault: false, creationTime: '2026-04-14T00:00:00Z' },
])
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    roles: { fetch: rolesFetch, getAll: rolesGetAll },
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
    // Role sidebar shows both seeded roles, sorted by name, from the unpaged endpoint.
    const roles = wrapper.findAll('.t-role-func-page__role-item')
    expect(roles.length).toBe(2)
    expect(roles[0]?.text()).toContain('Admin')
    expect(roles[1]?.text()).toContain('Editor')
    expect(rolesGetAll).toHaveBeenCalled()
    expect(rolesFetch).not.toHaveBeenCalled()
  })

  /**
   * The unsaved-changes / clear-all / cleanup confirmations hand-rolled the
   * dialog promise and settled it only on the buttons, X and mask click. Esc
   * (`closeOnEsc`, default on) hides the dialog through none of those, so
   * every Esc left the calling flow awaiting forever; the adapter listens to
   * `onAfterLeave`, the one hook naive fires after any close.
   */
  it('a confirmation dismissed with Esc settles as cancelled', async () => {
    const wrapper = mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()
    dialogCalls.warning.length = 0

    const vm = wrapper.vm as unknown as { confirmSwitchDirty: () => Promise<boolean> }
    let settled: boolean | undefined
    const pending = vm.confirmSwitchDirty().then((ok) => { settled = ok })
    await nextTick()
    expect(dialogCalls.warning).toHaveLength(1)
    const opts = dialogCalls.warning[0]!
    // Esc: naive fires only the leave hook.
    expect(typeof opts.onAfterLeave).toBe('function')
    opts.onAfterLeave!()
    await pending
    expect(settled).toBe(false)
  })

  it('auto-selects the first role so the right pane is never blank on open', async () => {
    const wrapper = mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()
    // No "pick a role" placeholder - the first role is selected automatically.
    expect(wrapper.find('.t-role-func-page__placeholder').exists()).toBe(false)
    // First role in the rail is marked active and the matrix is rendered.
    const roles = wrapper.findAll('.t-role-func-page__role-item')
    expect(roles[0]?.classes()).toContain('is-active')
    expect(wrapper.find('.t-perm-matrix').exists()).toBe(true)
  })
})
