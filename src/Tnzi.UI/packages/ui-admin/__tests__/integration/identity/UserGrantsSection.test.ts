import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Direct grants and denies of one user. Each Save writes a set back WHOLE (the
 * user's entire allow list, the user's entire deny list), so the only sets that
 * may be saved are ones read, in full, for the user on screen: an empty set left
 * by a failed read would let one tick replace every direct grant, or lift every
 * other deny, and a late answer for the user shown before would land on this one.
 */
const getAssignedIds = vi.fn<(userId: string) => Promise<string[]>>()
const getDeniedIds = vi.fn<(userId: string) => Promise<string[]>>()
const setForUser = vi.fn(async (_userId: string, _ids: string[]) => undefined)
const setDeniedForUser = vi.fn(async (_userId: string, _ids: string[]) => undefined)

// The section is shown on `authorization.userFunction.view` alone. The module
// management read (`/admin/modules`, functionModule.view) answers 403 here, as
// it does for a delegated administrator who holds only the section's code; the
// catalogue must come from the user-functions surface.
const reads = vi.hoisted(() => ({
  moduleAdminGetAll: vi.fn(async () => {
    throw new Error('403: authorization.functionModule.view')
  }),
  moduleAdminGetByModule: vi.fn(async () => {
    throw new Error('403: authorization.functionModule.view')
  }),
}))
const catalogue = async () => ({
  modules: [{ id: 'm1', code: 'user', name: 'User', order: 1, isEnabled: true }],
  functionsByModule: new Map([
    ['m1', [
      { id: 'p1', code: 'user.view', name: 'View', moduleId: 'm1', isEnabled: true, order: 1 },
      { id: 'p2', code: 'user.update', name: 'Update', moduleId: 'm1', isEnabled: true, order: 2 },
      { id: 'p3', code: 'user.delete', name: 'Delete', moduleId: 'm1', isEnabled: true, order: 3 },
      { id: 'p9', code: 'user.legacy', name: 'Legacy', moduleId: 'm1', isEnabled: true, order: 9, isRetired: true },
    ]],
  ]),
})
const userFunctionsCatalogue = vi.fn<typeof catalogue>(catalogue)
const messageApi = vi.hoisted(() => ({
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}))
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => messageApi }))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: { getAll: reads.moduleAdminGetAll },
    permissions: { getByModule: reads.moduleAdminGetByModule },
    roleFunctions: { superAdminRoles: vi.fn(async () => []) },
    userFunctions: { catalogue: userFunctionsCatalogue, getAssignedIds, getDeniedIds, setForUser, setDeniedForUser },
  }),
}))

import UserGrantsSection from '../../../src/pages/identity/sections/UserGrantsSection.vue'

const TPermissionMatrix = { props: ['checkedIds'], template: '<div class="matrix" />' }

type Vm = {
  onAllowChecked: (ids: string[]) => void
  onDenyChecked: (ids: string[]) => void
  save: () => Promise<void>
  allowIds: string[]
  denyIds: string[]
}

