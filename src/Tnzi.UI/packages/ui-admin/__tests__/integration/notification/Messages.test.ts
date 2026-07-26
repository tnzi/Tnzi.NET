import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({
    messages: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'm1', subject: 'Welcome aboard', type: 'Email', status: 'Sent', totalRecipientCount: 3, successCount: 3, failureCount: 0, creationTime: '2026-01-01T00:00:00Z', failureReason: null },
          { id: 'm2', subject: 'Disk almost full', type: 'Sms', status: 'Failed', totalRecipientCount: 1, successCount: 0, failureCount: 1, creationTime: '2026-01-02T00:00:00Z', failureReason: 'Connection timeout' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => { throw new Error('read-only') }),
      update: vi.fn(async () => { throw new Error('read-only') }),
      delete: vi.fn(async () => undefined),
      send:   vi.fn(async () => undefined),
    },
    templates: {
      fetch:   vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create:  vi.fn(async () => { throw new Error('backend gap') }),
      update:  vi.fn(async () => { throw new Error('backend gap') }),
      delete:  vi.fn(async () => { throw new Error('backend gap') }),
      preview: vi.fn(async () => ''),
    },
    subscriptions: {
      fetch:  vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
    },
  }),
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

describe('Messages page (Phase 3.26)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-list-shell').exists()).toBe(true)
  })

  // Sends render as document rows: the subject leads and a delivery failure is
  // visible inline instead of only inside the view drawer.
  it('renders one row card per send, with the failure reason inline', async () => {
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.findAll('.t-item-card')).toHaveLength(2)
    expect(wrapper.text()).toContain('Welcome aboard')
    expect(wrapper.find('.nm-error').text()).toContain('Connection timeout')
  })
})

describe('notificationMessageColumns config', () => {
  it('exports columns with required keys', async () => {
    // Fields aligned with backend NotificationInfo (Tnzi.Notification):
    // subject / type / status / templateName / recipients / failureReason / retryCount / sentTime.
    const { notificationMessageColumns } = await import('../../../src/pages/notification/message-config')
    const keys = notificationMessageColumns.map((c) => c.key)
    expect(keys).toContain('subject')
    expect(keys).toContain('type')
    expect(keys).toContain('status')
    expect(keys).toContain('sentTime')
  })

  it('exports formSchema with required fields', async () => {
    const { notificationMessageFormSchema } = await import('../../../src/pages/notification/message-config')
    const keys = notificationMessageFormSchema.map((f) => f.key)
    expect(keys).toContain('subject')
    expect(keys).toContain('content')
    expect(keys).toContain('failureReason')
  })
})
