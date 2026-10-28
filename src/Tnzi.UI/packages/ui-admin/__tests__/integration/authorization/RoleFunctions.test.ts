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

// A role's assignment set is read whole and written back whole (Save replaces it).
const getAssignedIds = vi.fn(async (_roleId: string): Promise<string[]> => ['p1'])
const setForRole = vi.fn(async (_roleId: string, _ids: string[]) => undefined)

const messageApi = vi.hoisted(() => ({
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}))
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => messageApi }))

// The page is gated by `authorization.roleFunction.view` alone. Its role list
// and catalogue come from the role-functions surface; the module management
// read (`/admin/modules`, functionModule.view) and Identity's role list
// (`role.view`) answer 403 here, as they do for a delegated administrator who
// holds only the page's code.
const reads = vi.hoisted(() => ({
  moduleAdminGetAll: vi.fn(async () => {
    throw new Error('403: authorization.functionModule.view')
  }),
  identityRolesGetAll: vi.fn(async () => {
    throw new Error('403: role.view')
  }),
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
        { id: 'p9', code: 'user.legacy', name: 'Legacy', moduleId: 'm1', isEnabled: true, order: 9, isRetired: true },
      ]],
      ['m2', []],
    ]),
  })),
}))

// Authorization bridge - supplies the module/permission/role-function set.
vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: {
      fetch: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      getAll: reads.moduleAdminGetAll,
    },
    permissions: {
      fetch: vi.fn(),
      getByModule: vi.fn(async () => {
        throw new Error('403: authorization.functionModule.view')
      }),
    },
    roleFunctions: {
      fetch: vi.fn(),
      getAssignedIds,
      setForRole,
      clearForRole: vi.fn(async () => undefined),
      roles: reads.roles,
      catalogue: reads.catalogue,
    },
    entityRoles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    roles: { fetch: vi.fn(), getAll: reads.identityRolesGetAll },
    users: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    tenants: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    organizations: { getTree: vi.fn() },
    sessions: { listForUser: vi.fn() },
    loginLogs: { fetch: vi.fn() },
    gdpr: { fetchRequests: vi.fn() },
  }),
}))

describe('RoleFunctions page (Tier 3: dual-pane assignment)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getAssignedIds.mockReset()
    getAssignedIds.mockImplementation(async () => ['p1'])
    setForRole.mockClear()
    vi.mocked(messageApi.error).mockClear()
  })

  // The catalogue is one read: when it fails the operator is told, rather than
  // being shown a matrix with no modules, which reads as "nothing to grant".
  it('a catalogue that cannot be read is reported, not shown as having no permissions', async () => {
    reads.catalogue.mockRejectedValueOnce(new Error('Request failed with status code 500'))
    mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()

    expect(messageApi.error).toHaveBeenCalledWith('Request failed with status code 500')
  })

  it('mounts the dual-pane layout and loads roles + modules', async () => {
    const wrapper = mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()
    expect(wrapper.find('.t-content-page').exists()).toBe(true)
    // Role sidebar shows both seeded roles, sorted by name, from the role-functions surface.
    const roles = wrapper.findAll('.t-role-func-page__role-item')
    expect(roles.length).toBe(2)
    expect(roles[0]?.text()).toContain('Admin')
    expect(roles[1]?.text()).toContain('Editor')
    expect(reads.roles).toHaveBeenCalled()
    expect(reads.catalogue).toHaveBeenCalled()
    // Neither read gated by another code is touched.
    expect(reads.identityRolesGetAll).not.toHaveBeenCalled()
    expect(reads.moduleAdminGetAll).not.toHaveBeenCalled()
  })

  it('renders the catalogue modules and leaves retired functions out of the matrix', async () => {
    const wrapper = mount(RoleFunctions)
    await nextTick()
    await new Promise(r => setTimeout(r, 100))
    await nextTick()
    const vm = wrapper.vm as unknown as { modules: { id: string }[]; functionsByModule: Map<string, { code: string }[]> }
    expect(vm.modules.map((m) => m.id)).toEqual(['m1', 'm2'])
    expect(vm.functionsByModule.get('m1')?.map((f) => f.code)).toEqual(['user.view', 'user.edit'])
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

  describe('Save replaces the role\'s whole set, so only a set read for that role may be saved', () => {
    type Vm = {
      selectRole: (id: string) => Promise<void>
      onCheckedChange: (ids: string[]) => void
      handleSave: () => Promise<void>
      checkedFunctionIds: string[]
    }

    async function settle(): Promise<void> {
      await nextTick()
      await new Promise((r) => setTimeout(r, 50))
      await nextTick()
    }

    function deferred<T>() {
      let resolve: (value: T) => void = () => {}
      const promise = new Promise<T>((res) => { resolve = res })
      return { promise, resolve }
    }

    function saveButton(wrapper: ReturnType<typeof mount>) {
      return wrapper.findAll('button').find((b) => b.text().trim() === 'Save')!
    }

    it('a failed read of the role\'s set offers no Save, so one tick cannot replace everything it holds', async () => {
      getAssignedIds.mockImplementation(async () => {
        throw new Error('Role assignments unavailable')
      })
      const wrapper = mount(RoleFunctions)
      await settle()
      const vm = wrapper.vm as unknown as Vm

      vm.onCheckedChange(['p2'])
      await settle()
      expect(saveButton(wrapper).attributes('disabled')).toBeDefined()
      await vm.handleSave()
      expect(setForRole).not.toHaveBeenCalled()
    })

    it('a late answer for the role selected before is neither shown nor saved under the next one', async () => {
      const forEditor = deferred<string[]>()
      const forAdmin = deferred<string[]>()
      const wrapper = mount(RoleFunctions)
      await settle()
      const vm = wrapper.vm as unknown as Vm

      getAssignedIds.mockImplementation((id: string) => (id === 'r2' ? forEditor.promise : forAdmin.promise))
      void vm.selectRole('r2')
      await settle()
      void vm.selectRole('r1')
      await settle()
      // Admin's set lands first, then Editor's late answer arrives.
      forAdmin.resolve(['p1'])
      await settle()
      forEditor.resolve(['p2'])
      await settle()

      expect(vm.checkedFunctionIds).toEqual(['p1'])
      vm.onCheckedChange(['p1', 'p2'])
      await settle()
      await saveButton(wrapper).trigger('click')
      await settle()
      expect(setForRole).toHaveBeenCalledTimes(1)
      expect(setForRole).toHaveBeenCalledWith('r1', ['p1', 'p2'])
    })

    it('offers no Save while the selected role\'s set is still being read', async () => {
      const forEditor = deferred<string[]>()
      const wrapper = mount(RoleFunctions)
      await settle()
      const vm = wrapper.vm as unknown as Vm

      getAssignedIds.mockImplementation(() => forEditor.promise)
      void vm.selectRole('r2')
      await settle()
      // Admin's set is not left on screen under Editor's name.
      expect(vm.checkedFunctionIds).toEqual([])
      // Whatever is ticked now would be saved as Editor's entire set.
      vm.onCheckedChange(['p2'])
      await settle()
      expect(saveButton(wrapper).attributes('disabled')).toBeDefined()
      await vm.handleSave()
      expect(setForRole).not.toHaveBeenCalled()

      forEditor.resolve(['p1'])
      await settle()
      expect(vm.checkedFunctionIds).toEqual(['p1'])
    })
  })
})
