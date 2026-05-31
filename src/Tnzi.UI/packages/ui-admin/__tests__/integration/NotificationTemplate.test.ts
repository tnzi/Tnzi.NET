import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))

// NotificationTemplate.vue calls `useMessage()` directly (like GdprRequests /
// WorkflowEditor) — it requires NMessageProvider in the tree, so stub it.
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }),
  }
})
vi.mock('../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({
    messages: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => { throw new Error('read-only') }),
      update: vi.fn(async () => { throw new Error('read-only') }),
      delete: vi.fn(async () => undefined),
      send: vi.fn(async () => undefined),
    },
    templates: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
      preview: vi.fn(async () => '<p>Preview content</p>'),
    },
    subscriptions: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
    },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  // inheritAttrs:false so TCrudPage's `prefix` render-fn (and other NPagination
  // props) don't fall through onto the bare <div> — `Element.prefix` is a
  // read-only DOM property and assigning a function to it throws.
  Pagination: { inheritAttrs: false, template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button />' },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
}

describe('NotificationTemplate page (Phase 3.25)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: NotificationTemplate } = await import('../../src/pages/notification/NotificationTemplate.vue')
    const wrapper = mount(NotificationTemplate, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders a data table on load', async () => {
    const { default: NotificationTemplate } = await import('../../src/pages/notification/NotificationTemplate.vue')
    const wrapper = mount(NotificationTemplate, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('notificationTemplateColumns config', () => {
  it('exports columns with required keys', async () => {
    // Fields aligned with backend TemplateInfoDto (Tnzi.Template):
    // templateName / category / description / defaultLayoutName / isActive / source.
    const { notificationTemplateColumns } = await import('../../src/pages/notification/notification-template-config')
    const keys = notificationTemplateColumns.map((c) => c.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('category')
    expect(keys).toContain('isActive')
    expect(keys).toContain('source')
  })

  it('exports formSchema with required fields', async () => {
    const { notificationTemplateFormSchema } = await import('../../src/pages/notification/notification-template-config')
    const keys = notificationTemplateFormSchema.map((f) => f.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('category')
    expect(keys).toContain('contentTemplate')
    expect(keys).toContain('isActive')
  })
})
