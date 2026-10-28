import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))

// Templates.vue calls `useMessage()` directly (like GdprRequests /
// WorkflowEditor) - it requires NMessageProvider in the tree, so stub it.
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }),
  }
})
// Shared with the tests so the preview / test-send requests can be read back.
const templateActions = vi.hoisted(() => ({
  preview: vi.fn(async () => ({ subject: 'Hi', content: 'Body', isHtml: false })),
  sendTest: vi.fn(async () => undefined),
}))

vi.mock('../../../src/services/bridges/notification-bridge', () => ({
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
      preview: templateActions.preview,
      sendTest: templateActions.sendTest,
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
  // props) don't fall through onto the bare <div> - `Element.prefix` is a
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

describe('Templates page (Phase 3.25)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  // Stored Razor runs in the server process on every send and preview, so the backend
  // requires the Template module's code-execution codes on top of the notification ones.
  // A user holding only notification.template.* must not see a create button that 403s.
  async function createButtonFor(permissions: string[]): Promise<boolean> {
    useAdminAuthStore().setUserInfo({ id: 'u1', username: 'tester', roles: [], permissions })
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    return wrapper.find('.t-crud-page__create').exists()
  }

  it('hides create without the Template module code-execution grant', async () => {
    expect(await createButtonFor(['notification.template.view', 'notification.template.create'])).toBe(false)
  })

  it('shows create when both gates are held', async () => {
    expect(await createButtonFor(['notification.template.view', 'notification.template.create', 'template.template.create'])).toBe(true)
  })

  it('mounts without throwing', async () => {
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders a data table on load', async () => {
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

type TemplatesVm = {
  openPreview: (row: Record<string, unknown>) => Promise<void>
  openSendTest: (row: Record<string, unknown>) => void
  runSendTest: () => Promise<void>
  recipientAddress: string
}

describe('Templates page preview and test send', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    templateActions.preview.mockClear()
    templateActions.sendTest.mockClear()
  })

  // An SMS template filed under its own category: the channel comes from the
  // template's `type`, and the category goes along because the server finds a
  // template by name AND category. Guessing the channel from the category
  // previewed and test-sent this template as an email, and without the
  // category the lookup used the channel name and missed it.
  const smsRow = { id: 't1', templateName: 'otp', category: 'Auth', type: 'Sms', module: 'Notification' }

  it('previews a template on its own channel and category', async () => {
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    const vm = wrapper.vm as unknown as TemplatesVm

    await vm.openPreview(smsRow)

    expect(templateActions.preview).toHaveBeenLastCalledWith(
      expect.objectContaining({ templateName: 'otp', category: 'Auth', type: 'Sms' }),
    )
  })

  it('test-sends a template on its own channel and category', async () => {
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    const vm = wrapper.vm as unknown as TemplatesVm

    vm.openSendTest(smsRow)
    // The overlay opens asynchronously; the operator types after it is up.
    await new Promise(r => setTimeout(r, 0))
    vm.recipientAddress = '+15550100'
    await vm.runSendTest()

    expect(templateActions.sendTest).toHaveBeenLastCalledWith(
      expect.objectContaining({ templateName: 'otp', category: 'Auth', type: 'Sms', recipientAddress: '+15550100' }),
    )
  })

  // A Generic template (the store default, and what a file template reports)
  // names no channel; its category still does for the shipped Email / Sms /
  // Push folders. A template filed under its channel name goes without a
  // category, as real sends of it do, so the server's default lookup finds it.
  it('falls back to the category for a template whose type names no channel', async () => {
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    const vm = wrapper.vm as unknown as TemplatesVm

    await vm.openPreview({ id: 't2', templateName: 'alert', category: 'Push', type: 'Generic' })

    expect(templateActions.preview).toHaveBeenLastCalledWith(
      expect.objectContaining({ templateName: 'alert', category: null, type: 'Push' }),
    )
  })

  // Preview and test-send run on the notifications endpoints, which check the
  // message codes (`notification.message.view` / `.create`), not the template
  // codes this page is reached with. A template editor without them got a 403
  // on every Preview and Send test.
  it('★ offers Preview and Send test by the message codes their endpoints check', async () => {
    useAdminAuthStore().setUserInfo({
      id: 'editor', username: 'editor', roles: [],
      permissions: ['notification.template.view', 'notification.template.update'],
    } as never)
    const { default: Templates } = await import('../../../src/pages/notification/Templates.vue')
    const wrapper = mount(Templates, { global: { stubs } })
    await nextTick()
    const actions = (wrapper.vm as unknown as { rowActions: Array<{ key: string; show?: (r: unknown) => boolean }> }).rowActions
    const offered = () => actions.filter((a) => !a.show || a.show(smsRow)).map((a) => a.key)

    expect(offered()).not.toContain('preview')
    expect(offered()).not.toContain('sendTest')

    useAdminAuthStore().setUserInfo({
      id: 'sender', username: 'sender', roles: [],
      permissions: ['notification.template.view', 'notification.message.view', 'notification.message.create'],
    } as never)
    expect(offered()).toContain('preview')
    expect(offered()).toContain('sendTest')
  })
})

describe('notificationTemplateColumns config', () => {
  it('exports columns with required keys', async () => {
    // Fields aligned with backend TemplateInfoDto (Tnzi.Template):
    // templateName / category / description / defaultLayoutName / isActive / source.
    const { notificationTemplateColumns } = await import('../../../src/pages/notification/template-config')
    const keys = notificationTemplateColumns.map((c) => c.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('category')
    expect(keys).toContain('isActive')
    expect(keys).toContain('source')
  })

  it('exports formSchema with required fields', async () => {
    const { notificationTemplateFormSchema } = await import('../../../src/pages/notification/template-config')
    const keys = notificationTemplateFormSchema.map((f) => f.key)
    expect(keys).toContain('templateName')
    expect(keys).toContain('category')
    expect(keys).toContain('contentTemplate')
    expect(keys).toContain('isActive')
  })
})
