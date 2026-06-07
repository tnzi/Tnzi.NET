import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/chat-bridge', () => ({
  createChatBridge: () => ({
    sessions: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => ({ id: 'new', title: 't', status: 1, participants: [], messageCount: 0, creationTime: '' })),
      update: vi.fn(async () => ({ id: 'new', title: 't', status: 1, participants: [], messageCount: 0, creationTime: '' })),
      delete: vi.fn(async () => undefined),
    },
    messages: {
      fetch: vi.fn(async () => ({
        items: [],
        totalCount: 0,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => undefined),
      fetchBySession: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
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

describe('Sessions page (Phase 3.29)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Sessions } = await import('../../../src/pages/chat/Sessions.vue')
    const wrapper = mount(Sessions, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders a data table on load', async () => {
    const { default: Sessions } = await import('../../../src/pages/chat/Sessions.vue')
    const wrapper = mount(Sessions, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('chatSessionColumns config', () => {
  it('exports columns with required keys', async () => {
    const { chatSessionColumns } = await import('../../../src/pages/chat/session-config')
    const keys = chatSessionColumns.map((c) => c.key)
    expect(keys).toContain('title')
    expect(keys).toContain('participants')
    expect(keys).toContain('messageCount')
    expect(keys).toContain('lastMessageAt')
    expect(keys).toContain('status')
  })

  it('exports formSchema with required fields', async () => {
    const { chatSessionFormSchema } = await import('../../../src/pages/chat/session-config')
    const keys = chatSessionFormSchema.map((f) => f.key)
    expect(keys).toContain('title')
    expect(keys).toContain('description')
    expect(keys).toContain('status')
  })
})
