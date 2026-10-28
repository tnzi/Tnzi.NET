import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Enum fields are now PascalCase member-name strings (global JsonStringEnumConverter).
const fetch = vi.fn(async () => ({
  items: [
    {
      id: 'c1', type: 'Group', title: 'Team Alpha', ownerId: 'o1', ownerName: 'Bob',
      memberCount: 3, lastMessagePreview: 'hello', lastMessageAt: null, creationTime: '2026-01-01T00:00:00Z',
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
  totalPages: 1,
  hasPreviousPage: false,
  hasNextPage: false,
}))
const detail = vi.fn(async () => ({
  id: 'c1', type: 'Group', title: 'Team Alpha', notice: null, ownerId: 'o1', ownerName: 'Bob',
  directKey: null, memberCount: 3, messageCount: 2, lastMessageAt: null, creationTime: '2026-01-01T00:00:00Z',
  members: [
    { userId: 'o1', name: 'Bob', role: 'Owner', alias: null, unreadCount: 0, lastReadAt: null, joinedAt: '2026-01-01T00:00:00Z' },
  ],
}))
const messages = vi.fn(async () => ({
  messages: [
    { id: 'm1', conversationId: 'c1', senderId: 'o1', senderName: 'Bob', contentType: 'Text', content: 'hello world', sentAt: '2026-01-01T00:00:00Z' },
  ],
  hasMore: false,
}))
const del = vi.fn(async () => undefined)
const deleteMessage = vi.fn(async () => undefined)
// The embedded BroadcastDialog pulls broadcast / broadcasts off the same chat bridge.
const broadcast = vi.fn(async () => 1)
const broadcasts = vi.fn(async () => ({
  items: [], totalCount: 0, pageIndex: 1, pageSize: 5, totalPages: 0, hasPreviousPage: false, hasNextPage: false,
}))

vi.mock('../../../src/services/bridges/chat-bridge', () => ({
  createChatBridge: () => ({
    conversations: { fetch, detail, messages, delete: del },
    deleteMessage,
    broadcast,
    broadcasts,
  }),
}))
// BroadcastDialog (role/user pickers) builds an identity bridge - mock it so the
// embedded dialog mounts cleanly.
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    roles: { getAll: vi.fn(async () => []) },
    users: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20, totalPages: 0, hasPreviousPage: false, hasNextPage: false })) },
  }),
}))

import Conversations from '../../../src/pages/chat/Conversations.vue'

describe('Chat Conversations page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetch.mockClear()
    detail.mockClear()
    messages.mockClear()
    broadcasts.mockClear()
  })

  it('mounts and fetches the conversation list', async () => {
    const wrapper = mount(Conversations)
    await flushPromises()
    expect(fetch).toHaveBeenCalled()
    expect(wrapper.text()).toContain('Team Alpha')
  })

  it('has a Broadcast toolbar button that opens the dialog', async () => {
    const wrapper = mount(Conversations)
    await flushPromises()
    const btn = wrapper.findAll('button').find((b) => b.text().includes('Broadcast'))
    expect(btn).toBeTruthy()
    await btn!.trigger('click')
    expect((wrapper.vm as unknown as { broadcastShow: boolean }).broadcastShow).toBe(true)
  })

  // Opening A and then B before A answers: A's slower answer must not land
  // under B's drawer, where a recall would then target A's message.
  it('a slower answer for the conversation opened first does not replace the one opened after it', async () => {
    let releaseFirst: () => void = () => undefined
    detail.mockImplementationOnce(() => new Promise((resolve) => {
      releaseFirst = () => resolve({
        id: 'c1', type: 'Group', title: 'Team Alpha', notice: null, ownerId: 'o1', ownerName: 'Bob',
        directKey: null, memberCount: 3, messageCount: 2, lastMessageAt: null, creationTime: '2026-01-01T00:00:00Z',
        members: [],
      })
    }) as never)
    detail.mockImplementationOnce(async () => ({
      id: 'c2', type: 'Direct', title: 'Carol', notice: null, ownerId: 'o2', ownerName: 'Carol',
      directKey: 'k', memberCount: 2, messageCount: 1, lastMessageAt: null, creationTime: '2026-01-02T00:00:00Z',
      members: [],
    }) as never)
    const wrapper = mount(Conversations)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: { openView: (row: unknown) => void }
      detail: { id: string } | null
    }
    vm.crud.openView({ id: 'c1', title: 'Team Alpha' })
    await flushPromises()
    vm.crud.openView({ id: 'c2', title: 'Carol' })
    await flushPromises()
    expect(vm.detail?.id).toBe('c2')

    releaseFirst()
    await flushPromises()
    expect(vm.detail?.id).toBe('c2')
    expect(messages).not.toHaveBeenCalledWith('c1', expect.anything())
  })
})
