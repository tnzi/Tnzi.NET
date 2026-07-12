import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TChatHost from '../../../src/components/chat/TChatHost.vue'

// --- Fake bridge ---
const fakeBridge = {
  listConversations: vi.fn().mockResolvedValue([]),
  getUnreadCount: vi.fn().mockResolvedValue(0),
  getOrCreateDirect: vi.fn(),
  getConversation: vi.fn(),
  getMessages: vi.fn().mockResolvedValue({ messages: [], hasMore: false }),
  sendMessage: vi.fn(),
  markRead: vi.fn().mockResolvedValue(undefined),
  mute: vi.fn(),
  deleteMessage: vi.fn(),
  createGroup: vi.fn(),
  addMembers: vi.fn(),
  removeMember: vi.fn(),
  renameGroup: vi.fn(),
  dissolveGroup: vi.fn(),
  leaveGroup: vi.fn(),
  searchContacts: vi.fn().mockResolvedValue([]),
}

const mockRealtimeStart = vi.fn().mockResolvedValue(undefined)
const mockRealtimeStop = vi.fn().mockResolvedValue(undefined)

vi.mock('../../../src/services/bridges/chat-im-bridge', () => ({
  createChatImBridge: () => fakeBridge,
}))

vi.mock('../../../src/headless/useChatRealtime', () => ({
  useChatRealtime: () => ({ start: mockRealtimeStart, stop: mockRealtimeStop }),
}))

vi.mock('../../../src/headless/useChatSound', () => ({
  useChatSound: () => ({ configure: vi.fn(), playNotification: vi.fn(), playMessage: vi.fn(), preview: vi.fn() }),
}))

vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({ isSm: { value: false } }),
}))

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

// Client mock — toggled per test
let mockClient: object | undefined = {}
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: (required?: boolean) => {
    if (required === false) return mockClient
    if (!mockClient) throw new Error('no client')
    return mockClient
  },
  TNZI_ADMIN_CLIENT_KEY: Symbol('tnzi-admin-client'),
}))

const globalStubs = {
  stubs: {
    TChatLauncher: { template: '<button class="t-chat-launcher-stub" />', props: ['unreadCount'], emits: ['open'] },
    TChatWindow: { template: '<div class="t-chat-window-stub" />', props: ['show'], emits: ['update:show'] },
    NModal: { template: '<div><slot/></div>', props: ['show'] },
  },
}

describe('TChatHost', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    mockClient = {}
    fakeBridge.listConversations.mockResolvedValue([])
    mockRealtimeStart.mockResolvedValue(undefined)
    mockRealtimeStop.mockResolvedValue(undefined)
  })

  it('with client: inits store, calls fetchConversations and realtime.start on mount', async () => {
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [createPinia()] } })
    await vi.waitFor(() => {
      expect(fakeBridge.listConversations).toHaveBeenCalled()
      expect(mockRealtimeStart).toHaveBeenCalled()
    })
    wrapper.unmount()
  })

  it('with client: renders TChatLauncher', () => {
    setActivePinia(createPinia())
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [] } })
    expect(wrapper.find('.t-chat-launcher-stub').exists()).toBe(true)
    wrapper.unmount()
  })

  it('without client: renders nothing and does NOT start realtime', () => {
    mockClient = undefined
    setActivePinia(createPinia())
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [] } })
    expect(wrapper.find('.t-chat-launcher-stub').exists()).toBe(false)
    expect(fakeBridge.listConversations).not.toHaveBeenCalled()
    expect(mockRealtimeStart).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('calls realtime.stop on unmount', async () => {
    const wrapper = mount(TChatHost, { global: { ...globalStubs, plugins: [createPinia()] } })
    await vi.waitFor(() => expect(fakeBridge.listConversations).toHaveBeenCalled())
    wrapper.unmount()
    expect(mockRealtimeStop).toHaveBeenCalled()
  })
})
