import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Tax returns - which forms exist depends on the loaded country pack.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/tax-returns', fullPath: '/admin/finance/tax-returns', hash: '', name: 'finance.taxReturns', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const forms = vi.fn(async () => [{ country: 'CA', formCode: 'GST34' }])

const getReturn = vi.fn(async () => ({
  formCode: 'GST34',
  formName: 'GST34 - Goods and Services Tax / Harmonized Sales Tax Return',
  country: 'CA',
  periodFrom: '2026-07-01',
  periodTo: '2026-09-30',
  currency: 'CAD',
  netTax: 130,
  lines: [
    { line: '101', label: 'Sales and other revenue', amount: 4000, isCalculated: false },
    { line: '105', label: 'GST/HST collected', amount: 200, isCalculated: false },
    { line: '108', label: 'Input tax credits', amount: 70, isCalculated: false },
    { line: '109', label: 'Net tax', amount: 130, isCalculated: true },
  ],
}))

vi.mock('../../../src/services/bridges/finance-bridge', async () => ({
  createFinanceBridge: () => ({ taxReturns: { forms, get: getReturn } }),
}))

import Page from '../../../src/pages/finance/TaxReturns.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Select: { name: 'Select', template: '<select />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Alert: { name: 'Alert', template: '<div class="n-alert-stub"><slot /></div>' },
}

interface TaxVm {
  formOptions: Array<{ label: string; value: string }>
  selectedForm: string | null
  load: () => Promise<void>
  ret: { netTax: number; lines: unknown[] } | null
}

describe('Finance TaxReturns page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    forms.mockClear()
    getReturn.mockClear()
  })

  /** One form loaded = pick it; making the user choose from a list of one is noise. */
  it('lists the available forms and preselects a lone one', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    expect(forms).toHaveBeenCalled()
    const vm = wrapper.vm as unknown as TaxVm
    expect(vm.formOptions.length).toBe(1)
    expect(vm.selectedForm).toBe('CA/GST34')
  })

  it('runs the return for the selected period', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    await (wrapper.vm as unknown as TaxVm).load()
    await flushPromises()

    expect(getReturn).toHaveBeenCalled()
    const args = getReturn.mock.calls[0]!
    expect(args[0]).toBe('CA')
    expect(args[1]).toBe('GST34')
    expect((wrapper.vm as unknown as TaxVm).ret?.netTax).toBe(130)
  })

  /**
   * No country pack loaded is a deployment fact, not a failure - the page must
   * say what to do rather than render an empty table.
   */
  it('explains itself when no country pack is loaded', async () => {
    forms.mockResolvedValueOnce([])
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    expect((wrapper.vm as unknown as TaxVm).formOptions.length).toBe(0)
    expect(wrapper.find('.n-alert-stub').exists()).toBe(true)
  })
})
