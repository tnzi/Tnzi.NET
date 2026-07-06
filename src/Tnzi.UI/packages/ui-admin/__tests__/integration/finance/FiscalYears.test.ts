import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Fiscal Years page — the backend returns a flat list (wrapped as a single
 * page); rows carry close/reopen lifecycle actions.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/fiscal-years', fullPath: '/admin/finance/fiscal-years', hash: '', name: 'finance.fiscalYears', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const list = vi.fn(async () => [
  { id: 'f1', name: 'FY2026', startDate: '2026-01-01T00:00:00', endDate: '2026-12-31T00:00:00', isClosed: false },
])
const close = vi.fn(async () => undefined)
const reopen = vi.fn(async () => undefined)

vi.mock('../../../src/services/bridges/finance-bridge', () => ({
  createFinanceBridge: () => ({
    fiscalYears: {
      list,
      create: vi.fn(async (data: unknown) => ({ id: 'f2', ...(data as object) })),
      close,
      reopen,
      delete: vi.fn(async () => undefined),
    },
  }),
}))

import FiscalYears from '../../../src/pages/finance/FiscalYears.vue'

const stubs = {
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
}

interface FiscalYearsVm {
  closeYear: (row: { id?: string }) => Promise<void>
  reopenYear: (row: { id?: string }) => Promise<void>
}

describe('Finance FiscalYears page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    list.mockClear()
    close.mockClear()
    reopen.mockClear()
  })

  it('mounts and loads the fiscal-year list', async () => {
    mount(FiscalYears, { global: { stubs } })
    await flushPromises()
    expect(list).toHaveBeenCalledTimes(1)
  })

  it('close and reopen actions hit the bridge and refresh', async () => {
    const wrapper = mount(FiscalYears, { global: { stubs } })
    await flushPromises()
    list.mockClear()

    const vm = wrapper.vm as unknown as FiscalYearsVm
    await vm.closeYear({ id: 'f1' })
    expect(close).toHaveBeenCalledWith('f1')

    await vm.reopenYear({ id: 'f1' })
    expect(reopen).toHaveBeenCalledWith('f1')
    expect(list).toHaveBeenCalled()
  })
})
