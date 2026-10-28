import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Editing a draft pay run keeps its structure filter and memo.
 *
 * The edit form opens from the list row and the update replaces the run. The
 * list row used to carry neither field, so an edit made only to move the pay
 * date sent `structureId: null`: the form showed the filter as empty, the save
 * cleared it, and the next calculation pulled every employee with an
 * assignment into a run meant for one structure.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/payroll/pay-runs', fullPath: '/admin/payroll/pay-runs', hash: '', name: 'payroll.payRuns', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const draftRow = {
  id: 'r2', number: null, status: 'Draft', periodStart: '2026-07-01', periodEnd: '2026-07-07', payDate: '2026-07-10',
  frequency: 'Weekly', source: 'Internal', employeeCount: 0, grossTotal: 0, netTotal: 0, creationTime: '2026-07-01',
  structureId: 'weekly-structure', memo: 'Weekly crew only',
}

const h = vi.hoisted(() => ({ updateDraft: vi.fn() }))
const emptyPage = { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }

vi.mock('../../../src/services/bridges/payroll-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ...original,
    createPayrollBridge: () => ({
      runs: {
        fetch: vi.fn(async () => ({ items: [draftRow], totalCount: 1, pageIndex: 1, pageSize: 20 })),
        getById: vi.fn(async () => null),
        createDraft: vi.fn(), updateDraft: h.updateDraft, deleteDraft: vi.fn(),
        calculate: vi.fn(), post: vi.fn(), pay: vi.fn(), voidRun: vi.fn(),
        payslips: vi.fn(async () => []), payslip: vi.fn(async () => null),
        updatePayslipInputs: vi.fn(), createFromExternal: vi.fn(),
        inputs: vi.fn(async () => []), setInput: vi.fn(), deleteInput: vi.fn(),
      },
      structures: { fetch: vi.fn(async () => emptyPage) },
      components: { fetch: vi.fn(async () => emptyPage) },
      employees: { fetch: vi.fn(async () => emptyPage) },
    }),
  }
})

import Page from '../../../src/pages/payroll/PayRuns.vue'

let capturedRowActions: Array<{ key: string; onClick?: (row: unknown) => unknown }> = []

const stubs = {
  TCrudPage: {
    name: 'TCrudPage',
    props: ['state', 'allColumns', 'title', 'rowActions', 'translate', 'detailWidth', 'detailTitle'],
    template: '<div class="t-crud-page-stub" />',
    created(this: { rowActions: typeof capturedRowActions }) {
      capturedRowActions = this.rowActions ?? []
    },
  },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
}

describe('Pay run edit', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    capturedRowActions = []
    h.updateDraft.mockReset().mockResolvedValue({ ...draftRow })
  })

  it('★ opens with the structure filter and memo of the run, and saves them back', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const edit = capturedRowActions.find((a) => a.key === 'edit')
    expect(edit).toBeTruthy()
    edit!.onClick?.(draftRow)
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      crud: { formModal: { formData: { value: Record<string, unknown> } }; submit: () => Promise<void> }
    }
    expect(vm.crud.formModal.formData.value.structureId).toBe('weekly-structure')
    expect(vm.crud.formModal.formData.value.memo).toBe('Weekly crew only')

    await vm.crud.submit()
    await flushPromises()

    expect(h.updateDraft).toHaveBeenCalledWith('r2', expect.objectContaining({
      structureId: 'weekly-structure',
      memo: 'Weekly crew only',
    }))
  })
})
