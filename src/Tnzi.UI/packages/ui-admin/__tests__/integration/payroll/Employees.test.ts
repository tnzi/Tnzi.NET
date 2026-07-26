import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Payroll Employees page - TCrudPage with ensure-vendor + salary-assignment drawer.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/payroll/employees', fullPath: '/admin/payroll/employees', hash: '', name: 'payroll.employees', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchEmployees = vi.fn(async () => ({
  items: [{ id: 'e1', code: 'EMP-001', name: 'Alice', email: 'a@x.io', isActive: true, creationTime: '2026-07-01' }],
  totalCount: 1, pageIndex: 1, pageSize: 20,
}))

vi.mock('../../../src/services/bridges/payroll-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ...original,
    createPayrollBridge: () => ({
      employees: {
        fetch: fetchEmployees,
        get: vi.fn(async () => null),
        create: vi.fn(), update: vi.fn(), delete: vi.fn(),
        ensureVendor: vi.fn(), assignments: vi.fn(async () => []),
        createAssignment: vi.fn(), deleteAssignment: vi.fn(),
      },
      structures: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
    }),
  }
})

import Page from '../../../src/pages/payroll/Employees.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  Select: { name: 'Select', template: '<select />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
}

describe('Payroll Employees page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchEmployees.mockClear()
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchEmployees.mock.calls.length).toBeGreaterThan(0)
  })
})
