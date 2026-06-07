import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/template-bridge', () => ({
  createTemplateBridge: () => ({
    templates: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => ({})),
      update: vi.fn(async () => ({})),
      delete: vi.fn(async () => undefined),
      render: vi.fn(async () => ''),
      clone: vi.fn(async () => ({})),
    },
    layouts: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'l1',
            layoutName: 'default-layout',
            module: 'Identity',
            category: 'user',
            isActive: true,
            isDefault: true,
            description: null,
            creationTime: '2026-01-01T00:00:00Z',
            lastModificationTime: null,
          },
        ],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => ({ id: 'l2', layoutName: 'new-layout' })),
      update: vi.fn(async () => ({ id: 'l1', layoutName: 'updated-layout' })),
      delete: vi.fn(async () => undefined),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: {} }),
}))

const stubs = {
  DataTable:   { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination:  { template: '<div />' },
  Input:       { props: ['value'], template: '<input />' },
  Button:      { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal:       { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover:     { template: '<div><slot name="trigger" /></div>' },
  Checkbox:    { template: '<input type="checkbox" />' },
  Form:        { template: '<form><slot /></form>' },
  FormItem:    { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch:      { template: '<button />' },
  Select:      { template: '<select />' },
  DatePicker:  { template: '<input type="date" />' },
}

describe('Layouts page (Phase 3.37)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Layouts } = await import('../../../src/pages/template/Layouts.vue')
    const wrapper = mount(Layouts, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders data table after fetch', async () => {
    const { default: Layouts } = await import('../../../src/pages/template/Layouts.vue')
    const wrapper = mount(Layouts, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('layoutColumns config', () => {
  it('exports columns with required backend field keys', async () => {
    const { layoutColumns } = await import('../../../src/pages/template/layout-config')
    const keys = layoutColumns.map((c) => c.key)
    expect(keys).toContain('layoutName')
    expect(keys).toContain('module')
    expect(keys).toContain('category')
    expect(keys).toContain('isActive')
  })

  it('exports formSchema with required fields', async () => {
    const { layoutFormSchema } = await import('../../../src/pages/template/layout-config')
    const keys = layoutFormSchema.map((f) => f.key)
    expect(keys).toContain('layoutName')
    expect(keys).toContain('layoutContent')
    expect(keys).toContain('isActive')
  })
})
