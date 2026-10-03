import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

/**
 * Opt-outs page - the admin read/register/revoke surface over the
 * address-keyed suppression list. Before it existed the OptOut table had
 * writers (the one-click link, the send-path filter) and no reader at all.
 */

const optOutsFetch = vi.fn(async () => ({
  items: [
    { id: 'o1', address: 'gone@example.com', channel: 'Email', category: null, source: 'one-click link', creationTime: '2026-09-01T00:00:00Z' },
    { id: 'o2', address: '9055551234', channel: 'Fax', category: 'marketing', source: 'admin:u1', reason: 'call', creationTime: '2026-09-02T00:00:00Z' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({
    optOuts: {
      fetch: optOutsFetch,
      create: vi.fn(async () => ({ id: 'o3', address: 'new@example.com', channel: 'Email', creationTime: '2026-09-20T00:00:00Z' })),
      update: vi.fn(async () => { throw new Error('no editable fields') }),
      delete: vi.fn(async () => undefined),
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

describe('OptOuts page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    optOutsFetch.mockClear()
  })

  it('mounts and renders the suppression list', async () => {
    const { default: OptOuts } = await import('../../../src/pages/notification/OptOuts.vue')
    const wrapper = mount(OptOuts, { global: { stubs } })
    await nextTick()
    await flushPromises()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(optOutsFetch).toHaveBeenCalled()
  })

  it('★ the channel filter drives the fetch query', async () => {
    const { default: OptOuts } = await import('../../../src/pages/notification/OptOuts.vue')
    const wrapper = mount(OptOuts, { global: { stubs } })
    await flushPromises()
    optOutsFetch.mockClear()

    ;(wrapper.vm as unknown as { onChannelChange: (v: string) => void }).onChannelChange('Sms')
    await flushPromises()

    const query = (optOutsFetch.mock.calls.at(-1) as unknown[])[0] as { filters: Record<string, unknown> }
    expect(query.filters.channel).toBe('Sms')

    ;(wrapper.vm as unknown as { onChannelChange: (v: string) => void }).onChannelChange('')
    await flushPromises()
    const cleared = (optOutsFetch.mock.calls.at(-1) as unknown[])[0] as { filters: Record<string, unknown> }
    expect(cleared.filters.channel).toBeUndefined()
  })

  it('offers create and delete but never edit: an opt-out row has no editable fields', async () => {
    const { default: OptOuts } = await import('../../../src/pages/notification/OptOuts.vue')
    const wrapper = mount(OptOuts, { global: { stubs } })
    await flushPromises()
    const crud = (wrapper.vm as unknown as { crud: { canCreate: { value: boolean } | boolean; canUpdate: { value: boolean } | boolean; canDelete: { value: boolean } | boolean } }).crud
    const read = (v: { value: boolean } | boolean) => (typeof v === 'object' ? v.value : v)
    expect(read(crud.canCreate)).toBe(true)
    expect(read(crud.canDelete)).toBe(true)
    expect(read(crud.canUpdate)).toBe(false)
  })
})

describe('notificationOptOutColumns config', () => {
  it('exports columns aligned to OptOutDto', async () => {
    const { notificationOptOutColumns, notificationOptOutFormSchema } = await import('../../../src/pages/notification/opt-out-config')
    const keys = notificationOptOutColumns.map((c) => c.key)
    for (const k of ['address', 'channel', 'category', 'source', 'creationTime']) {
      expect(keys).toContain(k)
    }
    // Only the four delivering channels are offered; a row for a channel that
    // never sends would never be consulted.
    const channel = notificationOptOutFormSchema.find((f) => f.key === 'channel')
    expect(channel?.options?.map((o) => o.value)).toEqual(['Email', 'Sms', 'Push', 'Fax'])
  })
})
