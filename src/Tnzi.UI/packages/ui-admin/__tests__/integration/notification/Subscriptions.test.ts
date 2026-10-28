import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({
    messages: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
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
      fetch:  vi.fn(async () => ({
        items: [
          { id: 's1', userId: 'u1', channel: 'Email', category: 'Marketing', isEnabled: true },
          { id: 's2', userId: 'u2', channel: 'Sms',   category: null,        isEnabled: false },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => ({ id: 's3', userId: 'u3', channel: 'InApp', isEnabled: true })),
      update: vi.fn(async () => ({ id: 's1', userId: 'u1', channel: 'Email', isEnabled: false })),
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

describe('Subscriptions page (Phase 3.27)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Subscriptions } = await import('../../../src/pages/notification/Subscriptions.vue')
    const wrapper = mount(Subscriptions, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders a data table on load', async () => {
    const { default: Subscriptions } = await import('../../../src/pages/notification/Subscriptions.vue')
    const wrapper = mount(Subscriptions, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('Subscriptions page - editing a row', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  // A preference is upserted by (user, channel, category), not by its id. Editing
  // any of the three saved a second row and left the original untouched; on edit
  // they are shown, not edited.
  const formStubs = {
    ...stubs,
    Modal: { props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
    Input: { props: ['value', 'disabled'], template: '<input :value="value" :disabled="disabled" />' },
    TUserSelector: { props: ['value', 'disabled'], template: '<input class="user-selector" :value="value" :disabled="disabled" />' },
    Select: { props: ['value', 'disabled', 'options'], template: '<div class="select-stub" />' },
  }

  async function mountPage() {
    const { default: Subscriptions } = await import('../../../src/pages/notification/Subscriptions.vue')
    const wrapper = mount(Subscriptions, { global: { stubs: formStubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))
    return wrapper
  }

  it('★ shows the user, channel and category of an edited row without letting them change', async () => {
    const wrapper = await mountPage()
    const crud = (wrapper.vm as unknown as { crud: { openEdit: (r: unknown) => void } }).crud

    crud.openEdit({ id: 's1', userId: 'u1', channel: 'Email', category: 'Marketing', isEnabled: true })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    const locked = wrapper.findAll('input')
      .filter((i) => i.attributes('disabled') !== undefined)
      .map((i) => (i.element as HTMLInputElement).value)
    expect(locked).toEqual(['u1', 'Email', 'Marketing'])
    expect(wrapper.find('.user-selector').exists()).toBe(false)
  })

  it('a new preference takes its user, channel and category as input', async () => {
    const wrapper = await mountPage()
    const crud = (wrapper.vm as unknown as { crud: { openCreate: () => void } }).crud

    crud.openCreate()
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    expect(wrapper.find('.user-selector').exists()).toBe(true)
    expect(wrapper.findAll('input').some((i) => i.attributes('disabled') !== undefined)).toBe(false)
  })
})

describe('notificationSubscriptionColumns config', () => {
  it('exports columns with required keys aligned to NotificationPreferenceDto', async () => {
    const { notificationSubscriptionColumns } = await import('../../../src/pages/notification/subscription-config')
    const keys = notificationSubscriptionColumns.map((c) => c.key)
    expect(keys).toContain('userId')
    expect(keys).toContain('channel')
    expect(keys).toContain('category')
    expect(keys).toContain('isEnabled')
    expect(keys).toContain('quietHoursStart')
    expect(keys).toContain('maxFrequencyPerHour')
  })

  it('exports formSchema with required fields', async () => {
    const { notificationSubscriptionFormSchema } = await import('../../../src/pages/notification/subscription-config')
    const keys = notificationSubscriptionFormSchema.map((f) => f.key)
    expect(keys).toContain('userId')
    expect(keys).toContain('channel')
    expect(keys).toContain('category')
    expect(keys).toContain('isEnabled')
  })
})