function deferred<T>() {
  let resolve: (value: T) => void = () => {}
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

function mountSection(userId = 'u1') {
  return mount(UserGrantsSection, {
    props: { userId, userRoleNames: [], canAssign: true, t: (key: string) => key },
    global: { stubs: { TPermissionMatrix } },
  })
}

async function settle(): Promise<void> {
  await flushPromises()
  await new Promise((r) => setTimeout(r, 20))
  await flushPromises()
}

function saveButton(wrapper: ReturnType<typeof mountSection>) {
  return wrapper.findAll('button').find((b) => b.text().trim() === 'admin.common.save')!
}

describe('UserGrantsSection - a Save replaces the whole set', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getAssignedIds.mockReset()
    getDeniedIds.mockReset()
    getAssignedIds.mockImplementation(async () => ['p1'])
    getDeniedIds.mockImplementation(async () => ['p3'])
    setForUser.mockClear()
    setDeniedForUser.mockClear()
    userFunctionsCatalogue.mockReset()
    userFunctionsCatalogue.mockImplementation(catalogue)
    messageApi.error.mockClear()
  })

  it('reads the catalogue from the user-functions surface and drops retired functions', async () => {
    const wrapper = mountSection()
    await settle()

    expect(userFunctionsCatalogue).toHaveBeenCalled()
    expect(reads.moduleAdminGetAll).not.toHaveBeenCalled()
    expect(reads.moduleAdminGetByModule).not.toHaveBeenCalled()
    const vm = wrapper.vm as unknown as { modules: { id: string }[]; functionsByModule: Map<string, { code: string }[]> }
    expect(vm.modules.map((m) => m.id)).toEqual(['m1'])
    expect(vm.functionsByModule.get('m1')?.map((f) => f.code)).toEqual(['user.view', 'user.update', 'user.delete'])
  })

  it('a catalogue that cannot be read is reported and offers no Save', async () => {
    userFunctionsCatalogue.mockImplementation(async () => {
      throw new Error('Request failed with status code 500')
    })
    const wrapper = mountSection()
    await settle()
    const vm = wrapper.vm as unknown as Vm

    expect(messageApi.error).toHaveBeenCalledWith('Request failed with status code 500')
    vm.onAllowChecked(['p2'])
    await settle()
    expect(saveButton(wrapper).attributes('disabled')).toBeDefined()
    await vm.save()
    expect(setForUser).not.toHaveBeenCalled()
  })

  it('saves the sets it read, with the edit, for the user on screen', async () => {
    const wrapper = mountSection()
    await settle()
    const vm = wrapper.vm as unknown as Vm

    vm.onAllowChecked(['p1', 'p2'])
    await settle()
    await saveButton(wrapper).trigger('click')
    await settle()

    expect(setForUser).toHaveBeenCalledWith('u1', ['p1', 'p2'])
    expect(setDeniedForUser).not.toHaveBeenCalled()
  })

  it('a failed read offers no Save: one deny tick would otherwise lift every other deny', async () => {
    getDeniedIds.mockImplementation(async () => {
      throw new Error('Grant store unavailable')
    })
    const wrapper = mountSection()
    await settle()
    const vm = wrapper.vm as unknown as Vm

    vm.onDenyChecked(['p2'])
    await settle()
    expect(saveButton(wrapper).attributes('disabled')).toBeDefined()
    await vm.save()
    expect(setDeniedForUser).not.toHaveBeenCalled()
    expect(setForUser).not.toHaveBeenCalled()
  })

  it('a failed read for the next user does not leave the previous user\'s sets savable onto them', async () => {
    const wrapper = mountSection('u1')
    await settle()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.allowIds).toEqual(['p1'])

    getAssignedIds.mockImplementation(async () => {
      throw new Error('Grant store unavailable')
    })
    await wrapper.setProps({ userId: 'u2' })
    await settle()

    expect(vm.allowIds).toEqual([])
    vm.onAllowChecked(['p2'])
    await settle()
    await vm.save()
    expect(setForUser).not.toHaveBeenCalled()
  })

  it('a late answer for the user shown before is neither shown nor saved onto the next one', async () => {
    const forFirst = deferred<string[]>()
    getAssignedIds.mockImplementation((id: string) => (id === 'u1' ? forFirst.promise : Promise.resolve(['p2'])))
    const wrapper = mountSection('u1')
    await settle()
    const vm = wrapper.vm as unknown as Vm

    await wrapper.setProps({ userId: 'u2' })
    await settle()
    forFirst.resolve(['p1', 'p3'])
    await settle()

    expect(vm.allowIds).toEqual(['p2'])
    vm.onAllowChecked(['p2', 'p1'])
    await settle()
    await saveButton(wrapper).trigger('click')
    await settle()
    expect(setForUser).toHaveBeenCalledTimes(1)
    expect(setForUser).toHaveBeenCalledWith('u2', ['p2', 'p1'])
  })
})
