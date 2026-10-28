import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Payroll Setup page - TTabsPage with components / structures / brackets tabs.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/payroll/setup', fullPath: '/admin/payroll/setup', hash: '', name: 'payroll.setup', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchComponents = vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }))
const listSection = () => ({
  fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
  getById: vi.fn(async () => null),
  create: vi.fn(), update: vi.fn(), delete: vi.fn(), resolve: vi.fn(),
})
const fetchPacks = vi.fn(async () => ({
  items: [{ code: 'US', displayName: 'United States', description: null }],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))
const seedPack = vi.fn(async () => ({ componentsSeeded: 3, bracketTablesSeeded: 1 }))

vi.mock('../../../src/services/bridges/payroll-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ...original,
    createPayrollBridge: () => ({
      components: { fetch: fetchComponents, create: vi.fn(), update: vi.fn(), delete: vi.fn() },
      structures: listSection(),
      brackets: listSection(),
      countryPacks: { fetch: fetchPacks, seed: seedPack },
    }),
  }
})

import Page from '../../../src/pages/payroll/Setup.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  Select: { name: 'Select', template: '<select />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div><slot /></div>' },
}

describe('Payroll Setup page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchComponents.mockClear()
  })

  it('mounts and loads the components tab', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchComponents.mock.calls.length).toBeGreaterThan(0)
  })

  it('loads registered country packs on mount and can seed one', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchPacks).toHaveBeenCalled()

    const vm = wrapper.vm as unknown as {
      packsCrud: { items: { value?: Array<{ code: string }> } | Array<{ code: string }> }
      seedPack: (row: { code: string }) => Promise<void>
    }
    const items = Array.isArray(vm.packsCrud.items) ? vm.packsCrud.items : (vm.packsCrud.items.value ?? [])
    expect(items.map((p) => p.code)).toEqual(['US'])

    // seed 成功后必须刷新组件/税级表列表(播种产物否则陈旧,阻断「播种→建结构」)
    fetchComponents.mockClear()
    await vm.seedPack({ code: 'US' })
    expect(seedPack).toHaveBeenCalledWith('US')
    expect(fetchComponents.mock.calls.length).toBeGreaterThan(0)
  })

  // The structure line editor picks from a component list read once on mount.
  // A component created on the Components tab was missing from it until the
  // page was reloaded; editing or deleting one left the stale entry behind.
  it.each(['create', 'update', 'delete'] as const)(
    '★ the structure editor picks up a component after a %s on the Components tab',
    async (kind) => {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as {
        componentCrud: {
          openCreate: () => void
          openEdit: (row: unknown) => void
          submit: () => Promise<unknown>
          formModal: { formData: { value: Record<string, unknown> | null } }
          remove?: (row: unknown) => Promise<unknown>
        }
        componentActions: Array<{ key: string; onClick?: (row: unknown) => unknown }>
        sources: { componentList: { value: Array<{ code: string }> } }
      }
      const bonus = { id: 'c9', code: 'BONUS', name: 'Bonus', type: 'Earning', isActive: true }
      fetchComponents.mockImplementation((async () => ({ items: [bonus], totalCount: 1, pageIndex: 1, pageSize: 100 })) as never)

      try {
        if (kind === 'create') {
          vm.componentCrud.openCreate()
          vm.componentCrud.formModal.formData.value!.code = 'BONUS'
          vm.componentCrud.formModal.formData.value!.name = 'Bonus'
          await vm.componentCrud.submit()
        } else if (kind === 'update') {
          vm.componentCrud.openEdit(bonus)
          await vm.componentCrud.submit()
        } else {
          await vm.componentActions.find((a) => a.key === 'delete')!.onClick!(bonus)
        }
        await flushPromises()

        expect(vm.sources.componentList.value.map((c) => c.code)).toEqual(['BONUS'])
      } finally {
        fetchComponents.mockImplementation(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }))
        wrapper.unmount()
      }
    },
  )
})
