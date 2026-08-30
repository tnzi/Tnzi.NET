import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const safeMessageMock = vi.hoisted(() => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }))

const bridgeMock = vi.hoisted(() => ({
  cancel: vi.fn(async () => undefined),
  batchCancel: vi.fn(async () => 2),
  getDeliveryReport: vi.fn(async () => ({
    messageId: 'm3',
    subject: 'Quarterly newsletter',
    type: 'Email',
    totalRecipients: 3,
    sentCount: 1,
    failedCount: 1,
    pendingCount: 1,
    readCount: 0,
    successRate: 33.3,
    recipients: [
      { id: 'r1', address: 'a@example.com', status: 'Sent', sentTime: '2026-01-03T00:00:00Z', isRead: false },
      { id: 'r2', address: 'b@example.com', status: 'Failed', failureReason: 'mailbox unavailable', isRead: false },
      { id: 'r3', address: 'c@example.com', status: 'Cancelled', failureReason: 'Recipient reached the hourly limit', isRead: false },
    ],
  })),
}))

vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => safeMessageMock }))
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({
    messages: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'm1', subject: 'Welcome aboard', type: 'Email', status: 'Sent', totalRecipientCount: 3, successCount: 3, failureCount: 0, creationTime: '2026-01-01T00:00:00Z', failureReason: null },
          { id: 'm2', subject: 'Disk almost full', type: 'Sms', status: 'Failed', totalRecipientCount: 1, successCount: 0, failureCount: 1, creationTime: '2026-01-02T00:00:00Z', failureReason: 'Connection timeout' },
          { id: 'm3', subject: 'Quarterly newsletter', type: 'Email', status: 'Scheduled', totalRecipientCount: 3, successCount: 0, failureCount: 0, creationTime: '2026-01-03T00:00:00Z', failureReason: null },
        ],
        totalCount: 3,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => { throw new Error('read-only') }),
      update: vi.fn(async () => { throw new Error('read-only') }),
      delete: vi.fn(async () => undefined),
      send:   vi.fn(async () => undefined),
      cancel: bridgeMock.cancel,
      batchCancel: bridgeMock.batchCancel,
      getDeliveryReport: bridgeMock.getDeliveryReport,
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
    expect(wrapper.findAll('.t-item-card')).toHaveLength(3)
    expect(wrapper.text()).toContain('Welcome aboard')
    expect(wrapper.find('.nm-error').text()).toContain('Connection timeout')
  })

  // Cancel stops a send before it goes out. Offering it on a message already
  // delivered would be a promise the backend cannot keep.
  it('offers Cancel only while the send can still be stopped', async () => {
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as {
      rowActions: Array<{ key: string; show?: (row: unknown) => boolean }>
    }
    const cancel = vm.rowActions.find((a) => a.key === 'cancel')!
    expect(cancel).toBeTruthy()

    expect(cancel.show!({ status: 'Scheduled' })).toBe(true)
    expect(cancel.show!({ status: 'Pending' })).toBe(true)
    expect(cancel.show!({ status: 'Sending' })).toBe(true)
    expect(cancel.show!({ status: 'Sent' })).toBe(false)
    expect(cancel.show!({ status: 'Failed' })).toBe(false)
    expect(cancel.show!({ status: 'Cancelled' })).toBe(false)
  })

  it('cancel and batch cancel reach the bridge', async () => {
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as {
      rowActions: Array<{ key: string; onClick?: (row: unknown) => unknown }>
      batchCancel: (ids: string[]) => Promise<void>
    }
    await vm.rowActions.find((a) => a.key === 'cancel')!.onClick!({ id: 'm3', status: 'Scheduled' })
    expect(bridgeMock.cancel).toHaveBeenCalledWith('m3')

    await vm.batchCancel(['m1', 'm3'])
    expect(bridgeMock.batchCancel).toHaveBeenCalledWith(['m1', 'm3'])
  })

  // A server that refuses a cancel (the message just went out; someone else got
  // there first) is a normal outcome, and the operator has to be told which.
  it('surfaces the server refusal instead of swallowing it', async () => {
    bridgeMock.cancel.mockRejectedValueOnce(new Error('Notification has already been sent') as never)
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as {
      rowActions: Array<{ key: string; onClick?: (row: unknown) => unknown }>
    }
    await expect(
      vm.rowActions.find((a) => a.key === 'cancel')!.onClick!({ id: 'm3', status: 'Scheduled' })
    ).resolves.toBeUndefined()
    expect(safeMessageMock.error).toHaveBeenCalledWith('Notification has already been sent')
  })

  // A bulk send's counters answer "how many"; only the report answers
  // "which recipients, and why".
  it('loads the per-recipient delivery report when a send is opened', async () => {
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()

    // Go through openView, not loadReport directly: the thing that can silently
    // break here is the onView wiring, and calling the loader by hand would not
    // notice it missing.
    const vm = wrapper.vm as unknown as {
      crud: { openView: (row: unknown) => void }
      report: { recipients: Array<{ failureReason?: string }> } | null
      reportError: string | null
    }
    vm.crud.openView({ id: 'm3', status: 'Scheduled' })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    expect(bridgeMock.getDeliveryReport).toHaveBeenCalledWith('m3')
    expect(vm.report?.recipients).toHaveLength(3)
    expect(vm.report?.recipients[1].failureReason).toContain('mailbox unavailable')
    expect(vm.reportError).toBeNull()
  })

  // A slow report for row A must not land on top of row B's. The wrong
  // recipients under the right title reads entirely like the truth.
  it('discards a report that arrives after the drawer moved on', async () => {
    let releaseFirst: (v: unknown) => void = () => {}
    bridgeMock.getDeliveryReport
      .mockImplementationOnce(() => new Promise((r) => { releaseFirst = r }) as never)
      .mockResolvedValueOnce({ messageId: 'm1', recipients: [{ id: 'z', address: 'second@example.com' }] } as never)

    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as {
      crud: { openView: (row: unknown) => void }
      report: { recipients: Array<{ address: string }> } | null
    }
    vm.crud.openView({ id: 'm3', status: 'Scheduled' })
    await nextTick()
    vm.crud.openView({ id: 'm1', status: 'Sent' })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    // The first request now answers, late.
    releaseFirst({ messageId: 'm3', recipients: [{ id: 'a', address: 'stale@example.com' }] })
    await new Promise((r) => setTimeout(r, 10))

    expect(vm.report?.recipients[0].address).toBe('second@example.com')
  })

  // The rest of the drawer is still worth reading when the report is the part
  // that failed.
  it('a failed report degrades to a message instead of taking the drawer down', async () => {
    bridgeMock.getDeliveryReport.mockRejectedValueOnce(new Error('report unavailable') as never)
    const { default: Messages } = await import('../../../src/pages/notification/Messages.vue')
    const wrapper = mount(Messages, { global: { stubs } })
    await nextTick()

    const vm = wrapper.vm as unknown as {
      loadReport: (row: unknown) => Promise<void>
      report: unknown
      reportError: string | null
    }
    await expect(vm.loadReport({ id: 'm3' })).resolves.toBeUndefined()
    expect(vm.report).toBeNull()
    expect(vm.reportError).toContain('report unavailable')
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
