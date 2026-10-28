import { beforeEach, describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { Ref } from 'vue'

/**
 * The Users list's edit modal against the replace-semantics profile update.
 *
 * ★★★ The modal used to submit the list row as-is. The server replaces the
 * profile as a whole once a request carries any profile field, so editing a
 * nickname (or anything, once the form grew a profile field) wiped the
 * person's names, avatar, birthday and bio. These pin that the modal opens on
 * the loaded user and that the saved body carries the profile through.
 */
const api = vi.hoisted(() => ({
  getById: vi.fn(),
  update: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: {
      fetch: vi.fn(async () => ({
        items: [{ id: '1', userName: 'ivy@corp.example', email: 'ivy@corp.example', phoneNumber: '+15550000001' }],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 20,
      })),
      getById: api.getById,
      create: vi.fn(),
      update: api.update,
      delete: vi.fn(async () => undefined),
      export: vi.fn(async () => new Blob(['x'])),
      import: vi.fn(async () => undefined),
    },
    roles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    tenants: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    loginLogs: { fetch: vi.fn() },
    gdpr: { requestExport: vi.fn(), requestDeletion: vi.fn() },
    invitations: { resend: vi.fn(), revoke: vi.fn() },
    getAuthConfig: vi.fn(async () => null),
  }),
}))

import Users from '../../../src/pages/identity/Users.vue'

const loaded = {
  id: '1',
  userName: 'ivy@corp.example',
  email: 'ivy@corp.example',
  phoneNumber: '+15550000001',
  firstName: 'Ivy',
  lastName: 'Lee',
  nickname: 'ivy',
  avatar: 'https://cdn.example/ivy.png',
  avatarId: 'file-1',
  gender: 2,
  birthday: '1990-05-01T00:00:00Z',
  bio: 'Payroll lead',
  address: '1 Main St',
  website: 'https://ivy.example',
  roles: ['Staff'],
}

interface Row { id: string; [key: string]: unknown }
interface PageVm {
  crud: {
    openEdit: (row: Row) => void
    submit: () => Promise<unknown>
    formModal: { formData: Ref<Record<string, unknown> | null>; open: (mode: 'edit', row: Row) => void }
  }
  rowActions: Array<{ key: string; onClick: (row: Row) => void }>
}

const stubs = {
  DataTable: { name: 'DataTable', props: ['data', 'columns', 'loading'], template: '<div class="n-data-table-stub"></div>' },
  Pagination: { name: 'Pagination', template: '<div></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

async function mountPage(): Promise<PageVm> {
  const wrapper = mount(Users, { global: { stubs } })
  await flushPromises()
  return wrapper.vm as unknown as PageVm
}

const listRow: Row = { id: '1', userName: 'ivy@corp.example', email: 'ivy@corp.example', phoneNumber: '+15550000001' }

describe('Users page: edit modal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.getById.mockReset().mockResolvedValue({ ...loaded })
    api.update.mockReset().mockImplementation(async (id: string, data: object) => ({ id, ...data }))
  })

  it('opens on the user as the server holds them, so the nickname shows', async () => {
    const vm = await mountPage()

    vm.rowActions.find((a) => a.key === 'edit')!.onClick(listRow)
    await flushPromises()

    expect(api.getById).toHaveBeenCalledWith('1')
    expect(vm.crud.formModal.formData.value?.nickname).toBe('ivy')
  })

  it('saving a phone change sends the whole profile, avatar included', async () => {
    const vm = await mountPage()
    vm.rowActions.find((a) => a.key === 'edit')!.onClick(listRow)
    await flushPromises()

    vm.crud.formModal.formData.value!.phoneNumber = '+15550000002'
    await vm.crud.submit()

    expect(api.update).toHaveBeenCalledTimes(1)
    const [id, body] = api.update.mock.calls[0]
    expect(id).toBe('1')
    expect(body).toEqual({
      email: 'ivy@corp.example',
      phoneNumber: '+15550000002',
      firstName: 'Ivy',
      lastName: 'Lee',
      nickname: 'ivy',
      gender: 2,
      birthday: '1990-05-01T00:00:00Z',
      bio: 'Payroll lead',
      address: '1 Main St',
      website: 'https://ivy.example',
      avatarId: 'file-1',
      avatarUrl: 'https://cdn.example/ivy.png',
    })
    // Row data that is not a field of the form never reaches the body.
    expect(body).not.toHaveProperty('roles')
  })

  /**
   * A modal restored from the list row (deep link) opens on the row before the
   * full record is swapped in. A save in that window still keeps the profile,
   * because the save reads the user afresh.
   */
  it('a modal opened on the bare list row still keeps the profile', async () => {
    const vm = await mountPage()
    vm.crud.formModal.open('edit', { ...listRow })

    await vm.crud.submit()

    const body = api.update.mock.calls[0][1]
    expect(body.nickname).toBe('ivy')
    expect(body.firstName).toBe('Ivy')
    expect(body.avatarId).toBe('file-1')
  })
})
