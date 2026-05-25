import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/chat-bridge', () => ({
  createChatBridge: () => ({
    sessions: {
      fetch: vi.fn(async () => { throw new Error('backend gap') }),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
    },
    messages: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'm1',
            title: 'Hello team',
            messageType: 1,
            senderId: 'u1',
            senderName: 'Alice',
            canReply: true,
            creationTime: '2026-01-01T00:00:00Z',
            isRead: false,
            replyCount: 2,
            isImportant: false,
          },
        ],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => undefined),
      fetchBySession: vi.fn(async () => ({
        items: [],
        totalCount: 0,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: { sessionId: 'sess-abc' } }),
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

describe('ChatMessage page (Phase 3.30)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: ChatMessage } = await import('../../src/pages/chat/ChatMessage.vue')
    const wrapper = mount(ChatMessage, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders a data table on load', async () => {
    const { default: ChatMessage } = await import('../../src/pages/chat/ChatMessage.vue')
    const wrapper = mount(ChatMessage, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('chatMessageColumns config', () => {
  it('exports columns with required keys', async () => {
    // Fields aligned with backend MessageListItemDto (Tnzi.Chat in-app messages):
    // title / messageType / senderName / isImportant / isRead / replyCount / creationTime.
    const { chatMessageColumns } = await import('../../src/pages/chat/chat-message-config')
    const keys = chatMessageColumns.map((c) => c.key)
    expect(keys).toContain('title')
    expect(keys).toContain('messageType')
    expect(keys).toContain('senderName')
    expect(keys).toContain('creationTime')
  })

  it('exports formSchema with required fields', async () => {
    const { chatMessageFormSchema } = await import('../../src/pages/chat/chat-message-config')
    const keys = chatMessageFormSchema.map((f) => f.key)
    expect(keys).toContain('title')
    expect(keys).toContain('content')
    expect(keys).toContain('messageType')
  })
})
