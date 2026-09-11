import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Feature flags page - three tabs (definitions / values / usage) over one route.
 *
 * All three tabs stay mounted (`displayDirective: 'show'`), so one mount
 * exercises every bridge sub-contract. The assertions that matter most are the
 * ones about scope: the values tab must land on a scope that can actually be
 * written to, and a set must post the WIRE string the backend validates.
 */
const definitions = [
  { id: 'd1', name: 'Orders.AdvancedSearch', displayName: 'Advanced search', valueType: 'Boolean', defaultValue: 'false', isEnabled: true, group: 'Orders', source: 'Database', isReadOnly: false },
  { id: '00000000-0000-0000-0000-000000000000', name: 'Core.Builtin', displayName: 'Built-in', valueType: 'Integer', defaultValue: '10', isEnabled: true, group: 'Core', source: 'Code', isReadOnly: true },
]

const providers = vi.fn(async () => [
  { name: 'Tenant', priority: 200, requiresKey: true, isActive: false, inactiveReason: 'multi-tenancy is disabled' },
  { name: 'Global', priority: 100, requiresKey: false, isActive: true, inactiveReason: null },
])
const all = vi.fn(async (_scope: unknown, q: { pageIndex: number; pageSize: number }) => ({
  items: [
    { id: 'v1', featureDefinitionId: 'd1', featureName: 'Orders.AdvancedSearch', displayName: 'Advanced search', group: 'Orders', valueType: 'Boolean', defaultValue: 'false', effectiveValue: 'true', isExplicitlySet: true, effectiveSource: 'Explicit', effectiveProvider: 'Global', isEnabled: true, source: 'Database', canOverride: true },
    { id: '00000000-0000-0000-0000-000000000000', featureDefinitionId: '00000000-0000-0000-0000-000000000000', featureName: 'Core.Builtin', displayName: 'Built-in', group: 'Core', valueType: 'Integer', defaultValue: '10', effectiveValue: '10', isExplicitlySet: false, effectiveSource: 'Default', effectiveProvider: null, isEnabled: true, source: 'Code', canOverride: false },
  ],
  totalCount: 2,
  pageIndex: q.pageIndex,
  pageSize: q.pageSize,
}))
const set = vi.fn(async () => ({ id: 'v1', featureDefinitionId: 'd1', providerName: 'Global', providerKey: null, value: 'true' }))
const clear = vi.fn(async () => undefined)

const mostUsed = vi.fn(async () => [
  { featureName: 'Orders.AdvancedSearch', checkCount: 42, uniqueUsers: 7, enableRate: 66.7 },
])
const stats = vi.fn(async () => ({
  featureName: 'Orders.AdvancedSearch', totalChecks: 42, uniqueUsers: 7, enableRate: 66.7,
  firstUsed: '2026-08-01T00:00:00Z', lastUsed: '2026-09-01T00:00:00Z',
}))
const trend = vi.fn(async () => [
  { period: '2026-08-30', checkCount: 20, enableCount: 15, disableCount: 5 },
  { period: '2026-08-31', checkCount: 22, enableCount: 13, disableCount: 9 },
])
const cleanup = vi.fn(async () => 12)

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/feature-bridge', () => ({
  createFeatureBridge: () => ({
    definitions: {
      fetch: vi.fn(async (q: { pageIndex: number; pageSize: number }) => ({
        items: definitions, totalCount: definitions.length, pageIndex: q.pageIndex, pageSize: q.pageSize,
      })),
      create: vi.fn(async (d: unknown) => ({ id: 'd9', ...(d as object) })),
      update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
      delete: vi.fn(async () => undefined),
    },
    values: { providers, all, set, clear },
    usage: { mostUsed, stats, trend, cleanup },
  }),
}))

