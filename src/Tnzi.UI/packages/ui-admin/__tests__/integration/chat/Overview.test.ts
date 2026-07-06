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

const statistics = vi.fn(async () => ({
  totalConversations: 12,
  directConversations: 7,
  groupConversations: 4,
  systemConversations: 1,
  totalMessages: 340,
  messagesToday: 25,
  activeMembers: 30,
  onlineUsers: 5,
}))
const presence = vi.fn(async () => ({
  total: 2,
  online: 1,
  away: 1,
  busy: 0,
  offline: 0,
  users: [
    { userId: 'u1', name: 'Alice', intentStatus: 'Online', effectiveStatus: 'Online', hasConnection: true, lastSeenAt: null, lastChangedAt: null },
    { userId: 'u2', name: 'Bob', intentStatus: 'Away', effectiveStatus: 'Away', hasConnection: true, lastSeenAt: null, lastChangedAt: null },
  ],
}))
vi.mock('../../../src/services/bridges/chat-bridge', () => ({
  createChatBridge: () => ({ statistics, presence }),
}))

import Overview from '../../../src/pages/chat/Overview.vue'

describe('Chat Overview page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    statistics.mockClear()
    presence.mockClear()
  })

  it('mounts, loads statistics + presence, and lists online users', async () => {
    const wrapper = mount(Overview)
    await flushPromises()
    expect(statistics).toHaveBeenCalledTimes(1)
    expect(presence).toHaveBeenCalledTimes(1)
    // KPI labels render (numeric values use an animated counter, so assert labels).
    expect(wrapper.text()).toContain('Total Conversations')
    expect(wrapper.text()).toContain('Online Users')
    // Presence table rows render.
    expect(wrapper.text()).toContain('Alice')
    expect(wrapper.text()).toContain('Bob')
  })
})
