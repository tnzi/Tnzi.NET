import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

// naive's message handle, captured so a test can read what the page toasts.
const toast = vi.hoisted(() => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }))
vi.mock('naive-ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('naive-ui')>()),
  useMessage: () => toast,
}))

const mockRenderFn = vi.fn(async (..._args: unknown[]) => '<p>Hello World</p>')
const mockCloneFn = vi.fn(async (..._args: unknown[]) => ({ id: 't2', templateName: 'welcome-email-copy' }))

// A FileSystem-source row: a .cshtml shipped with the binaries, listed with
// the empty Guid and its body inline.
const shippedRow = {
  id: '00000000-0000-0000-0000-000000000000',
  templateName: 'password-reset',
  module: 'Identity',
  category: 'Email',
  isActive: true,
  description: null,
  source: 'FileSystem',
  isReadOnly: true,
  contentTemplate: '<p>Reset</p>',
  creationTime: '2026-01-01T00:00:00Z',
  lastModificationTime: null,
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/template-bridge', () => ({
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
          shippedRow,
        ],
        totalCount: 2,
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

describe('Templates page (Phase 3.36)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Templates } = await import('../../../src/pages/template/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-list-shell').exists()).toBe(true)
  })

  // A template is content, so the library renders tiles showing a slice of the
  // body rather than a table of its metadata.
  it('renders one card per template, showing the body excerpt', async () => {
    const { default: Templates } = await import('../../../src/pages/template/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.findAll('.tpl-card').length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('welcome-email')
  })

  // A shipped template has no stored id to render by: the card hands the bridge
  // the row, whose body is what gets rendered.
  it('★ clicking a shipped template renders the row it shows', async () => {
    mockRenderFn.mockClear()
    const { default: Templates } = await import('../../../src/pages/template/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await flushPromises()
    const cards = wrapper.findAll('.tpl-card')
    expect(cards).toHaveLength(2)
    await cards[1]!.trigger('click')
    await flushPromises()
    expect(mockRenderFn).toHaveBeenCalledWith(expect.objectContaining({ templateName: 'password-reset', contentTemplate: '<p>Reset</p>' }), {})
  })

  // A body that does not compile used to read "No preview content available."
  it('★ a template that does not render shows the reason in the preview', async () => {
    mockRenderFn.mockRejectedValueOnce(new Error("The name 'Foo' does not exist in the current context"))
    const { default: Templates } = await import('../../../src/pages/template/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await flushPromises()
    await wrapper.findAll('.tpl-card')[0]!.trigger('click')
    await flushPromises()
    const vm = wrapper.vm as unknown as { previewError: string; previewContent: string }
    expect(vm.previewError).toBe("The name 'Foo' does not exist in the current context")
    expect(vm.previewContent).toBe('')
  })

  // Clone went through a bare catch: a copy name already taken, or a shipped
  // template the endpoint could not copy, left no trace at all.
  it('★ a refused clone reports the reason', async () => {
    toast.error.mockClear()
    toast.success.mockClear()
    mockCloneFn.mockRejectedValueOnce(new Error("Template with name 'welcome-email-copy' already exists"))
    const { default: Templates } = await import('../../../src/pages/template/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { handleClone: (row: unknown) => Promise<void> }
    await vm.handleClone({ id: 't1', templateName: 'welcome-email' })
    await flushPromises()
    expect(toast.error).toHaveBeenCalledWith("Template with name 'welcome-email-copy' already exists")
    expect(toast.success).not.toHaveBeenCalled()
  })

  it('★ clone hands the bridge the row and names the copy it made', async () => {
    toast.error.mockClear()
    toast.success.mockClear()
    mockCloneFn.mockClear()
    mockCloneFn.mockResolvedValueOnce({ id: 't9', templateName: 'password-reset-copy' })
    const { default: Templates } = await import('../../../src/pages/template/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { handleClone: (row: unknown) => Promise<void> }
    await vm.handleClone(shippedRow)
    await flushPromises()
    expect(mockCloneFn).toHaveBeenCalledWith(shippedRow)
    expect(toast.success).toHaveBeenCalledWith('Cloned as password-reset-copy')
    expect(toast.error).not.toHaveBeenCalled()
  })
})

describe('templateColumns config', () => {
  it('exports columns with required backend field keys', async () => {
    const { templateColumns } = await import('../../../src/pages/template/template-config')
    const keys = templateColumns.map((c) => c.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('module')
    expect(keys).toContain('category')
    expect(keys).toContain('isActive')
  })

  it('exports formSchema with required fields', async () => {
    const { templateFormSchema } = await import('../../../src/pages/template/template-config')
    const keys = templateFormSchema.map((f) => f.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('contentTemplate')
    expect(keys).toContain('isActive')
  })
})
