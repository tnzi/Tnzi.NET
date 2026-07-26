import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Promotions integration test.
 *
 * The page uses TListShell + TTableRenderer (useCrudPage fetch-only) for the
 * list chrome, while keeping a custom NModal + NForm for create/edit and a
 * bridge.deactivate() call for deactivation.
 *
 * Strategy: mock the promotion bridge and the admin client, mount with stubs,
 * then assert list rendering, create-modal open, edit-modal open, deactivation,
 * and active-filter-driven refetch.
 */

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// `useMessage` requires NMessageProvider in the tree - stub it so the page
// can call message.success / error without a real provider in test mounts.
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
    }),
  }
})

const mockGetList = vi.fn(async () => ({
  items: [
    {
      id: 'promo-1',
      promotionCode: 'SAVE10',
      name: 'Save 10%',
      description: 'Ten percent off',
      type: 1,
      discountValue: 10,
      discountType: 1,
      maxDiscountAmount: null,
      minimumOrderAmount: null,
      totalUsageLimit: 100,
      usedCount: 5,
      startTime: '2026-01-01T00:00:00Z',
      endTime: '2026-12-31T23:59:59Z',
      isActive: true,
      isValid: true,
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const mockCreate = vi.fn(async () => ({ id: 'promo-new' }))
const mockUpdate = vi.fn(async () => undefined)
const mockDeactivate = vi.fn(async () => undefined)

vi.mock('../../../src/services/bridges/promotion-bridge', () => ({
  createPromotionBridge: () => ({
    getList: mockGetList,
    getById: vi.fn(async () => null),
    create: mockCreate,
    update: mockUpdate,
    deactivate: mockDeactivate,
  }),
  DiscountType: { Percentage: 1, Fixed: 2 },
  PromotionType: {
    PercentageDiscount: 1,
    FixedAmountDiscount: 2,
    FirstSubscription: 3,
    LimitedTime: 4,
    ThresholdDiscount: 5,
  },
}))

import Promotions from '../../../src/pages/payment/Promotions.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="(data || []).length"></div>',
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
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: {
    name: 'Button',
    props: ['loading', 'disabled'],
    template: '<button @click="$emit(\'click\')"><slot /><slot name="icon" /></button>',
  },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: {
    name: 'Popover',
    props: ['show'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Popconfirm: {
    name: 'Popconfirm',
    emits: ['positiveClick'],
    template: '<div class="n-popconfirm-stub"><slot name="trigger" /><button class="popconfirm-ok" @click="$emit(\'positiveClick\')">OK</button></div>',
  },
  Select: {
    name: 'Select',
    props: ['value', 'options'],
    emits: ['update:value'],
    template: '<select class="n-select-stub" :value="value" @change="$emit(\'update:value\', $event.target.value)"><option v-for="o in (options || [])" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  FormItemGi: { name: 'FormItemGi', template: '<div class="form-item-gi"><slot /></div>' },
  Grid: { name: 'Grid', template: '<div class="n-grid"><slot /></div>' },
  GridItem: { name: 'GridItem', template: '<div class="n-grid-item"><slot /></div>' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  Switch: { name: 'Switch', template: '<button />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Card: {
    name: 'Card',
    template: '<div class="n-card"><slot name="header" /><slot name="header-extra" /><slot /><slot name="footer" /></div>',
  },
  Space: { name: 'Space', template: '<div class="n-space"><slot /></div>' },
  Tag: { name: 'Tag', template: '<span class="n-tag"><slot /></span>' },
  Alert: { name: 'Alert', template: '<div class="n-alert"><slot /></div>' },
}

describe('Promotions page (standard useCrudPage + TCrudPage)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockGetList.mockClear()
    mockCreate.mockClear()
    mockUpdate.mockClear()
    mockDeactivate.mockClear()
  })

  it('mounts without throwing', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    expect(wrapper.exists()).toBe(true)
  })

  it('calls bridge.getList on mount', async () => {
    mount(Promotions, { global: { stubs } })
    await flushPromises()
    expect(mockGetList).toHaveBeenCalledTimes(1)
  })

  it('renders the TCrudPage / TListShell shell', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell').exists()).toBe(true)
  })

  it('renders table via TTableRenderer after fetch', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('1')
  })

  it('create form modal is closed initially', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.n-modal-stub').exists()).toBe(false)
  })

  it('openCreate opens the create form with seeded enum defaults', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openCreate: () => void
      crud: { formModal: { visible: { value: boolean }; mode: { value: string }; formData: { value: Record<string, unknown> } } }
    }
    vm.openCreate()
    await flushPromises()
    expect(vm.crud.formModal.visible.value).toBe(true)
    expect(vm.crud.formModal.mode.value).toBe('create')
    // Required enum fields are pre-seeded so the create form opens valid.
    expect(vm.crud.formModal.formData.value.type).toBeDefined()
    expect(vm.crud.formModal.formData.value.discountType).toBeDefined()
  })

  it('editAction opens the form in edit mode bound to the row', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: { openEdit: (row: Record<string, unknown>) => void; formModal: { mode: { value: string }; formData: { value: Record<string, unknown> } } }
    }
    vm.crud.openEdit({ id: 'promo-1', promotionCode: 'SAVE10', name: 'Save 10%', type: 1, discountType: 1, discountValue: 10, isActive: true, isValid: true })
    await flushPromises()
    expect(vm.crud.formModal.mode.value).toBe('edit')
    expect(vm.crud.formModal.formData.value.promotionCode).toBe('SAVE10')
  })

  it('deactivate calls bridge.deactivate and then refreshes', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { deactivate: (id: string) => Promise<void> }
    await vm.deactivate('promo-1')
    await flushPromises()
    expect(mockDeactivate).toHaveBeenCalledWith('promo-1')
    expect(mockGetList).toHaveBeenCalledTimes(2)
  })

  it('active-filter change calls setFilters + triggers refetch', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { onActiveFilterChange: (v: string | null) => void }
    vm.onActiveFilterChange('active')
    await flushPromises()
    expect(mockGetList).toHaveBeenCalledTimes(2)
    const secondCall = mockGetList.mock.calls[1][0] as { isActive: boolean | null }
    expect(secondCall.isActive).toBe(true)
  })

  it('active-filter "inactive" passes isActive=false', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { onActiveFilterChange: (v: string | null) => void }
    vm.onActiveFilterChange('inactive')
    await flushPromises()
    const secondCall = mockGetList.mock.calls[1][0] as { isActive: boolean | null }
    expect(secondCall.isActive).toBe(false)
  })

  it('active-filter null (cleared) passes isActive=null', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { onActiveFilterChange: (v: string | null) => void }
    vm.onActiveFilterChange(null)
    await flushPromises()
    const secondCall = mockGetList.mock.calls[1][0] as { isActive: boolean | null }
    expect(secondCall.isActive).toBe(null)
  })

  it('submitting a create calls bridge.create and refreshes', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openCreate: () => void
      crud: { formModal: { formData: { value: Record<string, unknown> } }; submit: () => Promise<unknown> }
    }
    vm.openCreate()
    await flushPromises()
    vm.crud.formModal.formData.value.promotionCode = 'NEW20'
    vm.crud.formModal.formData.value.name = 'New Promo'
    vm.crud.formModal.formData.value.discountValue = 20
    await vm.crud.submit()
    await flushPromises()
    expect(mockCreate).toHaveBeenCalledTimes(1)
    expect(mockGetList).toHaveBeenCalledTimes(2)
  })

  it('no default create button (custom #primary slot with seeded defaults)', async () => {
    const wrapper = mount(Promotions, { global: { stubs } })
    await flushPromises()
    // The page overrides #primary with its own button (class t-list-shell__action),
    // so TCrudPage's default create button (t-list-shell__create) is suppressed.
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(false)
  })
})