import Features from '../../../src/pages/system/Features.vue'
import FeatureValuesTab from '../../../src/pages/system/features/FeatureValuesTab.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="data.length"></div>',
  },
  Pagination: { name: 'Pagination', props: ['page', 'itemCount', 'pageSize'], template: '<div />' },
  Input: { name: 'Input', props: ['value'], emits: ['update:value'], template: '<input class="n-input-stub" :value="value" />' },
  InputNumber: { name: 'InputNumber', props: ['value'], emits: ['update:value'], template: '<input type="number" />' },
  Switch: { name: 'Switch', props: ['value'], emits: ['update:value'], template: '<button class="n-switch-stub" />' },
  Select: {
    name: 'Select',
    props: ['value', 'options'],
    emits: ['update:value'],
    template: '<select class="n-select-stub" :data-options="JSON.stringify(options)" :data-value="value" />',
  },
  DatePicker: { name: 'DatePicker', props: ['value'], emits: ['update:value'], template: '<input type="date" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], emits: ['update:show'], template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>' },
  Popover: { name: 'Popover', props: ['show'], template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Empty: { name: 'Empty', props: ['description'], template: '<div class="n-empty-stub">{{ description }}</div>' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

describe('Features page (definitions / values / usage tabs)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('renders the three tabs and one card per definition', async () => {
    const wrapper = mount(Features, { global: { stubs } })
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('Definitions')
    expect(text).toContain('Values')
    expect(text).toContain('Usage')
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
    expect(text).toContain('Orders.AdvancedSearch')
  })

  it('values tab lands on the first ACTIVE keyless scope, never on an inactive one', async () => {
    const wrapper = mount(Features, { global: { stubs } })
    await flushPromises()
    expect(providers).toHaveBeenCalled()
    // Tenant is listed first (higher priority) but inactive; Global is what loads.
    expect(all).toHaveBeenCalled()
    const [scope] = all.mock.calls[0] as unknown as [{ providerName: string; providerKey: string | null }]
    expect(scope.providerName).toBe('Global')
    expect(scope.providerKey).toBeNull()
    // The inactive scope is still offered, disabled, with its reason in the label.
    const selects = wrapper.findAll('.n-select-stub')
    const scopeSelect = selects.find((s) => (s.attributes('data-options') ?? '').includes('Tenant'))
    expect(scopeSelect).toBeDefined()
    const options = JSON.parse(scopeSelect!.attributes('data-options')!) as Array<{ value: string; disabled: boolean; label: string }>
    const tenant = options.find((o) => o.value === 'Tenant')!
    expect(tenant.disabled).toBe(true)
    expect(tenant.label).toContain('inactive')
    expect(options.find((o) => o.value === 'Global')!.disabled).toBe(false)
  })

  it('setting a value posts the WIRE string for the applied scope', async () => {
    const wrapper = mount(Features, { global: { stubs } })
    await flushPromises()
    const values = wrapper.findComponent(FeatureValuesTab)
    const crud = (values.vm as unknown as { crud: any }).crud
    expect(crud.items.value).toHaveLength(2)

    // The typed seed: a Boolean row edits as a real boolean...
    const row = crud.items.value[0]
    expect(row.value).toBe(true)
    crud.openEdit(row)
    crud.formModal.formData.value = { ...row, value: false }
    await crud.submit()
    await flushPromises()

    // ...and goes back over the wire as the string the backend validates.
    expect(set).toHaveBeenCalledWith({
      featureDefinitionId: 'd1',
      providerName: 'Global',
      providerKey: null,
      value: 'false',
    })
  })

  it('usage tab loads the ranking and drills into the top feature', async () => {
    const wrapper = mount(Features, { global: { stubs } })
    await flushPromises()
    expect(mostUsed).toHaveBeenCalledWith(10, expect.objectContaining({ from: expect.any(String), to: expect.any(String) }))
    expect(stats).toHaveBeenCalledWith('Orders.AdvancedSearch', expect.any(Object))
    expect(trend).toHaveBeenCalledWith('Orders.AdvancedSearch', 'daily', expect.any(Object))
    const text = wrapper.text()
    expect(text).toContain('66.7%')
    expect(text).toContain('Unique users')
  })
})
