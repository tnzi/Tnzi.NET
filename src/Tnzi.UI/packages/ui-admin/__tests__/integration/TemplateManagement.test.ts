import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const mockRenderFn = vi.fn(async () => '<p>Hello World</p>')
const mockCloneFn = vi.fn(async () => ({ id: 't2', templateName: 'welcome-email-copy' }))

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/template-bridge', () => ({
  createTemplateBridge: () => ({
    templates: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 't1',
            templateName: 'welcome-email',
            module: 'Identity',
            category: 'user',
            defaultLayoutName: 'default-layout',
            isActive: true,
            description: null,
            creationTime: '2026-01-01T00:00:00Z',
            lastModificationTime: null,
          },
        ],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => ({ id: 't2', templateName: 'new-template' })),
      update: vi.fn(async () => ({ id: 't1', templateName: 'updated' })),
      delete: vi.fn(async () => undefined),
      render: mockRenderFn,
      clone: mockCloneFn,
    },
    layouts: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => ({})),
      update: vi.fn(async () => ({})),
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

describe('TemplateManagement page (Phase 3.36)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: TemplateManagement } = await import('../../src/pages/template/TemplateManagement.vue')
    const wrapper = mount(TemplateManagement, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders data table after fetch', async () => {
    const { default: TemplateManagement } = await import('../../src/pages/template/TemplateManagement.vue')
    const wrapper = mount(TemplateManagement, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('templateColumns config', () => {
  it('exports columns with required backend field keys', async () => {
    const { templateColumns } = await import('../../src/pages/template/template-config')
    const keys = templateColumns.map((c) => c.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('module')
    expect(keys).toContain('category')
    expect(keys).toContain('isActive')
  })

  it('exports formSchema with required fields', async () => {
    const { templateFormSchema } = await import('../../src/pages/template/template-config')
    const keys = templateFormSchema.map((f) => f.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('contentTemplate')
    expect(keys).toContain('isActive')
  })
})
