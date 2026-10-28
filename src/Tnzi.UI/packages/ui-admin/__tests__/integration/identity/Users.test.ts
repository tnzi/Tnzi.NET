import { beforeEach, describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const h = vi.hoisted(() => ({
  update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
  getById: vi.fn(),
  importCsv: vi.fn(),
  success: vi.fn(),
  warning: vi.fn(),
  downloadBlob: vi.fn(),
}))

vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => ({ success: h.success, error: vi.fn(), warning: h.warning, info: vi.fn() }),
}))
vi.mock('@tnzi/core/utils', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  downloadBlob: h.downloadBlob,
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: {
      fetch: vi.fn(async () => ({
        items: [
          { id: '1', userName: 'alice', email: 'alice@example.com' },
          { id: '2', userName: 'bob', email: 'bob@example.com' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({ ...(data as object), id: '3' })),
      update: h.update,
      getById: h.getById,
      delete: vi.fn(async () => undefined),
      export: vi.fn(async () => new Blob(['x'])),
      import: h.importCsv,
    },
    roles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    tenants: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    loginLogs: { fetch: vi.fn() },
    gdpr: { requestExport: vi.fn(), requestDeletion: vi.fn() },
    getAuthConfig: vi.fn(async () => null),
  }),
}))

import Users from '../../../src/pages/identity/Users.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="data.length"></div>',
  },
  Pagination: {
    name: 'Pagination',
    props: ['page', 'itemCount', 'pageSize'],
    emits: ['update:page', 'update:pageSize'],
    template: '<div class="n-pagination-stub"></div>',
  },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: {
    name: 'Button',
    template: '<button @click="$emit(\'click\')"><slot /></button>',
  },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template:
      '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: {
    name: 'Popover',
    props: ['show'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

describe('Users page (integration)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts, fetches users on mount, and displays rows', async () => {
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('create button opens form modal in create mode', async () => {
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    await wrapper.find('.t-crud-page__create').trigger('click')
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  /**
   * The list's edit form only shows email / phone / nickname, and the list row
   * carries none of the profile. But the update replaces the user's whole
   * detail record, so an edit started from a list row sent nulls for the name,
   * avatar, bio and the rest, and fixing a typo in an email wiped the profile.
   */
  it('editing from the list keeps the parts of the profile the form does not show', async () => {
    h.update.mockClear()
    h.getById.mockReset().mockResolvedValue({
      id: '1', userName: 'alice', email: 'alice@example.com', phoneNumber: null, nickname: 'Al',
      firstName: 'Alice', lastName: 'Liddell', gender: 2, birthday: '1990-05-04T00:00:00Z', address: '1 Rabbit Hole',
      website: 'https://alice.example', bio: 'Curious', avatar: 'https://cdn.example/alice.png', avatarId: 'f-1',
      roles: ['Staff'], isLockedOut: false,
    })
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      rowActions: Array<{ key: string; onClick: (row: unknown) => unknown }>
      crud: { formModal: { formData: { value: Record<string, unknown> | null } }; submit: () => Promise<unknown> }
    }

    await vm.rowActions.find((a) => a.key === 'edit')!.onClick({ id: '1', userName: 'alice', email: 'alice@example.com' })
    await flushPromises()
    // The form opens on the full record: the nickname it shows is the real one.
    expect(vm.crud.formModal.formData.value?.nickname).toBe('Al')

    vm.crud.formModal.formData.value!.email = 'alice@wonderland.example'
    await vm.crud.submit()

    expect(h.update).toHaveBeenCalledWith('1', expect.objectContaining({
      email: 'alice@wonderland.example',
      nickname: 'Al',
      firstName: 'Alice',
      lastName: 'Liddell',
      gender: 2,
      birthday: '1990-05-04T00:00:00Z',
      address: '1 Rabbit Hole',
      website: 'https://alice.example',
      bio: 'Curious',
      avatarId: 'f-1',
      avatarUrl: 'https://cdn.example/alice.png',
    }))
    wrapper.unmount()
  })

  /**
   * A restored `?detail=edit:<id>` (refresh, Back / Forward) resolves from the
   * loaded list first, and a list row has no nickname: the form showed an empty
   * one and saving it cleared the user's nickname.
   */
  it('★ an edit restored onto a list row keeps the nickname the row does not carry', async () => {
    h.update.mockClear()
    h.getById.mockReset().mockResolvedValue({
      id: '1', userName: 'alice', email: 'alice@example.com', phoneNumber: null, nickname: 'Al',
      firstName: 'Alice', gender: 2, roles: [], isLockedOut: false,
    })
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: {
        openEdit: (row: unknown) => void
        formModal: { formData: { value: Record<string, unknown> | null } }
        submit: () => Promise<unknown>
      }
    }

    // What the restore hands the form: the list row, without the profile.
    vm.crud.openEdit({ id: '1', userName: 'alice', email: 'alice@example.com' })
    await flushPromises()
    expect(vm.crud.formModal.formData.value?.nickname).toBe('Al')

    await vm.crud.submit()
    expect(h.update).toHaveBeenCalledWith('1', expect.objectContaining({ nickname: 'Al', firstName: 'Alice' }))
    wrapper.unmount()
  })

  it('★ a form restored on the list row cannot be saved until the full record is in, and then keeps the nickname', async () => {
    h.update.mockClear()
    const full = {
      id: '1', userName: 'alice', email: 'alice@example.com', phoneNumber: null, nickname: 'Al',
      firstName: 'Alice', gender: 2, roles: [], isLockedOut: false,
    }
    let releaseFirst: (v: unknown) => void = () => {}
    h.getById.mockReset()
      // the form's own swap-in read, still out when Save is pressed
      .mockImplementationOnce(() => new Promise((r) => { releaseFirst = r }))
      .mockResolvedValue(full)
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: {
        formModal: {
          formData: { value: Record<string, unknown> | null }
          open: (mode: 'edit', row: unknown) => void
        }
        submit: () => Promise<unknown>
      }
    }

    // What a restored `?detail=edit:1` does: open on the list row, no nickname.
    vm.crud.formModal.open('edit', { id: '1', userName: 'alice', email: 'alice@example.com' })
    await flushPromises()
    expect(await vm.crud.submit()).toBeNull()
    expect(h.update).not.toHaveBeenCalled()

    releaseFirst(full)
    await flushPromises()
    expect(vm.crud.formModal.formData.value).toMatchObject({ nickname: 'Al' })
    await vm.crud.submit()

    expect(h.update).toHaveBeenCalledWith('1', expect.objectContaining({ nickname: 'Al' }))
    wrapper.unmount()
  })

  /**
   * The import endpoint answers 200 with a per-row result even when rows were
   * refused (password policy, missing columns) or skipped (the email already has
   * an account). The page used to drop that result, so a partial import looked
   * exactly like a complete one.
   */
  describe('CSV import', () => {
    beforeEach(() => {
      h.importCsv.mockReset()
      h.success.mockClear()
      h.warning.mockClear()
    })

    /** The warning's text: the page renders it as a pre-line block so each row reads on its own line. */
    function warningText(): string {
      const content = h.warning.mock.calls[0]![0] as string | (() => { children: unknown })
      return String(typeof content === 'function' ? content().children : content)
    }

    async function importFile() {
      const wrapper = mount(Users, { global: { stubs } })
      await flushPromises()
      await (wrapper.vm as unknown as { crud: { importFile: (f: File) => Promise<void> } }).crud
        .importFile(new File(['UserName,Email,Password'], 'users.csv', { type: 'text/csv' }))
      await flushPromises()
    }

    it('★ reports the rows that were not created, and why', async () => {
      h.importCsv.mockResolvedValue({
        totalRows: 50, successCount: 25, failedCount: 20, skippedCount: 5,
        errors: { 3: 'Passwords must have at least one digit.', 7: 'Email and Password are required' },
      })
      await importFile()

      expect(h.warning).toHaveBeenCalledTimes(1)
      const text = warningText()
      expect(text).toContain('Imported 25 of 50 rows: 20 failed, 5 skipped')
      expect(text).toContain('Row 3: Passwords must have at least one digit.')
      expect(text).toContain('Row 7: Email and Password are required')
      expect(h.success).not.toHaveBeenCalled()
      // A report that names rows to fix stays until the admin closes it.
      expect(h.warning.mock.calls[0]![1]).toEqual({ duration: 0, closable: true })
    })

    it('lists the first few reasons and counts the rest', async () => {
      const errors = Object.fromEntries(Array.from({ length: 8 }, (_, i) => [i + 2, `Reason ${i + 2}`]))
      h.importCsv.mockResolvedValue({ totalRows: 8, successCount: 0, failedCount: 8, skippedCount: 0, errors })
      await importFile()

      const text = warningText()
      expect(text).toContain('Row 2: Reason 2')
      expect(text).not.toContain('Row 9: Reason 9')
      expect(text).toContain('3 more failed rows not shown')
    })

    it('says a complete import is complete', async () => {
      h.importCsv.mockResolvedValue({ totalRows: 3, successCount: 3, failedCount: 0, skippedCount: 0, errors: {} })
      await importFile()

      expect(h.success).toHaveBeenCalledWith('Imported 3 users.')
      expect(h.warning).not.toHaveBeenCalled()
    })

    // The endpoint reads the upload as CSV text; the picker must not offer a
    // spreadsheet that would then be refused row by row.
    it('offers only CSV files in the picker', async () => {
      const wrapper = mount(Users, { global: { stubs } })
      await flushPromises()
      expect((wrapper.vm as unknown as { crud: { importAccept?: string } }).crud.importAccept).toBe('.csv')
    })
  })

  // The export file was named after the untranslated title key ("title.csv").
  it('names the CSV export after the page title', async () => {
    h.downloadBlob.mockClear()
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    const exportButton = wrapper.findAll('button').find((b) => b.text().includes('Export'))!
    await exportButton.trigger('click')
    await flushPromises()

    expect(h.downloadBlob).toHaveBeenCalledWith(expect.any(Blob), 'Users.csv')
  })

  it('page title resolves the "Users" i18n label', async () => {
    const wrapper = mount(Users, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Users')
  })
})
